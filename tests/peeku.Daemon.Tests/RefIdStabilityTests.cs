using System.Text.RegularExpressions;
using peeku;
using peeku.Daemon;
using Xunit;

namespace peeku.Daemon.Tests;

/// <summary>
/// Tests for the refId-stability fix: a snapshot in one dispatch and an action/get in a separate
/// dispatch must resolve against the same warm daemon session (FIX A), and durable uia:pid:hash refs
/// must re-walk the live tree when the cached handle is gone (FIX B).
/// </summary>
/// <example>
/// <code>
/// using var session = new DaemonSession();
/// var snap = await session.ExecuteAsync((c, t) => c.UiaSnapshotAsync(new UiaSnapshotRequest(new Target.Desktop(), Depth: 1), t), CancellationToken.None);
/// var get = await session.ExecuteAsync((c, t) => c.ElementGetAsync(new ElementGetRequest(Element: snap.Root.Element), t), CancellationToken.None);
/// </code>
/// </example>
public sealed class RefIdStabilityTests
{
  private static readonly Regex DurableRefId = new("^uia:\\d+:[0-9a-f]+$", RegexOptions.Compiled);

  // -------- FIX A: singleton session keeps the cache alive across dispatches --------

  [Fact]
  public async Task Snapshot_then_element_get_across_two_dispatches_on_one_session_resolves()
  {
    // The core bug: each CLI command used to get a fresh per-connection session with an empty cache,
    // so a snapshot's refId from one command was unknown to the next. With a shared session the
    // cache survives, so dispatch 2 resolves the refId dispatch 1 produced.
    using var session = new DaemonSession();
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

    var snapshot = await session.ExecuteAsync(
      (client, ct) => client.UiaSnapshotAsync(
        new UiaSnapshotRequest(new Target.Desktop(), Depth: 1, MaxNodes: 200, IncludeProperties: UiaPropertiesMode.Basic),
        ct),
      cts.Token);

    Assert.True(snapshot.Ok, snapshot.Error?.Message);
    var refId = snapshot.Root.Element.RefId;
    Assert.False(string.IsNullOrWhiteSpace(refId));

    var get = await session.ExecuteAsync(
      (client, ct) => client.ElementGetAsync(new ElementGetRequest(Element: new ElementRef(refId)), ct),
      cts.Token);

    Assert.True(get.Ok, get.Error?.Code + ": " + get.Error?.Message);
    Assert.Equal(refId, get.Element.Element.RefId);
  }

  [Fact]
  public async Task Snapshot_in_one_connection_then_element_get_in_a_separate_connection_resolves()
  {
    // The discriminating regression for FIX A: two SEPARATE pipe connections (two ProcessAsync calls)
    // sharing ONE injected session, exactly like two CLI processes hitting the warm daemon. On the
    // pre-FIX-A per-connection `using var session`, connection 2 got a fresh empty cache and this
    // returned ElementNotFound; with the shared session + durable refIds it resolves Ok.
    var shutdown = new DaemonShutdown();
    var options = JsonOptions();
    using var session = new DaemonSession();
    var connection = new JsonRpcConnection(new JsonRpcCodec(options), new JsonRpcDispatcher(options), session, shutdown);
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

    // Connection 1: snapshot the desktop, capture the root refId from the batch result.
    const string snapshotLine = "{\"jsonrpc\":\"2.0\",\"id\":\"1\",\"method\":\"peeku.batch\",\"params\":{\"ops\":[{\"tool\":\"peeku_uia_snapshot\",\"args\":{\"target\":{\"kind\":\"desktop\"},\"depth\":1,\"maxNodes\":200}}],\"stopOnError\":true}}";
    var snapOut = await RunOneRequestAsync(connection, snapshotLine, cts.Token);
    using var snapDoc = System.Text.Json.JsonDocument.Parse(snapOut);
    var snapStep = snapDoc.RootElement.GetProperty("result").GetProperty("results")[0];
    Assert.True(snapStep.GetProperty("ok").GetBoolean(), snapOut);
    var refId = snapStep.GetProperty("result").GetProperty("root").GetProperty("element").GetProperty("refId").GetString();
    Assert.False(string.IsNullOrWhiteSpace(refId));
    Assert.Matches(DurableRefId, refId!);

    // Connection 2: a brand-new ProcessAsync (separate "CLI process") resolves that refId.
    var getLine = "{\"jsonrpc\":\"2.0\",\"id\":\"2\",\"method\":\"peeku.batch\",\"params\":{\"ops\":[{\"tool\":\"peeku_element_get\",\"args\":{\"elementRef\":{\"refId\":\"" + refId + "\"}}}],\"stopOnError\":true}}";
    var getOut = await RunOneRequestAsync(connection, getLine, cts.Token);
    using var getDoc = System.Text.Json.JsonDocument.Parse(getOut);
    var getStep = getDoc.RootElement.GetProperty("result").GetProperty("results")[0];
    Assert.True(getStep.GetProperty("ok").GetBoolean(), getOut);
  }

