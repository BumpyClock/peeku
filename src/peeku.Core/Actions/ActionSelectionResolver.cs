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

    if (hasElement == hasSelector)
    {
      return new Resolution(
        Ok: false,
        Error: PeekuErrors.Create(
          PeekuErrorCode.InvalidArgument,
          "Provide exactly one of elementRef/refId or selector.",
          new { hasElementRef = hasElement, hasSelector }));
    }

    var targetUsed = target ?? Target.Focused();

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
          return new Resolution(
            Ok: false,
            Error: PeekuErrors.Create(
              PeekuErrorCode.ElementNotFound,
              "Selector did not match any elements.",
              new { selector = selector!.Expr }),
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
      return new Resolution(
        Ok: false,
        Error: PeekuErrors.Create(
          PeekuErrorCode.ElementNotFound,
          "Selector did not match any elements.",
          new { selector = selector.Expr }),
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
}
