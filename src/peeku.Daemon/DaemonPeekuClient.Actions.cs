using FlaUI.Core.Definitions;
using peeku;

namespace peeku.Daemon;

public sealed partial class DaemonPeekuClient
{
  public async Task<ActionResult> ClickAsync(ClickRequest req, CancellationToken ct = default)
  {
    var scope = Results.Start();
    try
    {
      ThrowIfDisposed();
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

      var resolved = ResolveActionElement(req.Element, req.Selector, req.Target, ct);
      if (!resolved.Ok || resolved.Element is null)
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

      if (!TryClickViaUiaPatterns(resolved.Element, out var clickError))
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(warning: resolved.Warning),
          MethodUsed: methodUsed,
          Error: clickError);
      }

      return new ActionResult(
        Ok: true,
        Meta: scope.Meta(warning: resolved.Warning),
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
      ThrowIfDisposed();
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

      var resolved = ResolveActionElement(req.Element, req.Selector, req.Target, ct);
      if (!resolved.Ok || resolved.Element is null)
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(warning: resolved.Warning),
          MethodUsed: methodUsed,
          Error: resolved.Error ?? PeekuErrors.Create(PeekuErrorCode.Internal, "Selection resolution failed."));
      }

      if (!TryInvoke(resolved.Element, out var invokeError))
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(warning: resolved.Warning),
          MethodUsed: methodUsed,
          Error: invokeError);
      }

      return new ActionResult(
        Ok: true,
        Meta: scope.Meta(warning: resolved.Warning),
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
      ThrowIfDisposed();
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

      var resolved = ResolveActionElement(req.Element, req.Selector, req.Target, ct);
      if (!resolved.Ok || resolved.Element is null)
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(warning: resolved.Warning),
          MethodUsed: methodUsed,
          Error: resolved.Error ?? PeekuErrors.Create(PeekuErrorCode.Internal, "Selection resolution failed."));
      }

      if (!TrySetValue(resolved.Element, req.Value, out var setError))
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(warning: resolved.Warning),
          MethodUsed: methodUsed,
          Error: setError);
      }

      return new ActionResult(
        Ok: true,
        Meta: scope.Meta(warning: resolved.Warning),
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
      ThrowIfDisposed();
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

      var resolved = ResolveActionElement(req.Element, req.Selector, req.Target, ct);
      if (!resolved.Ok || resolved.Element is null)
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(warning: resolved.Warning),
          MethodUsed: methodUsed,
          Error: resolved.Error ?? PeekuErrors.Create(PeekuErrorCode.Internal, "Selection resolution failed."));
      }

      var delayMs = req.DelayMs.GetValueOrDefault(0);

      var baseText = "";
      if (req.Append && TryGetValue(resolved.Element, out var current))
      {
        baseText = current ?? "";
      }

      if (delayMs <= 0)
      {
        if (!TrySetValue(resolved.Element, baseText + req.Text, out var setError))
        {
          return new ActionResult(
            Ok: false,
            Meta: scope.Meta(warning: resolved.Warning),
            MethodUsed: methodUsed,
            Error: setError);
        }

        return new ActionResult(
          Ok: true,
          Meta: scope.Meta(warning: resolved.Warning),
          MethodUsed: methodUsed);
      }

      var typed = baseText;
      for (var i = 0; i < req.Text.Length; i++)
      {
        ct.ThrowIfCancellationRequested();
        typed += req.Text[i];

        if (!TrySetValue(resolved.Element, typed, out var setError))
        {
          return new ActionResult(
            Ok: false,
            Meta: scope.Meta(warning: resolved.Warning),
            MethodUsed: methodUsed,
            Error: setError);
        }

        await Task.Delay(delayMs, ct).ConfigureAwait(false);
      }

      return new ActionResult(
        Ok: true,
        Meta: scope.Meta(warning: resolved.Warning),
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
      ThrowIfDisposed();
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

      var resolved = ResolveActionElement(req.Element, req.Selector, req.Target, ct);
      if (!resolved.Ok || resolved.Element is null)
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

      var element = resolved.Element;
      if (!element.Patterns.Scroll.IsSupported)
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(warning: warning),
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
          Meta: scope.Meta(warning: warning),
          MethodUsed: ActionMethod.Uia,
          Error: PeekuErrors.Create(
            PeekuErrorCode.Internal,
            "Scroll failed.",
            new { exception = ex.GetType().FullName, ex.Message, ex.HResult }));
      }

      return new ActionResult(
        Ok: true,
        Meta: scope.Meta(warning: warning),
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

    try
    {
      ThrowIfDisposed();
      ct.ThrowIfCancellationRequested();

      if (!HotkeyInputInjector.TryParse(req.Keys, out var chord, out var parseError))
      {
        return Task.FromResult(new ActionResult(
          Ok: false,
          Meta: scope.Meta(),
          MethodUsed: ActionMethod.Input,
          Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, parseError ?? "Keys is invalid.")));
      }

      HotkeyInputInjector.Send(chord);

      return Task.FromResult(new ActionResult(
        Ok: true,
        Meta: scope.Meta(),
        MethodUsed: ActionMethod.Input));
    }
    catch (OperationCanceledException)
    {
      return Task.FromResult(new ActionResult(
        Ok: false,
        Meta: scope.Meta(),
        MethodUsed: ActionMethod.Input,
        Error: PeekuErrors.Create(PeekuErrorCode.Canceled, "Operation cancelled.")));
    }
    catch (Exception ex)
    {
      return Task.FromResult(new ActionResult(
        Ok: false,
        Meta: scope.Meta(),
        MethodUsed: ActionMethod.Input,
        Error: PeekuErrors.Create(
          PeekuErrorCode.Internal,
          "Hotkey failed.",
          new { exception = ex.GetType().FullName, ex.Message, ex.HResult })));
    }
  }
}
