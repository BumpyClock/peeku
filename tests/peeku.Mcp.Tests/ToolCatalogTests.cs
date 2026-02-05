using System.Text.Json;
using peeku;
using peeku.Mcp;
using Xunit;

namespace peeku.Mcp.Tests;

public sealed class ToolCatalogTests
{
  [Fact]
  public void Tools_HaveSameNamesAsToolRegistry()
  {
    var expected = ToolRegistry.All.Select(x => x.Name).ToArray();
    var actual = PeekuMcpToolCatalog.Tools.Select(x => x.Name).ToArray();
    Assert.Equal(expected, actual);
  }

  [Fact]
  public void Tools_ExposeInputAndOutputSchemas()
  {
    foreach (var tool in PeekuMcpToolCatalog.Tools)
    {
      AssertSchemaLooksValid(tool.Name, "input", tool.InputSchema);
      var outputSchema = tool.OutputSchema ?? default;
      Assert.NotEqual(JsonValueKind.Undefined, outputSchema.ValueKind);

      AssertSchemaLooksValid(tool.Name, "output", outputSchema);
      AssertOutputHasOkAndTraceId(tool.Name, outputSchema);
    }
  }

  private static void AssertSchemaLooksValid(string toolName, string schemaKind, JsonElement schema)
  {
    Assert.Equal(JsonValueKind.Object, schema.ValueKind);

    Assert.True(
      schema.TryGetProperty("type", out var typeElement),
      $"{toolName}: {schemaKind} schema missing 'type'");
    Assert.Equal("object", typeElement.GetString());

    Assert.True(
      schema.TryGetProperty("properties", out var propertiesElement),
      $"{toolName}: {schemaKind} schema missing 'properties'");
    Assert.Equal(JsonValueKind.Object, propertiesElement.ValueKind);
  }

  private static void AssertOutputHasOkAndTraceId(string toolName, JsonElement outputSchema)
  {
    Assert.True(
      outputSchema.TryGetProperty("properties", out var propertiesElement),
      $"{toolName}: output schema missing 'properties'");

    Assert.True(
      propertiesElement.TryGetProperty("ok", out _),
      $"{toolName}: output schema missing properties.ok");
    Assert.True(
      propertiesElement.TryGetProperty("traceId", out _),
      $"{toolName}: output schema missing properties.traceId");
  }
}
