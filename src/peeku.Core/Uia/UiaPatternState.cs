using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace peeku;

/// <summary>
/// Reads current pattern state from an AutomationElement into a plain dictionary.
/// Each field is guarded by IsSupported; a failing COM read silently omits that key.
/// Returns null when no state keys are present.
/// </summary>
internal static class UiaPatternState
{
  /// <summary>
  /// Returns a dictionary of current state values, or null if no patterns have state.
  /// Never throws.
  /// </summary>
  public static IReadOnlyDictionary<string, object?>? Read(AutomationElement element)
  {
    var d = new Dictionary<string, object?>(capacity: 5, comparer: StringComparer.Ordinal);

    if (SafeIsSupported(() => element.Patterns.Toggle.IsSupported))
    {
      var state = Safe(() => element.Patterns.Toggle.Pattern.ToggleState);
      if (state is ToggleState ts)
        d["toggleState"] = ts.ToString().ToLowerInvariant();
    }

    if (SafeIsSupported(() => element.Patterns.ExpandCollapse.IsSupported))
    {
      var state = Safe(() => element.Patterns.ExpandCollapse.Pattern.ExpandCollapseState);
      if (state is ExpandCollapseState ecs)
        d["expandState"] = ecs.ToString().ToLowerInvariant();
    }

    if (SafeIsSupported(() => element.Patterns.SelectionItem.IsSupported))
    {
      var selected = Safe(() => (object?)element.Patterns.SelectionItem.Pattern.IsSelected);
      if (selected is bool b)
        d["isSelected"] = b;
    }

    if (SafeIsSupported(() => element.Patterns.RangeValue.IsSupported))
    {
      var val = Safe(() => (object?)element.Patterns.RangeValue.Pattern.Value);
      if (val is double rv)
        d["rangeValue"] = rv;
    }

    if (SafeIsSupported(() => element.Patterns.Value.IsSupported))
    {
      var val = Safe(() => element.Patterns.Value.Pattern.Value);
      if (val is string sv && !string.IsNullOrEmpty(sv))
        d["value"] = sv;
    }

    return d.Count > 0 ? d : null;
  }

  private static bool SafeIsSupported(Func<bool> f)
  {
    try { return f(); }
    catch { return false; }
  }

  private static object? Safe(Func<object?> f)
  {
    try { return f(); }
    catch { return null; }
  }
}
