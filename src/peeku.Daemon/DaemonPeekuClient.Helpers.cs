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

      // Fast path 1: ephemeral h: handle still live in this process's cache.
      if (_handles.TryGet(refId, out var cached))
      {
        return new ActionResolution(Ok: true, Element: cached);
      }

      // Fast path 2: durable uia:pid:hash id whose element is still cached (indexed by stable key).
      // This is the common cross-CLI-process case: a separate `snapshot` cached the element under
      // the same warm daemon, so we hit the cache without re-walking.
      if (_handles.TryGetByStableKey(refId, out var byKey))
      {
        return new ActionResolution(Ok: true, Element: byKey);
      }

      // An ephemeral h: id that missed both fast paths is gone for good (no durable recompute path).
      if (_handles.IsHandleId(refId))
      {
        return new ActionResolution(
          Ok: false,
          Error: PeekuErrors.Create(PeekuErrorCode.ElementNotFound, "Element handle not found.", new { refId }));
      }

      // Durable uia: id, cache miss (TTL-expired / LRU-evicted / daemon-restarted): re-walk the live
      // tree to recompute it. Scope the re-walk to the ref's pid when parseable to avoid a
      // full-desktop walk (fallback cost ~= one snapshot of that pid's windows).
      var rootWarning = default(string);
      AutomationElement? found;
      if (TryParseUiaPid(refId, out var refPid))
      {
        found = FindByRefIdForPid(refPid, refId, ct, out rootWarning);
      }
      else
      {
        var root = ResolveRoot(targetUsed, ct, out rootWarning);
        if (root is null)
        {
          var code = rootWarning is not null && rootWarning.Contains("not supported", StringComparison.OrdinalIgnoreCase)
            ? PeekuErrorCode.NotSupported
            : PeekuErrorCode.WindowNotFound;

          return new ActionResolution(
            Ok: false,
            Error: PeekuErrors.Create(code, "Target window not found.", new { refId }),
            Warning: rootWarning);
        }

        found = FindByRefId(root, refId, maxNodes: 20_000, ct);
      }

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
        var intent = CandidateHints.IntentFromSelector(selector.Expr);
        var candidates = CandidateHints.Suggest(snapshot.Elements, intent);
        return new ActionResolution(
          Ok: false,
          Error: PeekuErrors.Create(
            PeekuErrorCode.ElementNotFound,
            "Selector did not match any elements.",
            new { selector = selector.Expr, candidates }),
          Warning: snapshot.Meta.Warning);
      }

      var refId = matches[0].RefId;

      // The snapshot we just built cached every node via StoreHandle, indexing the durable id as the
      // stable key. matches[0].RefId is now the durable uia: id (not h:), so a raw _handles.TryGet
      // would return false; resolve by stable key, with a re-walk fallback for the rare miss.
      if (_handles.TryGetByStableKey(refId, out var cached) || _handles.TryGet(refId, out cached))
      {
        return new ActionResolution(Ok: true, Element: cached, Warning: snapshot.Meta.Warning);
      }

      var rewalked = TryParseUiaPid(refId, out var snapPid)
        ? FindByRefIdForPid(snapPid, refId, ct, out _)
        : null;
      if (rewalked is null)
      {
        return new ActionResolution(
          Ok: false,
          Error: PeekuErrors.Create(PeekuErrorCode.ElementNotFound, "Element handle not available.", new { refId }),
          Warning: snapshot.Meta.Warning);
      }

      StoreHandle(rewalked);
      return new ActionResolution(Ok: true, Element: rewalked, Warning: snapshot.Meta.Warning);
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
      var liveIntent = CandidateHints.IntentFromSelector(selector.Expr);
      var liveCandidates = CollectLiveCandidates(liveRoot, liveIntent, maxVisited: 2000, maxCollect: 20, ct);
      return new ActionResolution(
        Ok: false,
        Error: PeekuErrors.Create(
          PeekuErrorCode.ElementNotFound,
          "Selector did not match any elements.",
          new { selector = selector.Expr, candidates = liveCandidates }),
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

  /// <summary>
  /// Parses the pid segment of a durable <c>uia:&lt;pid&gt;:&lt;hash&gt;</c> ref id (see UiaRefId.Create).
  /// Returns false for ephemeral <c>h:</c> ids or any malformed value so callers fall back to the
  /// target-scoped root instead of crashing.
  /// </summary>
  private static bool TryParseUiaPid(string refId, out int pid)
  {
    pid = 0;
    if (string.IsNullOrWhiteSpace(refId))
    {
      return false;
    }

    var s = refId.Trim();
    if (!s.StartsWith("uia:", StringComparison.Ordinal))
    {
      return false;
    }

    var rest = s.AsSpan(4);
    var sep = rest.IndexOf(':');
    if (sep <= 0)
    {
      return false;
    }

    return int.TryParse(rest[..sep], NumberStyles.None, CultureInfo.InvariantCulture, out pid) && pid > 0;
  }

  /// <summary>
  /// Resolves the live UIA roots (top-level windows) owned by <paramref name="pid"/>, reusing
  /// Win32 window enumeration filtered by process id. Spec calls this "ResolveRootForPid"; a pid can
  /// own several top-level windows, so this returns all of them and the caller re-walks each.
  /// </summary>
  private IReadOnlyList<AutomationElement> ResolveRootsForPid(int pid, CancellationToken ct)
  {
    if (pid <= 0)
    {
      return Array.Empty<AutomationElement>();
    }

    // Filter by pid DURING enumeration (before the Limit cap), so other apps' top-level windows on a
    // busy desktop can't push this pid's windows past the cap and cause a false ElementNotFound on the
    // re-walk. Limit 256 is the effective ceiling here; a single pid owns far fewer top-level windows.
    var windows = Win32Windows.ListWindows(
      new WindowsListRequest(TitleContains: null, ProcessName: null, Limit: 256),
      processId: pid,
      ct);

    var roots = new List<AutomationElement>(capacity: 4);
    foreach (var w in windows)
    {
      if (!TryParseHwndHex(w.HwndHex, out var hwnd))
      {
        continue;
      }

      try
      {
        roots.Add(_automation.FromHandle(hwnd));
      }
      catch
      {
      }
    }

    return roots;
  }

  /// <summary>
  /// Re-walks the live tree of every top-level window owned by <paramref name="pid"/> to recompute a
  /// durable ref id (the fallback path when the cached handle is gone). Returns the first match, or
  /// null when no window matched. Cost ~= one snapshot of that pid's windows.
  /// </summary>
  private AutomationElement? FindByRefIdForPid(int pid, string refId, CancellationToken ct, out string? warning)
  {
    warning = null;
    var roots = ResolveRootsForPid(pid, ct);

    // UWP fallback: a packaged app's elements report the APP's pid, but its top-level window is owned
    // by ApplicationFrameHost (a different pid), so the pid-scoped enumeration is EMPTY and the re-walk
    // would falsely report WindowNotFound. Re-walk every top-level window to find the matching durable
    // refId. Fallback-only — classic apps (window pid == element pid) resolve via the pid scope above.
    if (roots.Count == 0)
    {
      roots = ResolveAllTopLevelRoots(ct);
      if (roots.Count == 0)
      {
        warning = "Target window not found.";
        return null;
      }
    }

    foreach (var root in roots)
    {
      ct.ThrowIfCancellationRequested();
      var found = FindByRefId(root, refId, maxNodes: 20_000, ct);
      if (found is not null)
      {
        return found;
      }
    }

    return null;
  }

  /// <summary>
  /// Resolves live UIA roots for ALL top-level windows (no pid filter). Fallback for when a
  /// pid-scoped resolve is empty — notably UWP/packaged apps, whose window is owned by
  /// ApplicationFrameHost, not the element's pid. More expensive (re-walks each window's subtree),
  /// so only used when the pid scope yields nothing.
  /// </summary>
  private IReadOnlyList<AutomationElement> ResolveAllTopLevelRoots(CancellationToken ct)
  {
    var windows = Win32Windows.ListWindows(
      new WindowsListRequest(TitleContains: null, ProcessName: null, Limit: 256),
      ct);

    var roots = new List<AutomationElement>(capacity: 16);
    foreach (var w in windows)
    {
      if (!TryParseHwndHex(w.HwndHex, out var hwnd))
      {
        continue;
      }

      try
      {
        roots.Add(_automation.FromHandle(hwnd));
      }
      catch
      {
      }
    }

    return roots;
  }

  private readonly record struct ActionResolution(
    bool Ok,
    AutomationElement? Element = null,
    PeekuError? Error = null,
    string? Warning = null);

  /// <summary>
  /// Bounded DFS over the live UIA tree, collecting up to <paramref name="maxCollect"/> elements
  /// (with name or automationId) for candidate-hint ranking. Visits at most
  /// <paramref name="maxVisited"/> nodes to stay cheap on large trees.
  /// </summary>
  private static IReadOnlyList<CandidateHints.Candidate> CollectLiveCandidates(
    AutomationElement root,
    string? intent,
    int maxVisited,
    int maxCollect,
    CancellationToken ct)
  {
    var pool = new List<UiaElement>(capacity: Math.Min(maxCollect * 4, 64));
    var stack = new Stack<AutomationElement>(capacity: 64);
    stack.Push(root);
    var visited = 0;

    while (stack.Count > 0 && visited < maxVisited)
    {
      ct.ThrowIfCancellationRequested();
      var el = stack.Pop();
      visited++;

      string? name = null;
      string? automationId = null;
      string? controlType = null;
      Rect? rect = null;

      try { name = el.Name; } catch { }
      try { automationId = el.AutomationId; } catch { }
      try { controlType = el.ControlType.ToString(); } catch { }
      try
      {
        var r = el.BoundingRectangle;
        if (r.Width > 0 && r.Height > 0)
        {
          rect = new Rect(r.Left, r.Top, r.Width, r.Height);
        }
      }
      catch { }

      if (!string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(automationId))
      {
        pool.Add(new UiaElement(
          Element: new ElementRef(""),
          Rect: rect,
          Name: string.IsNullOrWhiteSpace(name) ? null : name,
          ControlType: string.IsNullOrWhiteSpace(controlType) ? null : controlType,
          AutomationId: string.IsNullOrWhiteSpace(automationId) ? null : automationId));
      }

      AutomationElement[] children;
      try { children = el.FindAllChildren(); }
      catch { continue; }

      for (var i = 0; i < children.Length; i++)
      {
        stack.Push(children[i]);
      }
    }

    return CandidateHints.Suggest(pool, intent, maxCollect);
  }
}
