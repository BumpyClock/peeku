using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace peeku;

/// <summary>
/// Evaluates a <see cref="WaitCondition"/> against a live <see cref="AutomationElement"/>.
/// All pattern reads are guarded by IsSupported and wrapped in try/catch so a failing COM
/// call returns false (element not ready) rather than aborting the wait loop.
/// Ref: UI Automation Control Pattern Identifiers (MSDN); FlaUI 5.0 pattern API.
/// </summary>
internal static class WaitPredicates
{
  /// <summary>
  /// Returns true when the element satisfies the condition.
  /// <paramref name="element"/> must be non-null for all conditions except the loop-level
  /// NotExists check (which is handled by the caller, not here).
  /// </summary>
  internal static bool Evaluate(AutomationElement element, WaitCondition condition, string? expected)
  {
    try
    {
      switch (condition)
      {
        case WaitCondition.Exists:
          // Element resolved → already exists.
          return true;

        case WaitCondition.NotExists:
          // Loop-level inversion: caller inverts presence; this branch is unreachable in normal flow
          // but returns false (element IS present, so NotExists not satisfied).
          return false;

        case WaitCondition.Enabled:
          return SafeBool(() => element.Properties.IsEnabled.ValueOrDefault);

        case WaitCondition.Disabled:
          return !SafeBool(() => element.Properties.IsEnabled.ValueOrDefault);

        case WaitCondition.Visible:
          // Visible = NOT offscreen. IsOffscreen true means hidden/clipped.
          return !SafeBool(() => element.Properties.IsOffscreen.ValueOrDefault);

        case WaitCondition.Hidden:
          return SafeBool(() => element.Properties.IsOffscreen.ValueOrDefault);

        case WaitCondition.Focused:
          return SafeBool(() => element.Properties.HasKeyboardFocus.ValueOrDefault);

        case WaitCondition.ToggleOn:
          if (!SafeIsSupported(() => element.Patterns.Toggle.IsSupported))
            return false;
          return Safe(() => element.Patterns.Toggle.Pattern.ToggleState.Value) is ToggleState ts &&
                 ts == ToggleState.On;

        case WaitCondition.ToggleOff:
          if (!SafeIsSupported(() => element.Patterns.Toggle.IsSupported))
            return false;
          return Safe(() => element.Patterns.Toggle.Pattern.ToggleState.Value) is ToggleState tsOff &&
                 tsOff == ToggleState.Off;

        case WaitCondition.Expanded:
          if (!SafeIsSupported(() => element.Patterns.ExpandCollapse.IsSupported))
            return false;
          return Safe(() => element.Patterns.ExpandCollapse.Pattern.ExpandCollapseState.Value) is ExpandCollapseState ecs &&
                 ecs == ExpandCollapseState.Expanded;

        case WaitCondition.Collapsed:
          if (!SafeIsSupported(() => element.Patterns.ExpandCollapse.IsSupported))
            return false;
          return Safe(() => element.Patterns.ExpandCollapse.Pattern.ExpandCollapseState.Value) is ExpandCollapseState ecsColl &&
                 ecsColl == ExpandCollapseState.Collapsed;

        case WaitCondition.Selected:
          if (!SafeIsSupported(() => element.Patterns.SelectionItem.IsSupported))
            return false;
          return Safe(() => element.Patterns.SelectionItem.Pattern.IsSelected.Value) is bool sel && sel;

        case WaitCondition.NotSelected:
          if (!SafeIsSupported(() => element.Patterns.SelectionItem.IsSupported))
            return false;
          return Safe(() => element.Patterns.SelectionItem.Pattern.IsSelected.Value) is bool notSel && !notSel;

        case WaitCondition.ValueEquals:
        {
          var val = ReadValue(element);
          return StringsEqual(val, expected);
        }

        case WaitCondition.ValueContains:
        {
          var val = ReadValue(element);
          return StringContains(val, expected);
        }

        case WaitCondition.NameEquals:
        {
          var name = SafeString(() => element.Properties.Name.ValueOrDefault);
          return StringsEqual(name, expected);
        }

        case WaitCondition.NameContains:
        {
          var name = SafeString(() => element.Properties.Name.ValueOrDefault);
          return StringContains(name, expected);
        }

        default:
          return false;
      }
    }
    catch
    {
      return false;
    }
  }

  private static string? ReadValue(AutomationElement element)
  {
    if (SafeIsSupported(() => element.Patterns.Value.IsSupported))
    {
      return SafeString(() => element.Patterns.Value.Pattern.Value.Value);
    }

    return null;
  }

  private static bool StringsEqual(string? actual, string? expected)
    => string.Equals(actual ?? "", expected ?? "", StringComparison.OrdinalIgnoreCase);

  private static bool StringContains(string? actual, string? expected)
  {
    if (string.IsNullOrEmpty(expected))
    {
      return true;
    }

    return (actual ?? "").Contains(expected, StringComparison.OrdinalIgnoreCase);
  }

  private static bool SafeBool(Func<bool> f)
  {
    try { return f(); }
    catch { return false; }
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

  private static string? SafeString(Func<string?> f)
  {
    try { return f(); }
    catch { return null; }
  }
}
