using System.IO.Pipes;
using System.Text;

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

  public DaemonServer(string pipeName, JsonRpcConnection connection, DaemonShutdown shutdown)
  {
    if (string.IsNullOrWhiteSpace(pipeName))
    {
      throw new ArgumentException("Pipe name is required", nameof(pipeName));
    }

    _pipeName = pipeName;
    _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    _shutdown = shutdown ?? throw new ArgumentNullException(nameof(shutdown));
    _encoding = new UTF8Encoding(false);
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

      await server.WaitForConnectionAsync(ct).ConfigureAwait(false);

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
}
