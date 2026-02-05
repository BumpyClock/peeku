using ModelContextProtocol.Protocol;

namespace peeku.Mcp;

public static class PeekuMcpToolCatalog
{
  public static IList<Tool> Tools => _tools;

  private static readonly IList<Tool> _tools = ToolRegistry.All
    .Select(static t => new Tool
    {
      Name = t.Name,
      Title = t.Title,
      Description = t.Description,
      InputSchema = t.InputSchema.RootElement.Clone(),
      OutputSchema = t.OutputSchema.RootElement.Clone(),
    })
    .ToList();
}
