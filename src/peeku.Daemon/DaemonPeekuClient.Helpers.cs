using System.Globalization;
using FlaUI.Core.AutomationElements;
using peeku;

namespace peeku.Daemon;

public sealed partial class DaemonPeekuClient
{
  private ActionResolution ResolveActionElement(ElementRef? element, Selector? selector, Target? target, CancellationToken ct)
  {
    var hasElement = element is not null && !string.IsNullOrWhiteSpace(element.RefId);
    var hasSelector = selector is not null && !string.IsNullOrWhiteSpace(selector.Expr);

    if (hasElement == hasSelector)
    {
      return new ActionResolution(
        Ok: false,
        Error: PeekuErrors.Create(
          PeekuErrorCode.InvalidArgument,
          "Provide exactly one of elementRef/refId or selector.",
          new { hasElementRef = hasElement, hasSelector }));
    }

    var targetUsed = target ?? Target.Focused();

    if (hasElement)
    {
      var refId = element!.RefId.Trim();
      if (_handles.TryGet(refId, out var cached))
      {
        return new ActionResolution(Ok: true, Element: cached);
      }

      if (_handles.IsHandleId(refId))
      {
        return new ActionResolution(
          Ok: false,
          Error: PeekuErrors.Create(PeekuErrorCode.ElementNotFound, "Element handle not found.", new { refId }));
      }

      var root = ResolveRoot(targetUsed, ct, out var rootWarning);
      if (root is null)
      {
        var code = rootWarning is not null && rootWarning.Contains("not supported", StringComparison.OrdinalIgnoreCase)
          ? PeekuErrorCode.NotSupported
          : PeekuErrorCode.WindowNotFound;

        return new ActionResolution(
          Ok: false,
          Error: PeekuErrors.Create(code, "Target window not found."),
          Warning: rootWarning);
      }

      var found = FindByRefId(root, refId, maxNodes: 20_000, ct);
      if (found is null)
      {
        return new ActionResolution(
          Ok: false,
          Error: PeekuErrors.Create(
            PeekuErrorCode.ElementNotFound,
            "Element not found.",
            new { refId, target = targetUsed }),
          Warning: rootWarning);
      }

      StoreHandle(found);
      return new ActionResolution(Ok: true, Element: found, Warning: rootWarning);
    }

    if (selector!.PreferCachedSnapshot)
    {
      var snapshot = UiaSnapshotAsync(
        new UiaSnapshotRequest(
          Target: targetUsed,
          Depth: 6,
          MaxNodes: 5000,
          IncludeProperties: UiaPropertiesMode.All),
        ct).GetAwaiter().GetResult();

      if (!snapshot.Ok)
      {
        return new ActionResolution(
          Ok: false,
          Error: snapshot.Error ?? PeekuErrors.Create(PeekuErrorCode.Internal, "Selection resolution failed."),
          Warning: snapshot.Meta.Warning);
      }

      IReadOnlyList<ElementRef> matches;
      try
      {
        matches = UiaSelectors.Select(snapshot, selector, limit: 1);
      }
      catch (ArgumentException ex)
      {
        return new ActionResolution(
          Ok: false,
          Error: PeekuErrors.Create(
            PeekuErrorCode.InvalidArgument,
            "Invalid selector.",
            new { selector = selector.Expr, error = ex.Message }),
          Warning: snapshot.Meta.Warning);
      }
      catch (Exception ex)
      {
        return new ActionResolution(
          Ok: false,
          Error: PeekuErrors.Create(
            PeekuErrorCode.Internal,
            "Selector evaluation failed.",
            new { selector = selector.Expr, exception = ex.GetType().FullName, ex.Message, ex.HResult }),
          Warning: snapshot.Meta.Warning);
      }

      if (matches.Count == 0)
      {
        return new ActionResolution(
          Ok: false,
          Error: PeekuErrors.Create(
            PeekuErrorCode.ElementNotFound,
            "Selector did not match any elements.",
            new { selector = selector.Expr }),
          Warning: snapshot.Meta.Warning);
      }

      var refId = matches[0].RefId;
      if (!_handles.TryGet(refId, out var cached))
      {
        return new ActionResolution(
          Ok: false,
          Error: PeekuErrors.Create(PeekuErrorCode.ElementNotFound, "Element handle not available.", new { refId }),
          Warning: snapshot.Meta.Warning);
      }

      return new ActionResolution(Ok: true, Element: cached, Warning: snapshot.Meta.Warning);
    }

    var liveRoot = ResolveRoot(targetUsed, ct, out var liveWarning);
    if (liveRoot is null)
    {
      var code = liveWarning is not null && liveWarning.Contains("not supported", StringComparison.OrdinalIgnoreCase)
        ? PeekuErrorCode.NotSupported
        : PeekuErrorCode.WindowNotFound;

      return new ActionResolution(
        Ok: false,
        Error: PeekuErrors.Create(code, "Target window not found."),
        Warning: liveWarning);
    }

    IReadOnlyList<AutomationElement> liveMatches;
    try
    {
      liveMatches = UiaLiveSelectors.Select(liveRoot, selector, limit: 1, ct);
    }
    catch (ArgumentException ex)
    {
      return new ActionResolution(
        Ok: false,
        Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Invalid selector.", new { selector = selector.Expr, error = ex.Message }),
        Warning: liveWarning);
    }

    if (liveMatches.Count == 0)
    {
      return new ActionResolution(
        Ok: false,
        Error: PeekuErrors.Create(
          PeekuErrorCode.ElementNotFound,
          "Selector did not match any elements.",
          new { selector = selector.Expr }),
        Warning: liveWarning);
    }

    var match = liveMatches[0];
    StoreHandle(match);
    return new ActionResolution(Ok: true, Element: match, Warning: liveWarning);
  }

