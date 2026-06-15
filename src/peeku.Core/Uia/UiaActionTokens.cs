using FlaUI.Core.AutomationElements;

namespace peeku;

/// <summary>
/// Maps supported FlaUI patterns on an AutomationElement to terse CLI verb tokens.
/// Always includes focus/click/hover. Each pattern token is guarded by IsSupported.
/// </summary>
internal static class UiaActionTokens
{
  /// <summary>
  /// Returns the action tokens for the element.
  /// Never throws — any COM failure silently omits the affected token.
  /// </summary>
  public static IReadOnlyList<string> Read(AutomationElement element)
  {
    var tokens = new List<string>(capacity: 8);

    // Pattern-driven verbs — guarded by IsSupported
    if (SafeIsSupported(() => element.Patterns.Invoke.IsSupported))
      tokens.Add("invoke");

    if (SafeIsSupported(() => element.Patterns.Toggle.IsSupported))
      tokens.Add("toggle");

    if (SafeIsSupported(() => element.Patterns.Value.IsSupported))
      tokens.Add("value");

    if (SafeIsSupported(() => element.Patterns.ExpandCollapse.IsSupported))
      tokens.Add("expand");

    if (SafeIsSupported(() => element.Patterns.SelectionItem.IsSupported))
      tokens.Add("pick");

    if (SafeIsSupported(() => element.Patterns.Scroll.IsSupported))
      tokens.Add("scroll");

    if (SafeIsSupported(() => element.Patterns.Text.IsSupported))
      tokens.Add("read");

    if (SafeIsSupported(() => element.Patterns.GridItem.IsSupported))
      tokens.Add("grid");

    if (SafeIsSupported(() => element.Patterns.RangeValue.IsSupported))
      tokens.Add("range");

    // Always-present capabilities
    tokens.Add("focus");
    tokens.Add("click");
    tokens.Add("hover");

    return tokens;
  }

  private static bool SafeIsSupported(Func<bool> f)
  {
    try
    {
      return f();
    }
    catch
    {
      return false;
    }
  }
}
