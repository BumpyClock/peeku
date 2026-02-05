using System.Text.Json;
using peeku;
using peeku.Mcp;
using Xunit;

namespace peeku.Mcp.Tests;

public sealed class PeekuMcpResponseTests
{
  [Fact]
  public void Build_WhenPayloadIsResultBase_ReturnsPayload()
  {
    var res = new ActionResult(
      Ok: true,
      Meta: new ResultMeta("t", DateTimeOffset.UnixEpoch, 1),
      MethodUsed: ActionMethod.Uia);

    var built = PeekuMcpResponse.Build("peeku_click", ok: true, res.Meta, res, error: null);
    Assert.Same(res, built);
  }

  [Fact]
  public void Build_WhenObserve_WrapsEvents()
  {
    var meta = new ResultMeta("t", DateTimeOffset.UnixEpoch, 1);
    var payload = new[]
    {
      new ObservationEvent(DateTimeOffset.UnixEpoch, "focusChanged"),
    };

    var built = PeekuMcpResponse.Build("peeku_observe", ok: true, meta, payload, error: null);
    using var doc = JsonDocument.Parse(JsonSerializer.Serialize(built));

    Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
    Assert.Equal("t", doc.RootElement.GetProperty("traceId").GetString());
    Assert.True(doc.RootElement.TryGetProperty("events", out var eventsEl));
    Assert.Equal(JsonValueKind.Array, eventsEl.ValueKind);
    Assert.Equal(1, eventsEl.GetArrayLength());
  }

  [Fact]
  public void Build_WhenGeneric_WrapsResult()
  {
    var meta = new ResultMeta("t", DateTimeOffset.UnixEpoch, 1);
    var built = PeekuMcpResponse.Build("peeku_windows_focused", ok: true, meta, new { x = 1 }, error: null);
    using var doc = JsonDocument.Parse(JsonSerializer.Serialize(built));

    Assert.True(doc.RootElement.TryGetProperty("result", out var resultEl));
    Assert.Equal(1, resultEl.GetProperty("x").GetInt32());
  }
}