  private static string? CombineWarnings(string? a, string? b)
  {
    if (string.IsNullOrWhiteSpace(a))
    {
      return string.IsNullOrWhiteSpace(b) ? null : b;
    }

    if (string.IsNullOrWhiteSpace(b))
    {
      return a;
    }

    return $"{a} {b}";
  }

  private AutomationElement? ResolveRoot(Target target, CancellationToken ct, out string? warning)
  {
    warning = null;

    switch (target)
    {
      case Target.Desktop:
        return _automation.GetDesktop();

      case Target.FocusedWindow:
      {
        var win = Win32Windows.GetFocusedWindow();
        if (win is null)
        {
          warning = "No foreground window.";
          return null;
        }

        if (!TryParseHwndHex(win.HwndHex, out var hwnd))
        {
          warning = "Foreground window handle invalid.";
          return null;
        }

        return _automation.FromHandle(hwnd);
      }

      case Target.WindowByHwnd hwndTarget:
      {
        if (!TryParseHwndHex(hwndTarget.HwndHex, out var hwnd))
        {
          warning = "Window handle invalid.";
          return null;
        }

        return _automation.FromHandle(hwnd);
      }

      case Target.WindowByQuery queryTarget:
      {
        ct.ThrowIfCancellationRequested();

        var q = queryTarget.Query;
        var candidates = Win32Windows.ListWindows(
          new WindowsListRequest(TitleContains: q.TitleContains, ProcessName: q.ProcessName, Limit: 200),
          ct);

        var match = candidates.FirstOrDefault(w => q.ProcessId is null || w.ProcessId == q.ProcessId.Value);
        if (match is null)
        {
          warning = "No window matched query.";
          return null;
        }

        if (!TryParseHwndHex(match.HwndHex, out var hwnd))
        {
          warning = "Matched window handle invalid.";
          return null;
        }

        return _automation.FromHandle(hwnd);
      }

      case Target.Screen:
        warning = "Screen target not supported for UIA snapshot.";
        return null;

      default:
        warning = "Target not supported.";
        return null;
    }
  }

  private static bool TryParseHwndHex(string hwndHex, out nint hwnd)
  {
    hwnd = 0;
    if (string.IsNullOrWhiteSpace(hwndHex))
    {
      return false;
    }

    var s = hwndHex.Trim();
    if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
    {
      s = s[2..];
    }

    if (!long.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
    {
      return false;
    }

    hwnd = unchecked((nint)value);
    return hwnd != 0;
  }

  private string StoreHandle(AutomationElement element)
  {
    var stableKey = TryGetStableKey(element);
    return _handles.Store(element, stableKey);
  }

  private static string? TryGetStableKey(AutomationElement element)
  {
    try
    {
      return UiaRefId.Create(element);
    }
    catch
    {
      return null;
    }
  }

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
      ClassName: ReadClassName(element, mode));

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

