namespace peeku.Daemon;

/// <summary>
/// Processes JSON-RPC requests over a text reader and writer pair.
/// </summary>
/// <example>
/// <code>
/// var connection = new JsonRpcConnection(new JsonRpcCodec(new JsonSerializerOptions()), new JsonRpcDispatcher(new JsonSerializerOptions()), new DaemonShutdown());
/// await connection.ProcessAsync(new StringReader("{}"), new StringWriter(), CancellationToken.None);
/// </code>
/// </example>
public sealed class JsonRpcConnection
{
  private readonly JsonRpcCodec _codec;
  private readonly JsonRpcDispatcher _dispatcher;
  private readonly DaemonShutdown _shutdown;

  public JsonRpcConnection(JsonRpcCodec codec, JsonRpcDispatcher dispatcher, DaemonShutdown shutdown)
  {
    _codec = codec ?? throw new ArgumentNullException(nameof(codec));
    _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    _shutdown = shutdown ?? throw new ArgumentNullException(nameof(shutdown));
  }

  public async Task ProcessAsync(TextReader reader, TextWriter writer, CancellationToken ct)
  {
    if (reader is null)
    {
      throw new ArgumentNullException(nameof(reader));
    }

    if (writer is null)
    {
      throw new ArgumentNullException(nameof(writer));
    }

    using var session = new DaemonSession();

    while (!ct.IsCancellationRequested && !_shutdown.IsRequested)
    {
      var line = await reader.ReadLineAsync().WaitAsync(ct).ConfigureAwait(false);
      if (line is null)
      {
        break;
      }

      if (string.IsNullOrWhiteSpace(line))
      {
        continue;
      }

      var parsed = _codec.ParseRequest(line);
      if (!parsed.Ok)
      {
        if (parsed.Error is not null)
        {
          var errorLine = _codec.SerializeError(parsed.ErrorId, parsed.Error);
          await writer.WriteLineAsync(errorLine).WaitAsync(ct).ConfigureAwait(false);
          await writer.FlushAsync().WaitAsync(ct).ConfigureAwait(false);
        }

        continue;
      }

      var request = parsed.Request ?? throw new InvalidOperationException("Parsed request missing after successful parse");
      var dispatch = await _dispatcher.DispatchAsync(request, session, ct).ConfigureAwait(false);

      if (!request.HasId)
      {
        if (dispatch.ShutdownRequested)
        {
          _shutdown.Request();
        }

        continue;
      }

      var responseLine = dispatch.Ok
        ? _codec.SerializeSuccess(request.Id, dispatch.Result ?? throw new InvalidOperationException("Dispatch result missing for success response"))
        : _codec.SerializeError(request.Id, dispatch.Error ?? throw new InvalidOperationException("Dispatch error missing for failure response"));

      await writer.WriteLineAsync(responseLine).WaitAsync(ct).ConfigureAwait(false);
      await writer.FlushAsync().WaitAsync(ct).ConfigureAwait(false);

      if (dispatch.ShutdownRequested)
      {
        _shutdown.Request();
      }
    }
  }
}
