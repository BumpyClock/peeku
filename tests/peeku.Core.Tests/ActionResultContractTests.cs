using System.Text.Json;
using System.Text.Json.Serialization;
using peeku;
using Xunit;

namespace peeku.Core.Tests;

public sealed class ActionResultContractTests
{
  [Fact]
  public void ActionResult_Evidence_RoundTrips_With_Json()
  {
    var evidence = JsonSerializer.SerializeToElement(new
    {
      operation = "set-value",
      status = "matched",
      valuePatternSupported = true,
      expectedValue = "abc",
      actualValue = "abc",
      verificationPerformed = true,
      verificationMatched = true,
    });

    var result = new ActionResult(
      Ok: true,
      Meta: Results.Meta(traceId: "trace-1"),
      MethodUsed: ActionMethod.Uia,
      Evidence: evidence);

    var options = new JsonSerializerOptions
    {
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
      PropertyNameCaseInsensitive = true,
      DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
      WriteIndented = false,
    };

    var json = JsonSerializer.Serialize(result, options);
    var parsed = JsonSerializer.Deserialize<ActionResult>(json, options);

    Assert.NotNull(parsed);
    Assert.True(parsed!.Evidence.HasValue);
    Assert.Equal("set-value", parsed.Evidence.Value.GetProperty("operation").GetString());
    Assert.Equal("matched", parsed.Evidence.Value.GetProperty("status").GetString());
    Assert.True(parsed.Evidence.Value.GetProperty("verificationMatched").GetBoolean());
  }
}
