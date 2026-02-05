using System.Text;
using System.Text.Json;

namespace peeku.Daemon;

/// <summary>
/// Parses and serializes JSON-RPC 2.0 messages using JSONL framing.
/// </summary>
/// <example>
/// <code>
/// var codec = new JsonRpcCodec(new JsonSerializerOptions());
/// var parsed = codec.ParseRequest("{\"jsonrpc\":\"2.0\",\"method\":\"server.ping\"}");
/// </code>
/// </example>
public sealed class JsonRpcCodec
{
  private readonly JsonSerializerOptions _payloadOptions;

  public JsonRpcCodec(JsonSerializerOptions payloadOptions)
  {
    _payloadOptions = payloadOptions ?? throw new ArgumentNullException(nameof(payloadOptions));
  }

  public JsonRpcParseResult ParseRequest(string line)
  {
    if (line is null)
    {
      throw new ArgumentNullException(nameof(line));
    }

    if (string.IsNullOrWhiteSpace(line))
    {
      return new JsonRpcParseResult(new JsonRpcError(-32600, "Request is empty"), CreateNullId());
    }

    try
    {
      using var doc = JsonDocument.Parse(line);
      var root = doc.RootElement;
      if (root.ValueKind != JsonValueKind.Object)
      {
        return InvalidRequest("Request must be an object", hasId: false, id: default);
      }

      var (hasId, idValid, id) = ReadId(root);
      if (hasId && !idValid)
      {
        return InvalidRequest("Request id is invalid", hasId: false, id: default);
      }

      if (!TryGetString(root, "jsonrpc", out var version) || !string.Equals(version, "2.0", StringComparison.Ordinal))
      {
        return InvalidRequest("Invalid jsonrpc version", hasId, id);
      }

      if (!TryGetString(root, "method", out var method))
      {
        return InvalidRequest("Request method is required", hasId, id);
      }

      var hasParams = TryGetProperty(root, "params", out var paramsElement);
      var clonedParams = hasParams ? paramsElement.Clone() : default;

      return new JsonRpcParseResult(new JsonRpcRequest(method, hasId, id, hasParams, clonedParams));
    }
    catch (JsonException)
    {
      return new JsonRpcParseResult(new JsonRpcError(-32600, "Invalid JSON"), CreateNullId());
    }
  }

  public string SerializeSuccess(JsonElement id, object result)
  {
    if (result is null)
    {
      throw new ArgumentNullException(nameof(result));
    }

    using var stream = new MemoryStream();
    using var writer = new Utf8JsonWriter(stream);
    writer.WriteStartObject();
    writer.WriteString("jsonrpc", "2.0");
    writer.WritePropertyName("id");
    id.WriteTo(writer);
    writer.WritePropertyName("result");
    JsonSerializer.Serialize(writer, result, _payloadOptions);
    writer.WriteEndObject();
    writer.Flush();
    return Encoding.UTF8.GetString(stream.ToArray());
  }

  public string SerializeError(JsonElement id, JsonRpcError error)
  {
    if (error is null)
    {
      throw new ArgumentNullException(nameof(error));
    }

    using var stream = new MemoryStream();
    using var writer = new Utf8JsonWriter(stream);
    writer.WriteStartObject();
    writer.WriteString("jsonrpc", "2.0");
    writer.WritePropertyName("id");
    id.WriteTo(writer);
    writer.WritePropertyName("error");
    JsonSerializer.Serialize(writer, error, _payloadOptions);
    writer.WriteEndObject();
    writer.Flush();
    return Encoding.UTF8.GetString(stream.ToArray());
  }

  private JsonRpcParseResult InvalidRequest(string message, bool hasId, JsonElement id)
  {
    var errorId = hasId ? id : CreateNullId();
    return new JsonRpcParseResult(new JsonRpcError(-32600, message), errorId);
  }

  private (bool HasId, bool IdValid, JsonElement Id) ReadId(JsonElement root)
  {
    if (!TryGetProperty(root, "id", out var id))
    {
      return (false, true, default);
    }

    if (id.ValueKind == JsonValueKind.String || id.ValueKind == JsonValueKind.Number || id.ValueKind == JsonValueKind.Null)
    {
      return (true, true, id.Clone());
    }

    return (true, false, default);
  }

  private bool TryGetString(JsonElement obj, string name, out string value)
  {
    if (!TryGetProperty(obj, name, out var element))
    {
      value = "";
      return false;
    }

    if (element.ValueKind != JsonValueKind.String)
    {
      value = "";
      return false;
    }

    value = element.GetString() ?? "";
    return !string.IsNullOrWhiteSpace(value);
  }

  private bool TryGetProperty(JsonElement obj, string name, out JsonElement value)
  {
    foreach (var prop in obj.EnumerateObject())
    {
      if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
      {
        value = prop.Value;
        return true;
      }
    }

    value = default;
    return false;
  }

  private JsonElement CreateNullId()
  {
    using var doc = JsonDocument.Parse("null");
    return doc.RootElement.Clone();
  }
}
