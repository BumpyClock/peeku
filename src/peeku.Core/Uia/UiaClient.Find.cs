using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace peeku;

public sealed partial class UiaClient
{
  public async Task<FindResult> FindAsync(FindRequest req, CancellationToken ct = default)
  {
    var scope = Results.Start();

    try
    {
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

      using var automation = new UIA3Automation();
      var root = ResolveRoot(targetUsed, automation, ct, out var rootWarning);
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

        string refId;
        try
        {
          refId = UiaRefId.Create(el);
        }
        catch
        {
          continue;
        }

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
}

