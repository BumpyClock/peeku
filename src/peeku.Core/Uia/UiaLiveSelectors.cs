using FlaUI.Core.AutomationElements;

namespace peeku;

internal static class UiaLiveSelectors
{
  internal static IReadOnlyList<AutomationElement> Select(AutomationElement root, Selector selector, int limit, CancellationToken ct)
  {
    if (root is null)
    {
      throw new ArgumentNullException(nameof(root));
    }

    if (selector is null)
    {
      throw new ArgumentNullException(nameof(selector));
    }

    if (limit <= 0)
    {
      return Array.Empty<AutomationElement>();
    }

    ct.ThrowIfCancellationRequested();

    static AutomationElement[] Children(AutomationElement node)
      => SafeChildren(node);

    static string? Name(AutomationElement element)
      => ReadName(element);

    static string? ControlType(AutomationElement element)
      => Safe(() => element.ControlType.ToString());

    static string? AutomationId(AutomationElement element)
      => Safe(() => element.AutomationId);

    static string? ClassName(AutomationElement element)
      => Safe(() => element.ClassName);

    var segments = UiaSelectorEngine.Parse(selector.Expr);
    return UiaSelectorEngine.Select(
      root: root,
      segments: segments,
      limit: limit,
      children: Children,
      name: Name,
      controlType: ControlType,
      automationId: AutomationId,
      className: ClassName,
      ct: ct);
  }

  private static AutomationElement[] SafeChildren(AutomationElement node)
  {
    try
    {
      return node.FindAllChildren();
    }
    catch
    {
      return Array.Empty<AutomationElement>();
    }
  }

  private static string? ReadName(AutomationElement element)
  {
    var name = Safe(() => element.Name);
    if (!string.IsNullOrWhiteSpace(name))
    {
      return name;
    }

    return UiaNameFallbacks.ReadName(element);
  }

  private static string? Safe(Func<string?> f)
  {
    try
    {
      var v = f();
      return string.IsNullOrWhiteSpace(v) ? null : v;
    }
    catch
    {
      return null;
    }
  }
}
