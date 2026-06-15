namespace peeku;

internal static class ActionSelectionResolver
{
  internal readonly record struct Resolution(
    bool Ok,
    ResolvedActionSelection? Selection = null,
    PeekuError? Error = null,
    string? Warning = null);

  public static async Task<Resolution> ResolveAsync(
    Func<UiaSnapshotRequest, CancellationToken, Task<UiaSnapshotResult>> snapshotAsync,
    Func<Target, FlaUI.UIA3.UIA3Automation, CancellationToken, (FlaUI.Core.AutomationElements.AutomationElement? Root, string? Warning)> resolveRoot,
    ElementRef? element,
    Selector? selector,
    Target? target,
    CancellationToken ct)
  {
    if (snapshotAsync is null)
    {
      throw new ArgumentNullException(nameof(snapshotAsync));
    }

    if (resolveRoot is null)
    {
      throw new ArgumentNullException(nameof(resolveRoot));
    }

    var hasElement = element is not null && !string.IsNullOrWhiteSpace(element.RefId);
    var hasSelector = selector is not null && !string.IsNullOrWhiteSpace(selector.Expr);

    if (hasElement && hasSelector)
    {
      return new Resolution(
        Ok: false,
        Error: PeekuErrors.Create(
          PeekuErrorCode.InvalidArgument,
          "Provide only one of elementRef/refId or selector.",
          new { hasElementRef = hasElement, hasSelector }));
    }

    var targetUsed = target ?? Target.Focused();

    // Neither element nor selector: fall back to the target window's focused element.
    if (!hasElement && !hasSelector)
    {
      return await ResolveFocused(targetUsed, resolveRoot, ct).ConfigureAwait(false);
    }

    if (hasSelector && selector is not null && !selector.PreferCachedSnapshot)
    {
      return ResolveLive(selector, targetUsed, resolveRoot, ct);
    }

    var snapshotReq = new UiaSnapshotRequest(
      Target: targetUsed,
      Depth: 6,
      MaxNodes: 5000,
      IncludeProperties: UiaPropertiesMode.All);

    var snapshot = await snapshotAsync(snapshotReq, ct).ConfigureAwait(false);
    if (!snapshot.Ok)
    {
      return new Resolution(
        Ok: false,
        Error: snapshot.Error ?? PeekuErrors.Create(PeekuErrorCode.Internal, "Selection resolution failed."),
        Warning: snapshot.Meta.Warning);
    }

    string refId;
    if (hasElement)
    {
      refId = element!.RefId.Trim();
    }
    else
    {
      try
      {
        var matches = UiaSelectors.Select(snapshot, selector!, limit: 1);
        if (matches.Count <= 0)
        {
          var intent = CandidateHints.IntentFromSelector(selector!.Expr);
          var candidates = CandidateHints.Suggest(snapshot.Elements, intent);
          return new Resolution(
            Ok: false,
            Error: PeekuErrors.Create(
              PeekuErrorCode.ElementNotFound,
              "Selector did not match any elements.",
              new { selector = selector!.Expr, candidates }),
            Warning: snapshot.Meta.Warning);
        }

        refId = matches[0].RefId;
      }
      catch (ArgumentException ex)
      {
        return new Resolution(
          Ok: false,
          Error: PeekuErrors.Create(
            PeekuErrorCode.InvalidArgument,
            "Invalid selector.",
            new { selector = selector!.Expr, error = ex.Message }),
          Warning: snapshot.Meta.Warning);
      }
      catch (Exception ex)
      {
        return new Resolution(
          Ok: false,
          Error: PeekuErrors.Create(
            PeekuErrorCode.Internal,
            "Selector evaluation failed.",
            new { selector = selector!.Expr, exception = ex.GetType().FullName, ex.Message, ex.HResult }),
          Warning: snapshot.Meta.Warning);
      }
    }

    UiaElement? found = null;
    for (var i = 0; i < snapshot.Elements.Count; i++)
    {
      var e = snapshot.Elements[i];
      if (string.Equals(e.Element.RefId, refId, StringComparison.Ordinal))
      {
        found = e;
        break;
      }
    }

    if (found is null)
    {
      return new Resolution(
        Ok: false,
        Error: PeekuErrors.Create(
          PeekuErrorCode.ElementNotFound,
          "Element refId not present in current snapshot.",
          new { refId, target = targetUsed }),
        Warning: snapshot.Meta.Warning);
    }

    if (found.Rect is null)
    {
      return new Resolution(
        Ok: false,
        Error: PeekuErrors.Create(
          PeekuErrorCode.NotFound,
          "Element rect not available.",
          new { refId, target = targetUsed }),
        Warning: snapshot.Meta.Warning);
    }

    return new Resolution(
      Ok: true,
      Selection: new ResolvedActionSelection(
        Target: targetUsed,
        SnapshotId: snapshot.SnapshotId,
        Element: found.Element,
        Rect: found.Rect),
      Warning: snapshot.Meta.Warning);
  }

