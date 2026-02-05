using System.Text.Json;
using System.Text.Json.Serialization;

namespace peeku.Daemon;

/// <summary>
/// JSON-RPC request details captured from a single line.
/// </summary>
/// <example>
/// <code>
/// using var doc = JsonDocument.Parse("{\"id\":1}");
/// var request = new JsonRpcRequest("server.ping", true, doc.RootElement.Clone(), false, default);
/// </code>
/// </example>
public sealed class JsonRpcRequest
{
  public JsonRpcRequest(string method, bool hasId, JsonElement id, bool hasParams, JsonElement @params)
  {
    if (string.IsNullOrWhiteSpace(method))
    {
      throw new ArgumentException("Method is required", nameof(method));
    }

    Method = method;
    HasId = hasId;
    Id = id;
    HasParams = hasParams;
    Params = @params;
  }

  public string Method { get; }
  public bool HasId { get; }
  public JsonElement Id { get; }
  public bool HasParams { get; }
  public JsonElement Params { get; }
}

/// <summary>
/// JSON-RPC error object payload.
/// </summary>
/// <example>
/// <code>
/// var error = new JsonRpcError(-32601, "Method not found");
/// </code>
/// </example>
public sealed class JsonRpcError
{
  public JsonRpcError(int code, string message)
  {
    if (string.IsNullOrWhiteSpace(message))
    {
      throw new ArgumentException("Message is required", nameof(message));
    }

    Code = code;
    Message = message;
  }

  public JsonRpcError(int code, string message, object data)
    : this(code, message)
  {
    if (data is null)
    {
      throw new ArgumentNullException(nameof(data));
    }

    Data = data;
  }

  [JsonPropertyName("code")]
  public int Code { get; }

  [JsonPropertyName("message")]
  public string Message { get; }

  [JsonPropertyName("data")]
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public object? Data { get; }
}

/// <summary>
/// Parsing outcome for a JSON-RPC line.
/// </summary>
/// <example>
/// <code>
/// var ok = new JsonRpcParseResult(new JsonRpcRequest("server.ping", false, default, false, default));
/// </code>
/// </example>
public sealed class JsonRpcParseResult
{
  public JsonRpcParseResult(JsonRpcRequest request)
  {
    Request = request ?? throw new ArgumentNullException(nameof(request));
    Ok = true;
  }

  public JsonRpcParseResult(JsonRpcError error, JsonElement errorId)
  {
    Error = error ?? throw new ArgumentNullException(nameof(error));
    ErrorId = errorId;
    Ok = false;
    HasErrorId = true;
  }

  public bool Ok { get; }
  public JsonRpcRequest? Request { get; }
  public JsonRpcError? Error { get; }
  public bool HasErrorId { get; }
  public JsonElement ErrorId { get; }
}

/// <summary>
/// Dispatch outcome for a JSON-RPC request.
/// </summary>
/// <example>
/// <code>
/// var outcome = new JsonRpcDispatchResult(new { ok = true });
/// </code>
/// </example>
public sealed class JsonRpcDispatchResult
{
  public JsonRpcDispatchResult(object result, bool shutdownRequested = false)
  {
    Result = result ?? throw new ArgumentNullException(nameof(result));
    Ok = true;
    ShutdownRequested = shutdownRequested;
  }

  public JsonRpcDispatchResult(JsonRpcError error)
  {
    Error = error ?? throw new ArgumentNullException(nameof(error));
    Ok = false;
  }

  public bool Ok { get; }
  public object? Result { get; }
  public JsonRpcError? Error { get; }
  public bool ShutdownRequested { get; }
}

/// <summary>
/// Server ping/shutdown response payload.
/// </summary>
/// <example>
/// <code>
/// var scope = Results.Start();
/// var result = new ServerOkResult(true, scope.Meta());
/// </code>
/// </example>
public sealed record ServerOkResult(bool Ok, ResultMeta Meta);

/// <summary>
/// Server capability response payload.
/// </summary>
/// <example>
/// <code>
/// var caps = new ServerCapabilities("1", "1.0.0", false, false);
/// </code>
/// </example>
public sealed record ServerCapabilities(
  string ProtocolVersion,
  string BuildVersion,
  bool SupportsHandles,
  bool SupportsStreaming);
