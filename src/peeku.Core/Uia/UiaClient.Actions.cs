using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace peeku;

public sealed partial class UiaClient
{
  public async Task<ActionResult> ClickAsync(ClickRequest req, CancellationToken ct = default)
  {
    var scope = Results.Start();
    try
    {
      ct.ThrowIfCancellationRequested();

      if (req is null)
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(),
          Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Request is required."));
      }

      var methodUsed = ActionMethodRouter.Route(req.Method, uiaSupported: true, inputSupported: false, out var routeError);
      if (routeError is not null)
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(),
          MethodUsed: methodUsed,
          Error: routeError);
      }

      var resolved = await ActionSelectionResolver.ResolveAsync(UiaSnapshotAsync, req.Element, req.Selector, req.Target, ct).ConfigureAwait(false);
      if (!resolved.Ok || resolved.Selection is null)
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(warning: resolved.Warning),
          MethodUsed: methodUsed,
          Error: resolved.Error ?? PeekuErrors.Create(PeekuErrorCode.Internal, "Selection resolution failed."));
      }

      if (methodUsed == ActionMethod.Input)
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(warning: resolved.Warning),
          MethodUsed: methodUsed,
          Error: PeekuErrors.Create(PeekuErrorCode.NotSupported, "Input click not supported yet."));
      }

      using var automation = new UIA3Automation();
      var root = ResolveRoot(resolved.Selection.Target, automation, ct, out var rootWarning);
      if (root is null)
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(warning: CombineWarnings(resolved.Warning, rootWarning)),
          MethodUsed: methodUsed,
          Error: PeekuErrors.Create(PeekuErrorCode.WindowNotFound, "Target window not found."));
      }

      var element = FindByRefId(root, resolved.Selection.Element.RefId, maxNodes: 20_000, ct);
      if (element is null)
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(warning: CombineWarnings(resolved.Warning, rootWarning)),
          MethodUsed: methodUsed,
          Error: PeekuErrors.Create(
            PeekuErrorCode.ElementNotFound,
            "Element not found for click.",
            new { refId = resolved.Selection.Element.RefId, snapshotId = resolved.Selection.SnapshotId }));
      }

      if (!TryClickViaUiaPatterns(element, out var clickError))
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(warning: CombineWarnings(resolved.Warning, rootWarning)),
          MethodUsed: methodUsed,
          Error: clickError);
      }

      return new ActionResult(
        Ok: true,
        Meta: scope.Meta(warning: CombineWarnings(resolved.Warning, rootWarning)),
        MethodUsed: methodUsed);
    }
    catch (OperationCanceledException)
    {
      return new ActionResult(
        Ok: false,
        Meta: scope.Meta(),
        Error: PeekuErrors.Create(PeekuErrorCode.Canceled, "Operation cancelled."));
    }
    catch (Exception ex)
    {
      return new ActionResult(
        Ok: false,
        Meta: scope.Meta(),
        Error: PeekuErrors.Create(
          PeekuErrorCode.Internal,
          "Click failed.",
          new { exception = ex.GetType().FullName, ex.Message, ex.HResult }));
    }
  }

  public async Task<ActionResult> InvokeAsync(InvokeRequest req, CancellationToken ct = default)
  {
    ActionMethod? methodUsed = null;
    var scope = Results.Start();
    try
    {
      ct.ThrowIfCancellationRequested();

      if (req is null)
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(),
          Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Request is required."));
      }

      methodUsed = ActionMethodRouter.Route(ActionMethod.Auto, uiaSupported: true, inputSupported: false, out var routeError);
      if (routeError is not null)
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(),
          MethodUsed: methodUsed,
          Error: routeError);
      }

      var resolved = await ActionSelectionResolver.ResolveAsync(UiaSnapshotAsync, req.Element, req.Selector, req.Target, ct).ConfigureAwait(false);
      if (!resolved.Ok || resolved.Selection is null)
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(warning: resolved.Warning),
          MethodUsed: methodUsed,
          Error: resolved.Error ?? PeekuErrors.Create(PeekuErrorCode.Internal, "Selection resolution failed."));
      }

      using var automation = new UIA3Automation();
      var root = ResolveRoot(resolved.Selection.Target, automation, ct, out var rootWarning);
      if (root is null)
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(warning: CombineWarnings(resolved.Warning, rootWarning)),
          MethodUsed: methodUsed,
          Error: PeekuErrors.Create(PeekuErrorCode.WindowNotFound, "Target window not found."));
      }

      var element = FindByRefId(root, resolved.Selection.Element.RefId, maxNodes: 20_000, ct);
      if (element is null)
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(warning: CombineWarnings(resolved.Warning, rootWarning)),
          MethodUsed: methodUsed,
          Error: PeekuErrors.Create(PeekuErrorCode.ElementNotFound, "Element not found for invoke.", new { refId = resolved.Selection.Element.RefId }));
      }

      if (!TryInvoke(element, out var invokeError))
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(warning: CombineWarnings(resolved.Warning, rootWarning)),
          MethodUsed: methodUsed,
          Error: invokeError);
      }

      return new ActionResult(
        Ok: true,
        Meta: scope.Meta(warning: CombineWarnings(resolved.Warning, rootWarning)),
        MethodUsed: methodUsed);
    }
    catch (OperationCanceledException)
    {
      return new ActionResult(
        Ok: false,
        Meta: scope.Meta(),
        MethodUsed: methodUsed,
        Error: PeekuErrors.Create(PeekuErrorCode.Canceled, "Operation cancelled."));
    }
    catch (Exception ex)
    {
      return new ActionResult(
        Ok: false,
        Meta: scope.Meta(),
        MethodUsed: methodUsed,
        Error: PeekuErrors.Create(
          PeekuErrorCode.Internal,
          "Invoke failed.",
          new { exception = ex.GetType().FullName, ex.Message, ex.HResult }));
    }
  }

  public async Task<ActionResult> SetValueAsync(SetValueRequest req, CancellationToken ct = default)
  {
    ActionMethod? methodUsed = null;
    var scope = Results.Start();
    try
    {
      ct.ThrowIfCancellationRequested();

      if (req is null)
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(),
          Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Request is required."));
      }

      methodUsed = ActionMethodRouter.Route(ActionMethod.Auto, uiaSupported: true, inputSupported: false, out var routeError);
      if (routeError is not null)
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(),
          MethodUsed: methodUsed,
          Error: routeError);
      }

      var resolved = await ActionSelectionResolver.ResolveAsync(UiaSnapshotAsync, req.Element, req.Selector, req.Target, ct).ConfigureAwait(false);
      if (!resolved.Ok || resolved.Selection is null)
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(warning: resolved.Warning),
          MethodUsed: methodUsed,
          Error: resolved.Error ?? PeekuErrors.Create(PeekuErrorCode.Internal, "Selection resolution failed."));
      }

      using var automation = new UIA3Automation();
      var root = ResolveRoot(resolved.Selection.Target, automation, ct, out var rootWarning);
      if (root is null)
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(warning: CombineWarnings(resolved.Warning, rootWarning)),
          MethodUsed: methodUsed,
          Error: PeekuErrors.Create(PeekuErrorCode.WindowNotFound, "Target window not found."));
      }

      var element = FindByRefId(root, resolved.Selection.Element.RefId, maxNodes: 20_000, ct);
      if (element is null)
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(warning: CombineWarnings(resolved.Warning, rootWarning)),
          MethodUsed: methodUsed,
          Error: PeekuErrors.Create(PeekuErrorCode.ElementNotFound, "Element not found for set value.", new { refId = resolved.Selection.Element.RefId }));
      }

      if (!TrySetValue(element, req.Value, out var setError))
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(warning: CombineWarnings(resolved.Warning, rootWarning)),
          MethodUsed: methodUsed,
          Error: setError);
      }

      return new ActionResult(
        Ok: true,
        Meta: scope.Meta(warning: CombineWarnings(resolved.Warning, rootWarning)),
        MethodUsed: methodUsed);
    }
    catch (OperationCanceledException)
    {
      return new ActionResult(
        Ok: false,
        Meta: scope.Meta(),
        MethodUsed: methodUsed,
        Error: PeekuErrors.Create(PeekuErrorCode.Canceled, "Operation cancelled."));
    }
    catch (Exception ex)
    {
      return new ActionResult(
        Ok: false,
        Meta: scope.Meta(),
        MethodUsed: methodUsed,
        Error: PeekuErrors.Create(
          PeekuErrorCode.Internal,
          "Set value failed.",
          new { exception = ex.GetType().FullName, ex.Message, ex.HResult }));
    }
  }

  public async Task<ActionResult> TypeAsync(TypeRequest req, CancellationToken ct = default)
  {
    ActionMethod? methodUsed = null;
    var scope = Results.Start();
    try
    {
      ct.ThrowIfCancellationRequested();

      if (req is null)
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(),
          Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Request is required."));
      }

      methodUsed = ActionMethodRouter.Route(ActionMethod.Auto, uiaSupported: true, inputSupported: false, out var routeError);
      if (routeError is not null)
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(),
          MethodUsed: methodUsed,
          Error: routeError);
      }

      if (string.IsNullOrEmpty(req.Text))
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(),
          MethodUsed: methodUsed,
          Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Text is required."));
      }

      if (req.DelayMs is not null && req.DelayMs.Value < 0)
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(),
          MethodUsed: methodUsed,
          Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "DelayMs must be >= 0."));
      }

      var resolved = await ActionSelectionResolver.ResolveAsync(UiaSnapshotAsync, req.Element, req.Selector, req.Target, ct).ConfigureAwait(false);
      if (!resolved.Ok || resolved.Selection is null)
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(warning: resolved.Warning),
          MethodUsed: methodUsed,
          Error: resolved.Error ?? PeekuErrors.Create(PeekuErrorCode.Internal, "Selection resolution failed."));
      }

      using var automation = new UIA3Automation();
      var root = ResolveRoot(resolved.Selection.Target, automation, ct, out var rootWarning);
      if (root is null)
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(warning: CombineWarnings(resolved.Warning, rootWarning)),
          MethodUsed: methodUsed,
          Error: PeekuErrors.Create(PeekuErrorCode.WindowNotFound, "Target window not found."));
      }

      var element = FindByRefId(root, resolved.Selection.Element.RefId, maxNodes: 20_000, ct);
      if (element is null)
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(warning: CombineWarnings(resolved.Warning, rootWarning)),
          MethodUsed: methodUsed,
          Error: PeekuErrors.Create(PeekuErrorCode.ElementNotFound, "Element not found for type.", new { refId = resolved.Selection.Element.RefId }));
      }

      var delayMs = req.DelayMs.GetValueOrDefault(0);

      var baseText = "";
      if (req.Append && TryGetValue(element, out var current))
      {
        baseText = current ?? "";
      }

      if (delayMs <= 0)
      {
        if (!TrySetValue(element, baseText + req.Text, out var setError))
        {
          return new ActionResult(
            Ok: false,
            Meta: scope.Meta(warning: CombineWarnings(resolved.Warning, rootWarning)),
            MethodUsed: methodUsed,
            Error: setError);
        }

        return new ActionResult(
          Ok: true,
          Meta: scope.Meta(warning: CombineWarnings(resolved.Warning, rootWarning)),
          MethodUsed: methodUsed);
      }

      var typed = baseText;
      for (var i = 0; i < req.Text.Length; i++)
      {
        ct.ThrowIfCancellationRequested();
        typed += req.Text[i];

        if (!TrySetValue(element, typed, out var setError))
        {
          return new ActionResult(
            Ok: false,
            Meta: scope.Meta(warning: CombineWarnings(resolved.Warning, rootWarning)),
            MethodUsed: methodUsed,
            Error: setError);
        }

        await Task.Delay(delayMs, ct).ConfigureAwait(false);
      }

      return new ActionResult(
        Ok: true,
        Meta: scope.Meta(warning: CombineWarnings(resolved.Warning, rootWarning)),
        MethodUsed: methodUsed);
    }
    catch (OperationCanceledException)
    {
      return new ActionResult(
        Ok: false,
        Meta: scope.Meta(),
        MethodUsed: methodUsed,
        Error: PeekuErrors.Create(PeekuErrorCode.Canceled, "Operation cancelled."));
    }
    catch (Exception ex)
    {
      return new ActionResult(
        Ok: false,
        Meta: scope.Meta(),
        MethodUsed: methodUsed,
        Error: PeekuErrors.Create(
          PeekuErrorCode.Internal,
          "Type failed.",
          new { exception = ex.GetType().FullName, ex.Message, ex.HResult }));
    }
  }

  public async Task<ActionResult> ScrollAsync(ScrollRequest req, CancellationToken ct = default)
  {
    var scope = Results.Start();
    try
    {
      ct.ThrowIfCancellationRequested();

      if (req is null)
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(),
          Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Request is required."));
      }

      if (req.Delta is null && req.Lines is null)
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(),
          MethodUsed: ActionMethod.Uia,
          Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Provide delta or lines."));
      }

      if (req.Lines is not null && req.Lines.Value == 0)
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(),
          MethodUsed: ActionMethod.Uia,
          Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Lines must be non-zero."));
      }

      if (req.Delta is not null && req.Delta.Value == 0)
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(),
          MethodUsed: ActionMethod.Uia,
          Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Delta must be non-zero."));
      }

      var resolved = await ActionSelectionResolver.ResolveAsync(UiaSnapshotAsync, req.Element, req.Selector, req.Target, ct).ConfigureAwait(false);
      if (!resolved.Ok || resolved.Selection is null)
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(warning: resolved.Warning),
          MethodUsed: ActionMethod.Uia,
          Error: resolved.Error ?? PeekuErrors.Create(PeekuErrorCode.Internal, "Selection resolution failed."));
      }

      var stepsRaw = req.Lines ?? (req.Delta is null ? 0 : req.Delta.Value / 120);
      if (stepsRaw == 0)
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(warning: resolved.Warning),
          MethodUsed: ActionMethod.Uia,
          Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Scroll delta too small; use lines or a larger delta."));
      }

      var warning = resolved.Warning;
      var steps = stepsRaw;
      if (Math.Abs(steps) > 50)
      {
        steps = Math.Sign(steps) * 50;
        warning = CombineWarnings(warning, "Scroll steps clamped to 50.");
      }

      using var automation = new UIA3Automation();
      var root = ResolveRoot(resolved.Selection.Target, automation, ct, out var rootWarning);
      if (root is null)
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(warning: CombineWarnings(warning, rootWarning)),
          MethodUsed: ActionMethod.Uia,
          Error: PeekuErrors.Create(PeekuErrorCode.WindowNotFound, "Target window not found."));
      }

      var element = FindByRefId(root, resolved.Selection.Element.RefId, maxNodes: 20_000, ct);
      if (element is null)
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(warning: CombineWarnings(warning, rootWarning)),
          MethodUsed: ActionMethod.Uia,
          Error: PeekuErrors.Create(PeekuErrorCode.ElementNotFound, "Element not found for scroll.", new { refId = resolved.Selection.Element.RefId }));
      }

      if (!element.Patterns.Scroll.IsSupported)
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(warning: CombineWarnings(warning, rootWarning)),
          MethodUsed: ActionMethod.Uia,
          Error: PeekuErrors.Create(PeekuErrorCode.NotSupported, "Element does not support scroll pattern."));
      }

      var pattern = element.Patterns.Scroll.Pattern;
      var amount = steps > 0 ? ScrollAmount.SmallIncrement : ScrollAmount.SmallDecrement;
      var count = Math.Abs(steps);

      try
      {
        for (var i = 0; i < count; i++)
        {
          ct.ThrowIfCancellationRequested();

          if (req.Direction == ScrollDirection.Horizontal)
          {
            pattern.Scroll(amount, ScrollAmount.NoAmount);
          }
          else
          {
            pattern.Scroll(ScrollAmount.NoAmount, amount);
          }
        }
      }
      catch (Exception ex)
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(warning: CombineWarnings(warning, rootWarning)),
          MethodUsed: ActionMethod.Uia,
          Error: PeekuErrors.Create(
            PeekuErrorCode.Internal,
            "Scroll failed.",
            new { exception = ex.GetType().FullName, ex.Message, ex.HResult }));
      }

      return new ActionResult(
        Ok: true,
        Meta: scope.Meta(warning: CombineWarnings(warning, rootWarning)),
        MethodUsed: ActionMethod.Uia);
    }
    catch (OperationCanceledException)
    {
      return new ActionResult(
        Ok: false,
        Meta: scope.Meta(),
        MethodUsed: ActionMethod.Uia,
        Error: PeekuErrors.Create(PeekuErrorCode.Canceled, "Operation cancelled."));
    }
    catch (Exception ex)
    {
      return new ActionResult(
        Ok: false,
        Meta: scope.Meta(),
        MethodUsed: ActionMethod.Uia,
        Error: PeekuErrors.Create(
          PeekuErrorCode.Internal,
          "Scroll failed.",
          new { exception = ex.GetType().FullName, ex.Message, ex.HResult }));
    }
  }

  public Task<ActionResult> HotkeyAsync(HotkeyRequest req, CancellationToken ct = default)
  {
    var scope = Results.Start();

    if (req is null || string.IsNullOrWhiteSpace(req.Keys))
    {
      return Task.FromResult(new ActionResult(
        Ok: false,
        Meta: scope.Meta(),
        MethodUsed: ActionMethod.Input,
        Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Keys is required.")));
    }

    return Task.FromResult(new ActionResult(
      Ok: false,
      Meta: scope.Meta(),
      MethodUsed: ActionMethod.Input,
      Error: PeekuErrors.Create(PeekuErrorCode.NotSupported, "Hotkey requires input injection (not supported yet).")));
  }
}
