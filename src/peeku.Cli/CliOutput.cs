using System.Text.Json;
using System.Text.Json.Serialization;

namespace peeku.Cli;

internal static class CliOutput
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false,
  };

  internal static void Write(object? value, OutputFormat format)
  {
    if (format == OutputFormat.Json)
    {
      Console.Out.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
      return;
    }

    if (value is null)
    {
      Console.Out.WriteLine("(null)");
      return;
    }

    // minimal pretty: indented JSON
    var pretty = JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonOptions) { WriteIndented = true });
    Console.Out.WriteLine(pretty);
  }
}