  private static Resolution ResolveLive(
    Selector selector,
    Target targetUsed,
    Func<Target, FlaUI.UIA3.UIA3Automation, CancellationToken, (FlaUI.Core.AutomationElements.AutomationElement? Root, string? Warning)> resolveRoot,
    CancellationToken ct)
  {
    using var automation = new FlaUI.UIA3.UIA3Automation();

    var (root, rootWarning) = resolveRoot(targetUsed, automation, ct);
    if (root is null)
    {
      var code = rootWarning is not null && rootWarning.Contains("not supported", StringComparison.OrdinalIgnoreCase)
        ? PeekuErrorCode.NotSupported
        : PeekuErrorCode.WindowNotFound;

      return new Resolution(
        Ok: false,
        Error: PeekuErrors.Create(code, "Target window not found."),
        Warning: rootWarning);
    }

    FlaUI.Core.AutomationElements.AutomationElement? match;
    try
    {
      var matches = UiaLiveSelectors.Select(root, selector, limit: 1, ct);
      match = matches.Count > 0 ? matches[0] : null;
    }
    catch (ArgumentException ex)
    {
      return new Resolution(
        Ok: false,
        Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Invalid selector.", new { selector = selector.Expr, error = ex.Message }),
        Warning: rootWarning);
    }

    if (match is null)
    {
      var liveIntent = CandidateHints.IntentFromSelector(selector.Expr);
      var liveCandidates = CollectLiveCandidates(root, liveIntent, maxVisited: 2000, maxCollect: 20, ct);
      return new Resolution(
        Ok: false,
        Error: PeekuErrors.Create(
          PeekuErrorCode.ElementNotFound,
          "Selector did not match any elements.",
          new { selector = selector.Expr, candidates = liveCandidates }),
        Warning: rootWarning);
    }

    string refId;
    try
    {
      refId = UiaRefId.Create(match);
    }
    catch (Exception ex)
    {
      return new Resolution(
        Ok: false,
        Error: PeekuErrors.Create(
          PeekuErrorCode.Internal,
          "Failed to compute element refId.",
          new { exception = ex.GetType().FullName, ex.Message, ex.HResult }),
        Warning: rootWarning);
    }

    var rect = ReadRect(match);
    if (rect is null)
    {
      return new Resolution(
        Ok: false,
        Error: PeekuErrors.Create(
          PeekuErrorCode.NotFound,
          "Element rect not available.",
          new { refId, target = targetUsed }),
        Warning: rootWarning);
    }

    return new Resolution(
      Ok: true,
      Selection: new ResolvedActionSelection(
        Target: targetUsed,
        SnapshotId: "",
        Element: new ElementRef(refId),
        Rect: rect),
      Warning: rootWarning);
  }

