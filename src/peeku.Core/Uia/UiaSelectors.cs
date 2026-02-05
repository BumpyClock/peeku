namespace peeku;

public static class UiaSelectors
{
  public static IReadOnlyList<ElementRef> Select(UiaSnapshotResult snapshot, Selector selector, int limit = 20)
  {
    if (snapshot is null)
    {
      throw new ArgumentNullException(nameof(snapshot));
    }

    if (selector is null)
    {
      throw new ArgumentNullException(nameof(selector));
    }

    if (limit <= 0)
    {
      return Array.Empty<ElementRef>();
    }

    var byRefId = snapshot.Elements.ToDictionary(e => e.Element.RefId, StringComparer.Ordinal);

    static IReadOnlyList<UiaNode> Children(UiaNode node)
      => node.Children ?? Array.Empty<UiaNode>();

    static string? Name(UiaNode node)
      => node.Name;

    static string? ControlType(UiaNode node)
      => node.ControlType;

    string? AutomationId(UiaNode node)
      => byRefId.TryGetValue(node.Element.RefId, out var el) ? el.AutomationId : null;

    string? ClassName(UiaNode node)
      => byRefId.TryGetValue(node.Element.RefId, out var el) ? el.ClassName : null;

    var segments = UiaSelectorEngine.Parse(selector.Expr);
    var nodes = UiaSelectorEngine.Select(
      root: snapshot.Root,
      segments: segments,
      limit: limit,
      children: Children,
      name: Name,
      controlType: ControlType,
      automationId: AutomationId,
      className: ClassName,
      ct: CancellationToken.None);

    var results = new List<ElementRef>(capacity: Math.Clamp(nodes.Count, 0, 256));
    for (var i = 0; i < nodes.Count; i++)
    {
      results.Add(nodes[i].Element);
    }

    return results;
  }
}
