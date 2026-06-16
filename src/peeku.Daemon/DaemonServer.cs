using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace peeku.Daemon;

/// <summary>
/// Hosts a named-pipe JSON-RPC server that processes one connection at a time.
/// </summary>
/// <example>
/// <code>
/// var server = new DaemonServer("peeku.user.v1", connection, shutdown);
/// await server.RunAsync(CancellationToken.None);
/// </code>
/// </example>
public sealed class DaemonServer
{
  private readonly string _pipeName;
  private readonly JsonRpcConnection _connection;
  private readonly DaemonShutdown _shutdown;
  private readonly Encoding _encoding;
  private readonly TimeSpan _idleTimeout;
  private readonly string? _markerPath;

  // CancelAfter rejects a delay whose total ms exceeds int.MaxValue (~24.8 days). A hand-passed
  // --idle-timeout of absurd minutes would otherwise throw on the first iteration; clamp it to an
  // effectively-unbounded-but-legal value instead.
  private static readonly TimeSpan MaxIdleTimeout = TimeSpan.FromMilliseconds(int.MaxValue);

  public DaemonServer(
    string pipeName,
    JsonRpcConnection connection,
    DaemonShutdown shutdown,
    TimeSpan idleTimeout = default,
    string? markerPath = null)
  {
    if (string.IsNullOrWhiteSpace(pipeName))
    {
      throw new ArgumentException("Pipe name is required", nameof(pipeName));
    }

    _pipeName = pipeName;
    _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    _shutdown = shutdown ?? throw new ArgumentNullException(nameof(shutdown));
    _encoding = new UTF8Encoding(false);
    _idleTimeout = idleTimeout > TimeSpan.Zero
      ? (idleTimeout > MaxIdleTimeout ? MaxIdleTimeout : idleTimeout)
      : TimeSpan.Zero;
    _markerPath = markerPath;
  }

  public async Task RunAsync(CancellationToken ct)
  {
    while (!ct.IsCancellationRequested && !_shutdown.IsRequested)
    {
      await using var server = new NamedPipeServerStream(
        _pipeName,
        PipeDirection.InOut,
        NamedPipeServerStream.MaxAllowedServerInstances,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous);

      try
      {
        using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (_idleTimeout > TimeSpan.Zero)
        {
          idleCts.CancelAfter(_idleTimeout);
        }

        await server.WaitForConnectionAsync(idleCts.Token).ConfigureAwait(false);
      }
      catch (OperationCanceledException) when (!ct.IsCancellationRequested)
      {
        // Idle timeout fired — no client connected within the window. Reap the daemon.
        TryDeleteOwnMarker();
        _shutdown.Request();
        break;
      }

      if (!server.IsConnected)
      {
        continue;
      }

      using var reader = new StreamReader(server, _encoding, false, 4096, leaveOpen: true);
      using var writer = new StreamWriter(server, _encoding, 4096, leaveOpen: true)
      {
        AutoFlush = true,
        NewLine = "\n",
      };

      await _connection.ProcessAsync(reader, writer, ct).ConfigureAwait(false);
    }
  }

  /// <summary>
  /// Best-effort delete of the marker on idle reap — but ONLY if it still names THIS process. The
  /// marker is CLI-owned and shared at a fixed path; an unconditional delete could remove a newer
  /// daemon's marker if the CLI re-spawned during our teardown (TOCTOU). We are still alive here
  /// (synchronous, pre-exit), so an ownership-matched delete is safe. Any failure is swallowed — the
  /// CLI's own IsAlive()-gated stale-marker GC is the backstop.
  /// </summary>
  private void TryDeleteOwnMarker()
  {
    if (_markerPath is null)
    {
      return;
    }

    try
    {
      if (!File.Exists(_markerPath))
      {
        return;
      }

      using var doc = JsonDocument.Parse(File.ReadAllText(_markerPath));
      if (doc.RootElement.TryGetProperty("pid", out var pidElement)
          && pidElement.TryGetInt32(out var pid)
          && pid == Environment.ProcessId)
      {
        File.Delete(_markerPath);
      }
    }
    catch
    {
      // best-effort; the CLI's stale-marker GC removes it on the next call regardless.
    }
  }
}
