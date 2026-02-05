using System.Reflection;
using System.Text.Json;
using peeku;

namespace peeku.Daemon;

/// <summary>
/// Routes JSON-RPC requests to daemon handlers.
/// </summary>
/// <example>
/// <code>
/// var dispatcher = new JsonRpcDispatcher(new JsonSerializerOptions());
/// using var session = new DaemonSession();
/// var result = await dispatcher.DispatchAsync(new JsonRpcRequest("server.ping", false, default, false, default), session, CancellationToken.None);
/// </code>
/// </example>
public sealed class JsonRpcDispatcher
{
  private readonly JsonSerializerOptions _deserializeOptions;
  private readonly string _buildVersion;

  public JsonRpcDispatcher(JsonSerializerOptions deserializeOptions)
  {
    _deserializeOptions = deserializeOptions ?? throw new ArgumentNullException(nameof(deserializeOptions));
    _buildVersion = ResolveBuildVersion();
  }

  public Task<JsonRpcDispatchResult> DispatchAsync(JsonRpcRequest request, DaemonSession session, CancellationToken ct)
  {
    if (request is null)
    {
      throw new ArgumentNullException(nameof(request));
    }

    if (session is null)
    {
      throw new ArgumentNullException(nameof(session));
    }

    return session.ExecuteAsync((client, token) => DispatchOnActorAsync(request, client, token), ct);
  }

  private async Task<JsonRpcDispatchResult> DispatchOnActorAsync(JsonRpcRequest request, IPeekuClient client, CancellationToken ct)
  {
    var scope = Results.Start();

    try
    {
      ct.ThrowIfCancellationRequested();

      if (string.Equals(request.Method, "server.ping", StringComparison.Ordinal))
      {
        return new JsonRpcDispatchResult(new ServerOkResult(true, scope.Meta()));
      }

      if (string.Equals(request.Method, "server.capabilities", StringComparison.Ordinal))
      {
        var caps = new ServerCapabilities("1", _buildVersion, false, false);
        return new JsonRpcDispatchResult(caps);
      }

      if (string.Equals(request.Method, "server.shutdown", StringComparison.Ordinal))
      {
        return new JsonRpcDispatchResult(new ServerOkResult(true, scope.Meta()), shutdownRequested: true);
      }

      if (string.Equals(request.Method, "peeku.batch", StringComparison.Ordinal))
      {
        if (!request.HasParams || request.Params.ValueKind != JsonValueKind.Object)
        {
          return new JsonRpcDispatchResult(new JsonRpcError(-32602, "Invalid params", new { method = request.Method, reason = "Batch params are required" }));
        }

        var batch = JsonSerializer.Deserialize<BatchRequest>(request.Params, _deserializeOptions);
        if (batch is null)
        {
          return new JsonRpcDispatchResult(new JsonRpcError(-32602, "Invalid params", new { method = request.Method, reason = "Batch params are invalid" }));
        }

        var result = await client.BatchAsync(batch, ct).ConfigureAwait(false);
        return new JsonRpcDispatchResult(result);
      }

      return new JsonRpcDispatchResult(new JsonRpcError(-32601, "Method not found", new { method = request.Method }));
    }
    catch (JsonException ex)
    {
      return new JsonRpcDispatchResult(new JsonRpcError(-32602, "Invalid params", new { method = request.Method, error = ex.Message }));
    }
    catch (OperationCanceledException)
    {
      return new JsonRpcDispatchResult(new JsonRpcError(-32603, "Request cancelled", new { method = request.Method }));
    }
    catch (Exception ex)
    {
      return new JsonRpcDispatchResult(new JsonRpcError(-32603, "Internal error", new { method = request.Method, exception = ex.GetType().FullName, ex.Message, ex.HResult }));
    }
  }

  private static string ResolveBuildVersion()
  {
    var version = Assembly.GetExecutingAssembly().GetName().Version;
    var text = version?.ToString() ?? "";
    return string.IsNullOrWhiteSpace(text) ? "0.0.0" : text;
  }
}
