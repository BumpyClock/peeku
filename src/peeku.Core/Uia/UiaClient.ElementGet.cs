using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace peeku;

public sealed partial class UiaClient
{
  public async Task<ElementGetResult> ElementGetAsync(ElementGetRequest req, CancellationToken ct = default)
  {
    var scope = Results.Start();

    try
    {
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
      if (hasRefId)
      {
        refId = req.Element!.RefId.Trim();
        snapshotId = req.Element!.SnapshotId?.Trim() ?? "";
      }
      else
      {
        if (!req.Selector!.PreferCachedSnapshot)
        {
          using var liveAutomation = new UIA3Automation();
          var liveRoot = ResolveRoot(targetUsed, liveAutomation, ct, out var liveRootWarning);
          if (liveRoot is null)
          {
            var code = liveRootWarning is not null && liveRootWarning.Contains("not supported", StringComparison.OrdinalIgnoreCase)
              ? PeekuErrorCode.NotSupported
              : PeekuErrorCode.WindowNotFound;

            return new ElementGetResult(
              Ok: false,
              Meta: scope.Meta(warning: liveRootWarning),
              Element: new UiaElement(new ElementRef("")),
              Properties: new Dictionary<string, object?>(),
              Patterns: Array.Empty<string>(),
              Rect: null,
              Error: PeekuErrors.Create(code, "Target window not found."));
          }

          selectorWarning = liveRootWarning;

          IReadOnlyList<AutomationElement> matches;
          try
          {
            matches = UiaLiveSelectors.Select(liveRoot, req.Selector!, limit: 1, ct);
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
            var cLiveIntent = CandidateHints.IntentFromSelector(req.Selector!.Expr);
            var cLiveCandidates = CollectLiveCandidatesInProc(liveRoot, cLiveIntent, maxVisited: 2000, maxCollect: 20, ct);
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
                new { selector = req.Selector!.Expr, target = targetUsed, candidates = cLiveCandidates }));
          }

          var match = matches[0];

          try
          {
            refId = UiaRefId.Create(match);
          }
          catch (Exception ex)
          {
            return new ElementGetResult(
              Ok: false,
              Meta: scope.Meta(warning: selectorWarning),
              Element: new UiaElement(new ElementRef("")),
              Properties: new Dictionary<string, object?>(),
              Patterns: Array.Empty<string>(),
              Rect: null,
              Error: PeekuErrors.Create(
                PeekuErrorCode.Internal,
                "Failed to compute element refId.",
                new { exception = ex.GetType().FullName, ex.Message, ex.HResult }));
          }
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
            var cSnapIntent = CandidateHints.IntentFromSelector(req.Selector!.Expr);
            var cSnapCandidates = CandidateHints.Suggest(snapshot.Elements, cSnapIntent);
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
                new { selector = req.Selector!.Expr, target = targetUsed, candidates = cSnapCandidates }));
          }

          refId = matches[0].RefId;
        }
      }

      using var automation = new UIA3Automation();
      var root = ResolveRoot(targetUsed, automation, ct, out var rootWarning);
      if (root is null)
      {
        var code = rootWarning is not null && rootWarning.Contains("not supported", StringComparison.OrdinalIgnoreCase)
          ? PeekuErrorCode.NotSupported
          : PeekuErrorCode.WindowNotFound;

        return new ElementGetResult(
          Ok: false,
          Meta: scope.Meta(warning: CombineWarnings(selectorWarning, rootWarning)),
          Element: new UiaElement(new ElementRef("")),
          Properties: new Dictionary<string, object?>(),
          Patterns: Array.Empty<string>(),
          Rect: null,
          Error: PeekuErrors.Create(code, "Target window not found."));
      }

      var element = FindByRefId(root, refId, maxNodes: 20_000, ct);
      if (element is null)
      {
        return new ElementGetResult(
          Ok: false,
          Meta: scope.Meta(warning: CombineWarnings(selectorWarning, rootWarning)),
          Element: new UiaElement(new ElementRef(refId, snapshotId)),
          Properties: new Dictionary<string, object?>(),
          Patterns: Array.Empty<string>(),
          Rect: null,
          Error: PeekuErrors.Create(PeekuErrorCode.ElementNotFound, "Element not found.", new { refId, snapshotId, target = targetUsed }));
      }

      var el = ReadElement(refId, snapshotId, element, req.IncludeProperties);
      var properties = ReadProperties(element, req.IncludeProperties);
      var patterns = ReadSupportedPatterns(element);

      return new ElementGetResult(
        Ok: true,
        Meta: scope.Meta(warning: CombineWarnings(selectorWarning, rootWarning)),
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
