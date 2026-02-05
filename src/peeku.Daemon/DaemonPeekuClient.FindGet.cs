using FlaUI.Core.AutomationElements;
using peeku;

namespace peeku.Daemon;

public sealed partial class DaemonPeekuClient
{
  public async Task<FindResult> FindAsync(FindRequest req, CancellationToken ct = default)
  {
    var scope = Results.Start();

    try
    {
      ThrowIfDisposed();
      ct.ThrowIfCancellationRequested();

      if (req is null)
      {
        return new FindResult(
          Ok: false,
          Meta: scope.Meta(),
          Matches: Array.Empty<FindMatch>(),
          Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Request is required."));
      }

      if (req.Selector is null || string.IsNullOrWhiteSpace(req.Selector.Expr))
      {
        return new FindResult(
          Ok: false,
          Meta: scope.Meta(),
          Matches: Array.Empty<FindMatch>(),
          Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Selector is required."));
      }

      if (req.Limit <= 0)
      {
        return new FindResult(
          Ok: true,
          Meta: scope.Meta(),
          Matches: Array.Empty<FindMatch>());
      }

      var targetUsed = req.Target ?? Target.Focused();

      if (req.Selector.PreferCachedSnapshot)
      {
        var snapshot = await UiaSnapshotAsync(
          new UiaSnapshotRequest(
            Target: targetUsed,
            Depth: 6,
            MaxNodes: 5000,
            IncludeProperties: UiaPropertiesMode.Basic),
          ct).ConfigureAwait(false);

        if (!snapshot.Ok)
        {
          return new FindResult(
            Ok: false,
            Meta: scope.Meta(warning: snapshot.Meta.Warning),
            Matches: Array.Empty<FindMatch>(),
            Error: snapshot.Error ?? PeekuErrors.Create(PeekuErrorCode.Internal, "UIA snapshot failed."));
        }

        IReadOnlyList<ElementRef> refs;
        try
        {
          refs = UiaSelectors.Select(snapshot, req.Selector, limit: Math.Min(req.Limit * 4, 5000));
        }
        catch (ArgumentException ex)
        {
          return new FindResult(
            Ok: false,
            Meta: scope.Meta(warning: snapshot.Meta.Warning),
            Matches: Array.Empty<FindMatch>(),
            Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Invalid selector.", new { selector = req.Selector.Expr, error = ex.Message }));
        }

        var byRefId = snapshot.Elements.ToDictionary(e => e.Element.RefId, StringComparer.Ordinal);
        var matches = new List<FindMatch>(capacity: Math.Clamp(req.Limit, 0, 256));

        var missingRects = 0;
        for (var i = 0; i < refs.Count; i++)
        {
          if (matches.Count >= req.Limit)
          {
            break;
          }

          var r = refs[i];
          if (!byRefId.TryGetValue(r.RefId, out var el) || el.Rect is null)
          {
            missingRects++;
            continue;
          }

          matches.Add(new FindMatch(
            Element: r,
            Rect: el.Rect,
            Score: 1.0,
            Name: el.Name,
            ControlType: el.ControlType));
        }

        var warning = CombineWarnings(
          snapshot.Meta.Warning,
          missingRects > 0 ? $"Skipped {missingRects} matches with no rect." : null);

        return new FindResult(
          Ok: true,
          Meta: scope.Meta(warning: warning),
          Matches: matches);
      }

      var root = ResolveRoot(targetUsed, ct, out var rootWarning);
      if (root is null)
      {
        var code = rootWarning is not null && rootWarning.Contains("not supported", StringComparison.OrdinalIgnoreCase)
          ? PeekuErrorCode.NotSupported
          : PeekuErrorCode.WindowNotFound;

        return new FindResult(
          Ok: false,
          Meta: scope.Meta(warning: rootWarning),
          Matches: Array.Empty<FindMatch>(),
          Error: PeekuErrors.Create(code, "Target window not found."));
      }

      IReadOnlyList<AutomationElement> elements;
      try
      {
        elements = UiaLiveSelectors.Select(root, req.Selector, limit: Math.Min(req.Limit * 4, 5000), ct);
      }
      catch (ArgumentException ex)
      {
        return new FindResult(
          Ok: false,
          Meta: scope.Meta(warning: rootWarning),
          Matches: Array.Empty<FindMatch>(),
          Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Invalid selector.", new { selector = req.Selector.Expr, error = ex.Message }));
      }

      var liveMatches = new List<FindMatch>(capacity: Math.Clamp(req.Limit, 0, 256));
      var missingRectsLive = 0;

      for (var i = 0; i < elements.Count; i++)
      {
        if (liveMatches.Count >= req.Limit)
        {
          break;
        }

        var el = elements[i];
        var rect = ReadRect(el, UiaPropertiesMode.Basic);
        if (rect is null)
        {
          missingRectsLive++;
          continue;
        }

        var refId = StoreHandle(el);

        liveMatches.Add(new FindMatch(
          Element: new ElementRef(refId),
          Rect: rect,
          Score: 1.0,
          Name: ReadName(el, UiaPropertiesMode.Basic),
          ControlType: ReadControlType(el, UiaPropertiesMode.Basic)));
      }

      var liveWarning = CombineWarnings(
        rootWarning,
        missingRectsLive > 0 ? $"Skipped {missingRectsLive} matches with no rect." : null);

      return new FindResult(
        Ok: true,
        Meta: scope.Meta(warning: liveWarning),
        Matches: liveMatches);
    }
    catch (OperationCanceledException)
    {
      return new FindResult(
        Ok: false,
        Meta: scope.Meta(),
        Matches: Array.Empty<FindMatch>(),
        Error: PeekuErrors.Create(PeekuErrorCode.Canceled, "Operation cancelled."));
    }
    catch (Exception ex)
    {
      return new FindResult(
        Ok: false,
        Meta: scope.Meta(),
        Matches: Array.Empty<FindMatch>(),
        Error: PeekuErrors.Create(
          PeekuErrorCode.Internal,
          "Find failed.",
          new { exception = ex.GetType().FullName, ex.Message, ex.HResult }));
    }
  }

  public async Task<ElementGetResult> ElementGetAsync(ElementGetRequest req, CancellationToken ct = default)
  {
    var scope = Results.Start();

    try
    {
      ThrowIfDisposed();
      ct.ThrowIfCancellationRequested();

      if (req is null)
      {
        return new ElementGetResult(
          Ok: false,
          Meta: scope.Meta(),
          Element: new UiaElement(new ElementRef("")),
          Properties: new Dictionary<string, object?>(),
          Patterns: Array.Empty<string>(),
          Rect: null,
          Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Request is required."));
      }

      if (!Enum.IsDefined(typeof(UiaPropertiesMode), req.IncludeProperties))
      {
        return new ElementGetResult(
          Ok: false,
          Meta: scope.Meta(),
          Element: new UiaElement(new ElementRef("")),
          Properties: new Dictionary<string, object?>(),
          Patterns: Array.Empty<string>(),
          Rect: null,
          Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "IncludeProperties is invalid."));
      }

      var hasRefId = req.Element is not null && !string.IsNullOrWhiteSpace(req.Element.RefId);
      var hasSelector = req.Selector is not null && !string.IsNullOrWhiteSpace(req.Selector.Expr);
      if (hasRefId == hasSelector)
      {
        return new ElementGetResult(
          Ok: false,
          Meta: scope.Meta(),
          Element: new UiaElement(new ElementRef("")),
          Properties: new Dictionary<string, object?>(),
          Patterns: Array.Empty<string>(),
          Rect: null,
          Error: PeekuErrors.Create(
            PeekuErrorCode.InvalidArgument,
            "Provide exactly one of elementRef or selector.",
            new { hasRefId, hasSelector }));
      }

      var targetUsed = req.Target ?? Target.Focused();
      var selectorWarning = default(string);
      var snapshotId = "";

      string refId;
      AutomationElement element;

      if (hasRefId)
      {
        refId = req.Element!.RefId.Trim();
        snapshotId = req.Element!.SnapshotId?.Trim() ?? "";

        if (_handles.TryGet(refId, out element))
        {
        }
        else if (_handles.IsHandleId(refId))
        {
          return new ElementGetResult(
            Ok: false,
            Meta: scope.Meta(),
            Element: new UiaElement(new ElementRef(refId, snapshotId)),
            Properties: new Dictionary<string, object?>(),
            Patterns: Array.Empty<string>(),
            Rect: null,
            Error: PeekuErrors.Create(PeekuErrorCode.ElementNotFound, "Element handle not found.", new { refId }));
        }
        else
        {
          var root = ResolveRoot(targetUsed, ct, out var rootWarning);
          if (root is null)
          {
            var code = rootWarning is not null && rootWarning.Contains("not supported", StringComparison.OrdinalIgnoreCase)
              ? PeekuErrorCode.NotSupported
              : PeekuErrorCode.WindowNotFound;

            return new ElementGetResult(
              Ok: false,
              Meta: scope.Meta(warning: rootWarning),
              Element: new UiaElement(new ElementRef(refId, snapshotId)),
              Properties: new Dictionary<string, object?>(),
              Patterns: Array.Empty<string>(),
              Rect: null,
              Error: PeekuErrors.Create(code, "Target window not found."));
          }

          var found = FindByRefId(root, refId, maxNodes: 20_000, ct);
          if (found is null)
          {
            return new ElementGetResult(
              Ok: false,
              Meta: scope.Meta(warning: rootWarning),
              Element: new UiaElement(new ElementRef(refId, snapshotId)),
              Properties: new Dictionary<string, object?>(),
              Patterns: Array.Empty<string>(),
              Rect: null,
              Error: PeekuErrors.Create(PeekuErrorCode.ElementNotFound, "Element not found.", new { refId, target = targetUsed }));
          }

          element = found;
          refId = StoreHandle(element);
          snapshotId = "";
          selectorWarning = rootWarning;
        }
      }
      else
      {
        if (!req.Selector!.PreferCachedSnapshot)
        {
          var root = ResolveRoot(targetUsed, ct, out var rootWarning);
          if (root is null)
          {
            var code = rootWarning is not null && rootWarning.Contains("not supported", StringComparison.OrdinalIgnoreCase)
              ? PeekuErrorCode.NotSupported
              : PeekuErrorCode.WindowNotFound;

            return new ElementGetResult(
              Ok: false,
              Meta: scope.Meta(warning: rootWarning),
              Element: new UiaElement(new ElementRef("")),
              Properties: new Dictionary<string, object?>(),
              Patterns: Array.Empty<string>(),
              Rect: null,
              Error: PeekuErrors.Create(code, "Target window not found."));
          }

          selectorWarning = rootWarning;

          IReadOnlyList<AutomationElement> matches;
          try
          {
            matches = UiaLiveSelectors.Select(root, req.Selector!, limit: 1, ct);
          }
          catch (ArgumentException ex)
          {
            return new ElementGetResult(
              Ok: false,
              Meta: scope.Meta(warning: selectorWarning),
              Element: new UiaElement(new ElementRef("")),
              Properties: new Dictionary<string, object?>(),
              Patterns: Array.Empty<string>(),
              Rect: null,
              Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Invalid selector.", new { selector = req.Selector!.Expr, error = ex.Message }));
          }

          if (matches.Count == 0)
          {
            return new ElementGetResult(
              Ok: false,
              Meta: scope.Meta(warning: selectorWarning),
              Element: new UiaElement(new ElementRef("")),
              Properties: new Dictionary<string, object?>(),
              Patterns: Array.Empty<string>(),
              Rect: null,
              Error: PeekuErrors.Create(
                PeekuErrorCode.ElementNotFound,
                "Selector did not match any elements.",
                new { selector = req.Selector!.Expr, target = targetUsed }));
          }

          element = matches[0];
          refId = StoreHandle(element);
        }
        else
        {
          var snapshot = await UiaSnapshotAsync(
            new UiaSnapshotRequest(
              Target: targetUsed,
              Depth: 6,
              MaxNodes: 5000,
              IncludeProperties: UiaPropertiesMode.All),
            ct).ConfigureAwait(false);

          if (!snapshot.Ok)
          {
            return new ElementGetResult(
              Ok: false,
              Meta: scope.Meta(warning: snapshot.Meta.Warning),
              Element: new UiaElement(new ElementRef("")),
              Properties: new Dictionary<string, object?>(),
              Patterns: Array.Empty<string>(),
              Rect: null,
              Error: snapshot.Error ?? PeekuErrors.Create(PeekuErrorCode.Internal, "UIA snapshot failed."));
          }

          snapshotId = snapshot.SnapshotId;
          selectorWarning = snapshot.Meta.Warning;

          IReadOnlyList<ElementRef> matches;
          try
          {
            matches = UiaSelectors.Select(snapshot, req.Selector!, limit: 1);
          }
          catch (ArgumentException ex)
          {
            return new ElementGetResult(
              Ok: false,
              Meta: scope.Meta(warning: selectorWarning),
              Element: new UiaElement(new ElementRef("")),
              Properties: new Dictionary<string, object?>(),
              Patterns: Array.Empty<string>(),
              Rect: null,
              Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Invalid selector.", new { selector = req.Selector!.Expr, error = ex.Message }));
          }

          if (matches.Count == 0)
          {
            return new ElementGetResult(
              Ok: false,
              Meta: scope.Meta(warning: selectorWarning),
              Element: new UiaElement(new ElementRef("")),
              Properties: new Dictionary<string, object?>(),
              Patterns: Array.Empty<string>(),
              Rect: null,
              Error: PeekuErrors.Create(
                PeekuErrorCode.ElementNotFound,
                "Selector did not match any elements.",
                new { selector = req.Selector!.Expr, target = targetUsed }));
          }

          refId = matches[0].RefId;

          if (!_handles.TryGet(refId, out element))
          {
            return new ElementGetResult(
              Ok: false,
              Meta: scope.Meta(warning: selectorWarning),
              Element: new UiaElement(new ElementRef(refId, snapshotId)),
              Properties: new Dictionary<string, object?>(),
              Patterns: Array.Empty<string>(),
              Rect: null,
              Error: PeekuErrors.Create(PeekuErrorCode.ElementNotFound, "Element handle not available.", new { refId }));
          }
        }
      }

      var el = ReadElement(refId, snapshotId, element, req.IncludeProperties);
      var properties = ReadProperties(element, req.IncludeProperties);
      var patterns = ReadSupportedPatterns(element);

      return new ElementGetResult(
        Ok: true,
        Meta: scope.Meta(warning: selectorWarning),
        Element: el,
        Properties: properties,
        Patterns: patterns,
        Rect: el.Rect);
    }
    catch (OperationCanceledException)
    {
      return new ElementGetResult(
        Ok: false,
        Meta: scope.Meta(),
        Element: new UiaElement(new ElementRef("")),
        Properties: new Dictionary<string, object?>(),
        Patterns: Array.Empty<string>(),
        Rect: null,
        Error: PeekuErrors.Create(PeekuErrorCode.Canceled, "Operation cancelled."));
    }
    catch (Exception ex)
    {
      return new ElementGetResult(
        Ok: false,
        Meta: scope.Meta(),
        Element: new UiaElement(new ElementRef("")),
        Properties: new Dictionary<string, object?>(),
        Patterns: Array.Empty<string>(),
        Rect: null,
        Error: PeekuErrors.Create(
          PeekuErrorCode.Internal,
          "Element get failed.",
          new { exception = ex.GetType().FullName, ex.Message, ex.HResult }));
    }
  }
}