  private static async Task<Resolution> ResolveFocused(
    Target targetUsed,
    Func<Target, FlaUI.UIA3.UIA3Automation, CancellationToken, (FlaUI.Core.AutomationElements.AutomationElement? Root, string? Warning)> resolveRoot,
    CancellationToken ct)
  {
    using var automation = new FlaUI.UIA3.UIA3Automation();

    var (root, rootWarning) = resolveRoot(targetUsed, automation, ct);
    if (root is null)
    {
      var code = rootWarning is not null && rootWarning.Contains("not supported", StringComparison.OrdinalIgnoreCase)
        ? PeekuErrorCode.NotSupported
        : PeekuErrorCode.WindowNotFound;

      return new Resolution(
        Ok: false,
        Error: PeekuErrors.Create(code, "Target window not found."),
        Warning: rootWarning);
    }

    // Focused element is system-global; bring the target window forward so keyboard focus is inside
    // it before querying, then settle briefly.
    try
    {
      var hwnd = root.Properties.NativeWindowHandle.ValueOrDefault;
      if (hwnd != IntPtr.Zero)
      {
        Win32Windows.BringToForeground(hwnd);
        await Task.Delay(75, ct).ConfigureAwait(false);
      }
    }
    catch (OperationCanceledException)
    {
      throw;
    }
    catch
    {
      // Foreground best-effort; continue with whatever has focus.
    }

    FlaUI.Core.AutomationElements.AutomationElement? focused = null;
    try
    {
      focused = automation.FocusedElement();
    }
    catch
    {
      focused = null;
    }

    // Validate the focused element is within the target window's subtree; otherwise fall back to root.
    var chosen = IsWithinWindow(automation, root, focused) ? focused! : root;

    string refId;
    try
    {
      refId = UiaRefId.Create(chosen);
    }
    catch (Exception ex)
    {
      return new Resolution(
        Ok: false,
        Error: PeekuErrors.Create(
          PeekuErrorCode.Internal,
          "Failed to compute element refId.",
          new { exception = ex.GetType().FullName, ex.Message, ex.HResult }),
        Warning: rootWarning);
    }

    var rect = ReadRect(chosen) ?? ReadRect(root);
    if (rect is null)
    {
      return new Resolution(
        Ok: false,
        Error: PeekuErrors.Create(
          PeekuErrorCode.NotFound,
          "Element rect not available.",
          new { refId, target = targetUsed }),
        Warning: rootWarning);
    }

    var warning = CombineWarnings(rootWarning, "No element/selector provided; targeted the window's focused element.");

    return new Resolution(
      Ok: true,
      Selection: new ResolvedActionSelection(
        Target: targetUsed,
        SnapshotId: "",
        Element: new ElementRef(refId),
        Rect: rect),
      Warning: warning);
  }

  private static bool IsWithinWindow(
    FlaUI.UIA3.UIA3Automation automation,
    FlaUI.Core.AutomationElements.AutomationElement root,
    FlaUI.Core.AutomationElements.AutomationElement? focused)
  {
    if (focused is null)
    {
      return false;
    }

    try
    {
      if (focused.Equals(root))
      {
        return true;
      }

      var walker = automation.TreeWalkerFactory.GetControlViewWalker();
      var current = focused;
      for (var i = 0; i < 40; i++)
      {
        var parent = walker.GetParent(current);
        if (parent is null)
        {
          return false;
        }

        if (parent.Equals(root))
        {
          return true;
        }

        current = parent;
      }
    }
    catch
    {
      return false;
    }

    return false;
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

  private static Rect? ReadRect(FlaUI.Core.AutomationElements.AutomationElement element)
  {
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

  /// <summary>
  /// Bounded BFS over the live UIA tree, collecting up to <paramref name="maxCollect"/> elements
  /// (with name or automationId) for candidate-hint ranking. Visits at most
  /// <paramref name="maxVisited"/> nodes to stay cheap on large trees.
  /// </summary>
  private static IReadOnlyList<CandidateHints.Candidate> CollectLiveCandidates(
    FlaUI.Core.AutomationElements.AutomationElement root,
    string? intent,
    int maxVisited,
    int maxCollect,
    CancellationToken ct)
  {
    var pool = new List<UiaElement>(capacity: Math.Min(maxCollect * 4, 64));
    var stack = new Stack<FlaUI.Core.AutomationElements.AutomationElement>(capacity: 64);
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
      rect = ReadRect(el);

      if (!string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(automationId))
      {
        pool.Add(new UiaElement(
          Element: new ElementRef(""),
          Rect: rect,
          Name: string.IsNullOrWhiteSpace(name) ? null : name,
          ControlType: string.IsNullOrWhiteSpace(controlType) ? null : controlType,
          AutomationId: string.IsNullOrWhiteSpace(automationId) ? null : automationId));
      }

      FlaUI.Core.AutomationElements.AutomationElement[] children;
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