  [Fact]
  public async Task Snapshot_root_refId_is_a_durable_uia_pid_hash_id()
  {
    using var client = new DaemonPeekuClient();
    var snapshot = await client.UiaSnapshotAsync(
      new UiaSnapshotRequest(new Target.Desktop(), Depth: 1, MaxNodes: 200, IncludeProperties: UiaPropertiesMode.Basic),
      CancellationToken.None);

    Assert.True(snapshot.Ok, snapshot.Error?.Message);
    Assert.Matches(DurableRefId, snapshot.Root.Element.RefId);
    Assert.All(snapshot.Elements, e => Assert.Matches(DurableRefId, e.Element.RefId));
  }

  [Fact]
  public async Task JsonRpcConnection_does_not_dispose_the_injected_session()
  {
    var shutdown = new DaemonShutdown();
    var options = JsonOptions();
    using var session = SessionUnderConnection(options, shutdown, out var connection);
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

    var reader = new StringReader("{\"jsonrpc\":\"2.0\",\"id\":\"1\",\"method\":\"server.ping\"}");
    var writer = new StringWriter();
    await connection.ProcessAsync(reader, writer, cts.Token);

    // The connection finished; the injected session must still be usable (not disposed by it).
    var ok = await session.ExecuteAsync((_, _) => Task.FromResult(42), cts.Token);
    Assert.Equal(42, ok);
  }

  [Fact]
  public async Task Disposing_the_session_at_shutdown_joins_the_actor_thread_and_rejects_new_work()
  {
    var session = new DaemonSession();
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

    // Warm it: prove the actor thread is alive before shutdown.
    var warm = await session.ExecuteAsync((_, _) => Task.FromResult(1), cts.Token);
    Assert.Equal(1, warm);

    var disposeTask = Task.Run(() => session.Dispose());
    var completed = await Task.WhenAny(disposeTask, Task.Delay(TimeSpan.FromSeconds(3)));
    Assert.Same(disposeTask, completed); // Dispose joined the actor thread without hanging.

    // ExecuteAsync throws synchronously (ThrowIfDisposed) before producing a task.
    Assert.Throws<ObjectDisposedException>(() => EnqueueNoop(session));
  }

  private static void EnqueueNoop(DaemonSession session)
    => _ = session.ExecuteAsync((_, _) => Task.FromResult(2), CancellationToken.None);

  // -------- FIX B: durable refs re-walk; ephemeral h: refs fail cleanly --------

  [Fact]
  public async Task A_durable_ref_resolves_on_a_fresh_client_via_re_walk_daemon_restart_sim()
  {
    // Sim a daemon restart: client A snapshots and yields durable refIds; client B starts with an
    // empty cache. Resolving one of A's child-window refs on B must re-walk the live tree (scoped to
    // the ref's pid) and succeed -- no shared cache state required.
    using var clientA = new DaemonPeekuClient();
    var snapshot = await clientA.UiaSnapshotAsync(
      new UiaSnapshotRequest(new Target.Desktop(), Depth: 2, MaxNodes: 500, IncludeProperties: UiaPropertiesMode.Basic),
      CancellationToken.None);

    Assert.True(snapshot.Ok, snapshot.Error?.Message);

    var childRefIds = ChildWindowRefIds(snapshot);
    Assert.NotEmpty(childRefIds); // Requires at least one top-level window on the desktop.

    using var clientB = new DaemonPeekuClient();
    var resolved = false;
    foreach (var refId in childRefIds)
    {
      var get = await clientB.ElementGetAsync(
        new ElementGetRequest(Element: new ElementRef(refId)),
        CancellationToken.None);
      if (get.Ok)
      {
        Assert.Equal(refId, get.Element.Element.RefId);
        resolved = true;
        break;
      }
    }

    Assert.True(resolved, "Expected at least one durable child-window ref to resolve via re-walk on a fresh client.");
  }

