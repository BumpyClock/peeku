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

  private readonly record struct ActionResolution(
    bool Ok,
    AutomationElement? Element = null,
    PeekuError? Error = null,
    string? Warning = null);
}
