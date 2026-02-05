using System.Text.Json;

namespace peeku.Mcp;

internal static class PeekuMcpJson
{
  internal static JsonElement ToArgsElement(IDictionary<string, JsonElement>? args)
  {
    if (args is null || args.Count == 0)
    {
      return JsonSerializer.SerializeToElement(new Dictionary<string, object?>());
    }

    return JsonSerializer.SerializeToElement(args);
  }
}