  [Fact]
  public async Task A_durable_ref_resolves_via_re_walk_after_the_cache_evicts_it()
  {
    // Tiny capacity + zero TTL: the snapshot's handles are evicted/expired immediately, so the
    // follow-up element.get cannot hit the cache and must fall through to the durable re-walk.
    using var client = new DaemonPeekuClient(handleCacheCapacity: 1, handleCacheTtl: TimeSpan.Zero);
    var snapshot = await client.UiaSnapshotAsync(
      new UiaSnapshotRequest(new Target.Desktop(), Depth: 2, MaxNodes: 500, IncludeProperties: UiaPropertiesMode.Basic),
      CancellationToken.None);

    Assert.True(snapshot.Ok, snapshot.Error?.Message);

    var childRefIds = ChildWindowRefIds(snapshot);
    Assert.NotEmpty(childRefIds);

    var resolved = false;
    foreach (var refId in childRefIds)
    {
      var get = await client.ElementGetAsync(
        new ElementGetRequest(Element: new ElementRef(refId)),
        CancellationToken.None);
      if (get.Ok)
      {
        resolved = true;
        break;
      }
    }

    Assert.True(resolved, "Expected a durable ref to resolve via re-walk after eviction.");
  }

  [Fact]
  public async Task A_stale_h_handle_id_fails_with_a_clean_element_not_found()
  {
    using var client = new DaemonPeekuClient();
    var stale = "h:" + Guid.NewGuid().ToString("N");

    var get = await client.ElementGetAsync(
      new ElementGetRequest(Element: new ElementRef(stale)),
      CancellationToken.None);

    Assert.False(get.Ok);
    Assert.NotNull(get.Error);
    Assert.Equal("ElementNotFound", get.Error!.Code);
    Assert.Equal("Element handle not found.", get.Error.Message);
  }

  // -------- FIX B: same-process selector paths must not regress to "handle not available" --------

  [Fact]
  public async Task Selector_to_get_in_one_process_resolves_after_durable_ids()
  {
    // Helpers/FindGet selector->cached branches used raw _handles.TryGet, which returns false for the
    // now-durable uia: ids: this would regress to "Element handle not available." even in the same
    // daemon. element.get --selector against the desktop must resolve a top-level window.
    using var client = new DaemonPeekuClient();
    var get = await client.ElementGetAsync(
      new ElementGetRequest(Selector: new Selector("window"), Target: new Target.Desktop()),
      CancellationToken.None);

    Assert.True(get.Ok, get.Error?.Code + ": " + get.Error?.Message);
    Assert.NotEqual("Element handle not available.", get.Error?.Message);
  }

  [Fact]
  public async Task Selector_to_action_in_one_process_does_not_fail_resolution()
  {
    // act --selector hits the same Helpers selector->cached branch. The action itself may fail for a
    // window (e.g. no Invoke pattern), but resolution must NOT return "Element handle not available."
    using var client = new DaemonPeekuClient();
    var act = await client.ClickAsync(
      new ClickRequest(Selector: new Selector("window"), Target: new Target.Desktop()),
      CancellationToken.None);

    Assert.NotEqual("Element handle not available.", act.Error?.Message);
  }

  // -------- helpers --------

  private static IReadOnlyList<string> ChildWindowRefIds(UiaSnapshotResult snapshot)
  {
    var children = snapshot.Root.Children;
    if (children is null)
    {
      return Array.Empty<string>();
    }

    var ids = new List<string>(children.Count);
    foreach (var child in children)
    {
      var refId = child.Element.RefId;
      if (!string.IsNullOrWhiteSpace(refId))
      {
        ids.Add(refId);
      }
    }

    return ids;
  }

  // Drives one request through a connection as a self-contained "pipe connection": ProcessAsync reads
  // the single line, responds, then hits EOF and returns -- modelling one CLI process per call while
  // the injected session lives across calls.
  private static async Task<string> RunOneRequestAsync(JsonRpcConnection connection, string requestLine, CancellationToken ct)
  {
    var reader = new StringReader(requestLine);
    var writer = new StringWriter();
    await connection.ProcessAsync(reader, writer, ct);
    return writer.ToString().Trim();
  }

  private static DaemonSession SessionUnderConnection(System.Text.Json.JsonSerializerOptions options, DaemonShutdown shutdown, out JsonRpcConnection connection)
  {
    var session = new DaemonSession();
    connection = new JsonRpcConnection(new JsonRpcCodec(options), new JsonRpcDispatcher(options), session, shutdown);
    return session;
  }

  private static System.Text.Json.JsonSerializerOptions JsonOptions()
    => new()
    {
      PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
      DictionaryKeyPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
      DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
      PropertyNameCaseInsensitive = true,
      WriteIndented = false,
    };
}
