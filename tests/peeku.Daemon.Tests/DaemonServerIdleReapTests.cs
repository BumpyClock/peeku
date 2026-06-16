using System.Text.Json;
using System.Text.Json.Serialization;
using peeku.Daemon;
using Xunit;

namespace peeku.Daemon.Tests;

/// <summary>
/// Tests for DaemonServer idle-timeout self-reap behavior.
/// </summary>
public sealed class DaemonServerIdleReapTests
{
  // (a) Server with a short idle timeout and no connecting client completes RunAsync
  //     and sets shutdown.IsRequested.
  [Fact]
  public async Task RunAsync_completes_on_idle_timeout_when_no_client_connects()
  {
    var shutdown = new DaemonShutdown();
    var server = CreateServer(UniquePipe(), shutdown, idleTimeout: TimeSpan.FromMilliseconds(150));

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

    // Should return on its own — no client connects.
    await server.RunAsync(cts.Token).WaitAsync(cts.Token);

    Assert.True(shutdown.IsRequested);
  }

  // (b) With a markerPath whose marker names THIS process, idle reap deletes it.
  [Fact]
  public async Task RunAsync_deletes_own_marker_file_on_idle_reap()
  {
    var markerPath = UniqueMarkerPath();
    WriteMarker(markerPath, Environment.ProcessId);
    try
    {
      var shutdown = new DaemonShutdown();
      var server = CreateServer(
        UniquePipe(),
        shutdown,
        idleTimeout: TimeSpan.FromMilliseconds(150),
        markerPath: markerPath);

      using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

      await server.RunAsync(cts.Token).WaitAsync(cts.Token);

      Assert.True(shutdown.IsRequested);
      Assert.False(File.Exists(markerPath), "Own marker should have been deleted on idle reap.");
    }
    finally
    {
      TryDelete(markerPath);
    }
  }

  // (b2) A marker naming a DIFFERENT process must NOT be deleted (ownership guard) — otherwise a
  //      reaping daemon could nuke a newer daemon's marker (TOCTOU).
  [Fact]
  public async Task RunAsync_does_not_delete_foreign_marker_on_idle_reap()
  {
    var markerPath = UniqueMarkerPath();
    WriteMarker(markerPath, foreignPid: int.MaxValue); // never this test process's pid
    try
    {
      var shutdown = new DaemonShutdown();
      var server = CreateServer(
        UniquePipe(),
        shutdown,
        idleTimeout: TimeSpan.FromMilliseconds(150),
        markerPath: markerPath);

      using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

      await server.RunAsync(cts.Token).WaitAsync(cts.Token);

      Assert.True(shutdown.IsRequested);
      Assert.True(File.Exists(markerPath), "A marker owned by another process must be left intact.");
    }
    finally
    {
      TryDelete(markerPath);
    }
  }

  // (c) With idleTimeout = TimeSpan.Zero (persistent), RunAsync does NOT self-complete from idle.
  //     An external CancellationToken cancels it; shutdown.IsRequested stays false.
  [Fact]
  public async Task RunAsync_does_not_self_reap_when_idle_timeout_is_zero()
  {
    var shutdown = new DaemonShutdown();
    var server = CreateServer(UniquePipe(), shutdown, idleTimeout: TimeSpan.Zero);

    // External cancellation after a short wait — simulates Ctrl-C.
    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => server.RunAsync(cts.Token));

    // The server did NOT call shutdown.Request() — it was stopped externally.
    Assert.False(shutdown.IsRequested);
  }

  private static string UniquePipe() => $"peeku.test.idle-reap-{Guid.NewGuid():N}";

  private static string UniqueMarkerPath()
    => Path.Combine(Path.GetTempPath(), $"peeku-idle-reap-{Guid.NewGuid():N}.json");

  private static void WriteMarker(string path, int foreignPid)
    => File.WriteAllText(path, $"{{\"pipeName\":\"peeku.test\",\"pid\":{foreignPid},\"protocolVersion\":\"1\"}}");

  private static void TryDelete(string path)
  {
    try
    {
      if (File.Exists(path))
      {
        File.Delete(path);
      }
    }
    catch
    {
      // test cleanup; ignore
    }
  }

  private static DaemonServer CreateServer(
    string pipeName,
    DaemonShutdown shutdown,
    TimeSpan idleTimeout = default,
    string? markerPath = null)
  {
    var options = new JsonSerializerOptions
    {
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
      DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
      PropertyNameCaseInsensitive = true,
      WriteIndented = false,
    };
    var codec = new JsonRpcCodec(options);
    var dispatcher = new JsonRpcDispatcher(options);
    // DaemonSession is Disposable; we let the GC finalize it here since it's not
    // used during idle (no connection arrives) and this avoids a using-in-lambda.
    var session = new DaemonSession();
    var connection = new JsonRpcConnection(codec, dispatcher, session, shutdown);
    return new DaemonServer(pipeName, connection, shutdown, idleTimeout, markerPath);
  }
}
