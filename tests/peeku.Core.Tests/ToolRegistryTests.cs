using System.Text.Json;
using peeku;
using Xunit;

namespace peeku.Core.Tests;

public sealed class ToolRegistryTests
{
  [Fact]
  public void All_ToolNames_MatchExpectedList()
  {
    var expected =
      new[]
      {
        "peeku_doctor",
        "peeku_windows_list",
        "peeku_windows_focused",
        "peeku_windows_focus",
        "peeku_capture_image",
        "peeku_uia_snapshot",
        "peeku_see",
        "peeku_find",
        "peeku_element_get",
        "peeku_click",
        "peeku_invoke",
        "peeku_set_value",
        "peeku_type",
        "peeku_scroll",
        "peeku_hotkey",
        "peeku_press",
        "peeku_observe",
        "peeku_wait",
        "peeku_batch",
        "peeku_diff",
        "peeku_element_from_point",
      };

    var actual = ToolRegistry.All.Select(x => x.Name).ToArray();
    Assert.Equal(expected, actual);
  }

  [Fact]
  public void All_Schemas_Parse_And_OutputHasOkAndTraceId()
  {
    foreach (var tool in ToolRegistry.All)
    {
      AssertSchemaLooksValid(tool.Name, "input", tool.InputSchema);
      AssertSchemaLooksValid(tool.Name, "output", tool.OutputSchema);
      AssertOutputHasOkAndTraceId(tool.Name, tool.OutputSchema);
    }
  }

  [Fact]
  public void SetValue_And_Type_OutputSchemas_IncludeEvidence()
  {
    AssertOutputHasProperty("peeku_set_value", "evidence");
    AssertOutputHasProperty("peeku_type", "evidence");
  }

  private static void AssertSchemaLooksValid(string toolName, string schemaKind, JsonDocument schema)
  {
    Assert.Equal(JsonValueKind.Object, schema.RootElement.ValueKind);

    Assert.True(
      schema.RootElement.TryGetProperty("type", out var typeElement),
      $"{toolName}: {schemaKind} schema missing 'type'");
    Assert.Equal("object", typeElement.GetString());

    Assert.True(
      schema.RootElement.TryGetProperty("properties", out var propertiesElement),
      $"{toolName}: {schemaKind} schema missing 'properties'");
    Assert.Equal(JsonValueKind.Object, propertiesElement.ValueKind);
  }

  private static void AssertOutputHasOkAndTraceId(string toolName, JsonDocument outputSchema)
  {
    var root = outputSchema.RootElement;

    Assert.True(
      root.TryGetProperty("properties", out var propertiesElement),
      $"{toolName}: output schema missing 'properties'");

    Assert.True(
      propertiesElement.TryGetProperty("ok", out _),
      $"{toolName}: output schema missing properties.ok");
    Assert.True(
      propertiesElement.TryGetProperty("traceId", out _),
      $"{toolName}: output schema missing properties.traceId");

    Assert.True(
      root.TryGetProperty("required", out var requiredElement),
      $"{toolName}: output schema missing 'required'");
    Assert.Equal(JsonValueKind.Array, requiredElement.ValueKind);

    var required = requiredElement.EnumerateArray()
      .Select(x => x.GetString())
      .Where(x => x is not null)
      .ToHashSet(StringComparer.Ordinal);

    Assert.Contains("ok", required);
    Assert.Contains("traceId", required);
  }

  private static void AssertOutputHasProperty(string toolName, string propertyName)
  {
    var tool = ToolRegistry.Get(toolName);
    var root = tool.OutputSchema.RootElement;
    Assert.True(root.TryGetProperty("properties", out var properties), $"{toolName}: output schema missing properties");
    Assert.True(properties.TryGetProperty(propertyName, out _), $"{toolName}: output schema missing properties.{propertyName}");
  }
}
