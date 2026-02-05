using System.Text.Json.Serialization;

namespace peeku.Mcp;

internal static class PeekuMcpResponse
{
  internal static object Build(string toolName, bool ok, ResultMeta meta, object? payload, PeekuError? error)
  {
    if (payload is ResultBase rb)
    {
      return rb;
    }

    if (string.Equals(toolName, "peeku_observe", StringComparison.Ordinal))
    {
      return new ObserveEnvelope(ok, meta, payload as IReadOnlyList<ObservationEvent> ?? Array.Empty<ObservationEvent>(), error);
    }

    return new GenericEnvelope(ok, meta, payload, error);
  }

  private sealed record GenericEnvelope(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("traceId")] string TraceId,
    [property: JsonPropertyName("meta")] ResultMeta Meta,
    [property: JsonPropertyName("result")] object? Result,
    [property: JsonPropertyName("error")] PeekuError? Error)
  {
    internal GenericEnvelope(bool ok, ResultMeta meta, object? result, PeekuError? error)
      : this(ok, meta.TraceId, meta, result, error)
    {
    }
  }

  private sealed record ObserveEnvelope(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("traceId")] string TraceId,
    [property: JsonPropertyName("meta")] ResultMeta Meta,
    [property: JsonPropertyName("events")] IReadOnlyList<ObservationEvent> Events,
    [property: JsonPropertyName("error")] PeekuError? Error)
  {
    internal ObserveEnvelope(bool ok, ResultMeta meta, IReadOnlyList<ObservationEvent> events, PeekuError? error)
      : this(ok, meta.TraceId, meta, events, error)
    {
    }
  }
}