  private static AutomationElement? FindByRefId(AutomationElement root, string refId, int maxNodes, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(refId))
    {
      return null;
    }

    if (maxNodes <= 0)
    {
      return null;
    }

    var stack = new Stack<AutomationElement>(capacity: 256);
    stack.Push(root);

    var seen = 0;
    while (stack.Count > 0)
    {
      ct.ThrowIfCancellationRequested();

      var el = stack.Pop();
      seen++;
      if (seen > maxNodes)
      {
        return null;
      }

      try
      {
        if (string.Equals(UiaRefId.Create(el), refId, StringComparison.Ordinal))
        {
          return el;
        }
      }
      catch
      {
      }

      AutomationElement[] children;
      try
      {
        children = el.FindAllChildren();
      }
      catch
      {
        continue;
      }

      for (var i = 0; i < children.Length; i++)
      {
        stack.Push(children[i]);
      }
    }

    return null;
  }

  private static bool TryClickViaUiaPatterns(AutomationElement element, out PeekuError error)
  {
    error = PeekuErrors.Create(PeekuErrorCode.NotSupported, "Element does not support click via UIA patterns.");

    try
    {
      if (element.Patterns.Invoke.IsSupported)
      {
        element.Patterns.Invoke.Pattern.Invoke();
        return true;
      }
    }
    catch (Exception ex)
    {
      error = PeekuErrors.Create(PeekuErrorCode.Internal, "UIA invoke failed.", new { exception = ex.GetType().FullName, ex.Message, ex.HResult });
      return false;
    }

    try
    {
      if (element.Patterns.Toggle.IsSupported)
      {
        element.Patterns.Toggle.Pattern.Toggle();
        return true;
      }
    }
    catch (Exception ex)
    {
      error = PeekuErrors.Create(PeekuErrorCode.Internal, "UIA toggle failed.", new { exception = ex.GetType().FullName, ex.Message, ex.HResult });
      return false;
    }

    try
    {
      if (element.Patterns.SelectionItem.IsSupported)
      {
        element.Patterns.SelectionItem.Pattern.Select();
        return true;
      }
    }
    catch (Exception ex)
    {
      error = PeekuErrors.Create(PeekuErrorCode.Internal, "UIA select failed.", new { exception = ex.GetType().FullName, ex.Message, ex.HResult });
      return false;
    }

    return false;
  }

  private static bool TryInvoke(AutomationElement element, out PeekuError error)
  {
    error = PeekuErrors.Create(PeekuErrorCode.NotSupported, "Element does not support invoke pattern.");

    if (!element.Patterns.Invoke.IsSupported)
    {
      return false;
    }

    try
    {
      element.Patterns.Invoke.Pattern.Invoke();
      return true;
    }
    catch (Exception ex)
    {
      error = PeekuErrors.Create(PeekuErrorCode.Internal, "UIA invoke failed.", new { exception = ex.GetType().FullName, ex.Message, ex.HResult });
      return false;
    }
  }

  private static bool TrySetValue(AutomationElement element, string value, out PeekuError error)
  {
    error = PeekuErrors.Create(PeekuErrorCode.NotSupported, "Element does not support value pattern.");

    if (!element.Patterns.Value.IsSupported)
    {
      return false;
    }

    try
    {
      element.Patterns.Value.Pattern.SetValue(value ?? "");
      return true;
    }
    catch (Exception ex)
    {
      error = PeekuErrors.Create(PeekuErrorCode.Internal, "UIA set value failed.", new { exception = ex.GetType().FullName, ex.Message, ex.HResult });
      return false;
    }
  }

  private static bool TryGetValue(AutomationElement element, out string? value)
  {
    value = null;

    if (!element.Patterns.Value.IsSupported)
    {
      return false;
    }

    try
    {
      value = element.Patterns.Value.Pattern.Value;
      return true;
    }
    catch
    {
      value = null;
      return false;
    }
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
      d["value"] = Safe(() => element.Patterns.Value.Pattern.Value);
      d["isReadOnly"] = Safe(() => element.Patterns.Value.Pattern.IsReadOnly);
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

  private readonly record struct ActionResolution(
    bool Ok,
    AutomationElement? Element = null,
    PeekuError? Error = null,
    string? Warning = null);
}
