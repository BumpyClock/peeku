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
          Patterns: new Dictionary<string, object?>(),
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
          Patterns: new Dictionary<string, object?>(),
          Rect: null,
          Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "IncludeProperties is invalid."));
      }

      var hasRefId = !string.IsNullOrWhiteSpace(req.RefId);
      var hasSelector = req.Selector is not null && !string.IsNullOrWhiteSpace(req.Selector.Expr);
      if (hasRefId == hasSelector)
      {
        return new ElementGetResult(
          Ok: false,
          Meta: scope.Meta(),
          Element: new UiaElement(new ElementRef("")),
          Properties: new Dictionary<string, object?>(),
          Patterns: new Dictionary<string, object?>(),
          Rect: null,
          Error: PeekuErrors.Create(
            PeekuErrorCode.InvalidArgument,
            "Provide exactly one of refId or selector.",
            new { hasRefId, hasSelector }));
      }

      var targetUsed = req.Target ?? Target.Focused();
      var selectorWarning = default(string);
      var snapshotId = "";

      string refId;
      if (hasRefId)
      {
        refId = req.RefId!.Trim();
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
            Patterns: new Dictionary<string, object?>(),
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
            Patterns: new Dictionary<string, object?>(),
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
            Patterns: new Dictionary<string, object?>(),
            Rect: null,
            Error: PeekuErrors.Create(
              PeekuErrorCode.ElementNotFound,
              "Selector did not match any elements.",
              new { selector = req.Selector!.Expr, target = targetUsed }));
        }

        refId = matches[0].RefId;
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
          Patterns: new Dictionary<string, object?>(),
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
          Patterns: new Dictionary<string, object?>(),
          Rect: null,
          Error: PeekuErrors.Create(PeekuErrorCode.ElementNotFound, "Element not found.", new { refId, snapshotId, target = targetUsed }));
      }

      var el = ReadElement(refId, snapshotId, element, req.IncludeProperties);
      var properties = ReadProperties(element, req.IncludeProperties);
      var patterns = ReadPatterns(element);

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
        Patterns: new Dictionary<string, object?>(),
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
        Patterns: new Dictionary<string, object?>(),
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
    d["nativeWindowHandle"] = Safe(() => element.Properties.NativeWindowHandle.ValueOrDefault);
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

  private static IReadOnlyDictionary<string, object?> ReadPatterns(AutomationElement element)
    => new Dictionary<string, object?>(StringComparer.Ordinal)
    {
      ["invoke"] = Safe(() => element.Patterns.Invoke.IsSupported),
      ["toggle"] = Safe(() => element.Patterns.Toggle.IsSupported),
      ["value"] = Safe(() => element.Patterns.Value.IsSupported),
      ["selectionItem"] = Safe(() => element.Patterns.SelectionItem.IsSupported),
      ["scroll"] = Safe(() => element.Patterns.Scroll.IsSupported),
      ["expandCollapse"] = Safe(() => element.Patterns.ExpandCollapse.IsSupported),
      ["rangeValue"] = Safe(() => element.Patterns.RangeValue.IsSupported),
      ["gridItem"] = Safe(() => element.Patterns.GridItem.IsSupported),
      ["text"] = Safe(() => element.Patterns.Text.IsSupported),
      ["window"] = Safe(() => element.Patterns.Window.IsSupported),
      ["transform"] = Safe(() => element.Patterns.Transform.IsSupported),
      ["legacyIAccessible"] = Safe(() => element.Patterns.LegacyIAccessible.IsSupported),
    };

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
}
