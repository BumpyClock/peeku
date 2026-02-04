using System.Text.Json;

namespace peeku;

public sealed record ToolDescriptor(
  string Name,
  string Title,
  string Description,
  JsonDocument InputSchema,
  JsonDocument OutputSchema,
  Func<JsonElement, CancellationToken, Task<object>> Handler);

