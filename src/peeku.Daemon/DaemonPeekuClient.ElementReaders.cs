using FlaUI.Core.AutomationElements;
using peeku;

namespace peeku.Daemon;

public sealed partial class DaemonPeekuClient
{
  private static string? ReadName(AutomationElement element, UiaPropertiesMode mode)
  {
    if (mode != UiaPropertiesMode.Basic && mode != UiaPropertiesMode.All)
    {
      return null;
    }

    try
    {
      var name = element.Name;
      if (!string.IsNullOrWhiteSpace(name))
      {
        return name;
      }
    }
    catch
    {
    }

    return UiaNameFallbacks.ReadName(element);
  }

  private static string? ReadControlType(AutomationElement element, UiaPropertiesMode mode)
  {
    if (mode != UiaPropertiesMode.Basic && mode != UiaPropertiesMode.All)
    {
      return null;
    }

    try
    {
      return element.ControlType.ToString();
    }
    catch
    {
      return null;
    }
  }

  private static Rect? ReadRect(AutomationElement element, UiaPropertiesMode mode)
  {
    if (mode != UiaPropertiesMode.Basic && mode != UiaPropertiesMode.All)
    {
      return null;
    }

    try
    {
      var r = element.BoundingRectangle;
      if (r.Width <= 0 || r.Height <= 0)
      {
        return null;
      }

      return new Rect(
        X: r.Left,
        Y: r.Top,
        Width: r.Width,
        Height: r.Height);
    }
    catch
    {
      return null;
    }
  }

  private static string? ReadAutomationId(AutomationElement element, UiaPropertiesMode mode)
  {
    if (mode != UiaPropertiesMode.All)
    {
      return null;
    }

    try
    {
      var id = element.AutomationId;
      return string.IsNullOrWhiteSpace(id) ? null : id;
    }
    catch
    {
      return null;
    }
  }

  private static string? ReadClassName(AutomationElement element, UiaPropertiesMode mode)
  {
    if (mode != UiaPropertiesMode.All)
    {
      return null;
    }

    try
    {
      var cn = element.ClassName;
      return string.IsNullOrWhiteSpace(cn) ? null : cn;
    }
    catch
    {
      return null;
    }
  }

  private static UiaElement ReadElement(string refId, string snapshotId, AutomationElement element, UiaPropertiesMode mode)
    => new(
      Element: new ElementRef(refId, snapshotId),
      Rect: ReadRect(element, mode),
      Name: ReadName(element, mode),
      ControlType: ReadControlType(element, mode),
      AutomationId: ReadAutomationId(element, mode),
      ClassName: ReadClassName(element, mode),
      Actions: mode == UiaPropertiesMode.All ? UiaActionTokens.Read(element) : null,
      State: mode == UiaPropertiesMode.All ? UiaPatternState.Read(element) : null);

  private static UiaNode BuildNode(NodeBuilder node, string snapshotId)
    => new(
      Element: new ElementRef(node.RefId, snapshotId),
      Name: node.Name,
      ControlType: node.ControlType,
      Children: node.Children is null ? null : node.Children.Select(c => BuildNode(c, snapshotId)).ToArray());

  private sealed record NodeBuilder(
    string RefId,
    string? Name,
    string? ControlType,
    AutomationElement Element)
  {
    public List<NodeBuilder>? Children { get; set; }
  }

  private static IReadOnlyDictionary<string, object?> ReadProperties(AutomationElement element, UiaPropertiesMode mode)
  {
    var d = new Dictionary<string, object?>(StringComparer.Ordinal)
    {
      ["name"] = Safe(() => element.Name),
      ["controlType"] = Safe(() => element.ControlType.ToString()),
      ["rect"] = ReadRect(element, UiaPropertiesMode.Basic),
    };

    if (mode != UiaPropertiesMode.All)
    {
      return d;
    }

    d["automationId"] = Safe(() => element.AutomationId);
    d["className"] = Safe(() => element.ClassName);
    d["frameworkId"] = Safe(() => element.Properties.FrameworkId.ValueOrDefault);
    d["processId"] = Safe(() => element.Properties.ProcessId.ValueOrDefault);
    d["nativeWindowHandle"] = SafeHwndHex(element);
    d["isEnabled"] = Safe(() => element.IsEnabled);
    d["isOffscreen"] = Safe(() => element.Properties.IsOffscreen.ValueOrDefault);
    d["isKeyboardFocusable"] = Safe(() => element.Properties.IsKeyboardFocusable.ValueOrDefault);
    d["hasKeyboardFocus"] = Safe(() => element.Properties.HasKeyboardFocus.ValueOrDefault);
    d["helpText"] = Safe(() => element.Properties.HelpText.ValueOrDefault);

    if (element.Patterns.Value.IsSupported)
    {
      // .ValueOrDefault unwraps FlaUI's AutomationProperty<T>; without it the wrapper object
      // itself was stored (and serialized) instead of the string/bool. Matches the property
      // reads above. (Same class of bug as the UiaPatternState fix.)
      d["value"] = Safe(() => element.Patterns.Value.Pattern.Value.ValueOrDefault);
      d["isReadOnly"] = Safe(() => element.Patterns.Value.Pattern.IsReadOnly.ValueOrDefault);
    }

    return d;
  }

  private static IReadOnlyList<string> ReadSupportedPatterns(AutomationElement element)
  {
    var list = new List<string>(capacity: 8);

    if (element.Patterns.Invoke.IsSupported) list.Add("invoke");
    if (element.Patterns.Toggle.IsSupported) list.Add("toggle");
    if (element.Patterns.Value.IsSupported) list.Add("value");
    if (element.Patterns.SelectionItem.IsSupported) list.Add("selectionItem");
    if (element.Patterns.Scroll.IsSupported) list.Add("scroll");
    if (element.Patterns.ExpandCollapse.IsSupported) list.Add("expandCollapse");
    if (element.Patterns.RangeValue.IsSupported) list.Add("rangeValue");
    if (element.Patterns.GridItem.IsSupported) list.Add("gridItem");
    if (element.Patterns.Text.IsSupported) list.Add("text");
    if (element.Patterns.Window.IsSupported) list.Add("window");
    if (element.Patterns.Transform.IsSupported) list.Add("transform");
    if (element.Patterns.LegacyIAccessible.IsSupported) list.Add("legacyIAccessible");

    return list;
  }

  private static object? Safe(Func<object?> f)
  {
    try
    {
      var v = f();
      return v is string s && string.IsNullOrWhiteSpace(s) ? null : v;
    }
    catch
    {
      return null;
    }
  }

  private static string? SafeHwndHex(AutomationElement element)
  {
    try
    {
      var hwnd = element.Properties.NativeWindowHandle.ValueOrDefault;
      var value = hwnd == 0 ? 0 : hwnd.ToInt64();
      return value == 0 ? null : $"0x{value:X}";
    }
    catch
    {
      return null;
    }
  }
}
