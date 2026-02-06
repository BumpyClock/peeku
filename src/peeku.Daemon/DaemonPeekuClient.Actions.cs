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

      var expectedValue = req.Value ?? "";
      if (!TryIsValuePatternSupported(resolved.Element, out var valuePatternSupported, out var probeError))
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(warning: resolved.Warning),
          MethodUsed: methodUsed,
          Error: probeError ?? PeekuErrors.Create(PeekuErrorCode.Internal, "ValuePattern probe failed."),
          Evidence: CreateValueActionEvidence(
            operation: "set-value",
            valuePatternSupported: false,
            expectedValue: expectedValue,
            actualValue: null,
            status: "probe_failed",
            verificationPerformed: false,
            verificationMatched: null));
      }

      if (!valuePatternSupported)
      {
        var verification = EvaluateValueVerification(
          operation: "set-value",
          valuePatternSupported: false,
          expectedValue: expectedValue,
          actualValue: null);

        return new ActionResult(
          Ok: true,
          Meta: scope.Meta(warning: CombineWarnings(resolved.Warning, verification.Warning)),
          MethodUsed: methodUsed,
          Evidence: verification.Evidence);
      }

      if (!TrySetValue(resolved.Element, expectedValue, out var setError))
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(warning: resolved.Warning),
          MethodUsed: methodUsed,
          Error: setError,
          Evidence: CreateValueActionEvidence(
            operation: "set-value",
            valuePatternSupported: true,
            expectedValue: expectedValue,
            actualValue: null,
            status: "set_failed",
            verificationPerformed: false,
            verificationMatched: null));
      }

      if (!TryReadValue(resolved.Element, out var actualValue, out var readError))
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(warning: resolved.Warning),
          MethodUsed: methodUsed,
          Error: readError,
          Evidence: CreateValueActionEvidence(
            operation: "set-value",
            valuePatternSupported: true,
            expectedValue: expectedValue,
            actualValue: null,
            status: "read_failed",
            verificationPerformed: false,
            verificationMatched: null));
      }

      var verificationResult = EvaluateValueVerification(
        operation: "set-value",
        valuePatternSupported: true,
        expectedValue: expectedValue,
        actualValue: actualValue);

      if (!verificationResult.Ok)
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(warning: resolved.Warning),
          MethodUsed: methodUsed,
          Error: verificationResult.Error,
          Evidence: verificationResult.Evidence);
      }

      return new ActionResult(
        Ok: true,
        Meta: scope.Meta(warning: resolved.Warning),
        MethodUsed: methodUsed,
        Evidence: verificationResult.Evidence);
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
      if (!TryIsValuePatternSupported(resolved.Element, out var valuePatternSupported, out var probeError))
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(warning: resolved.Warning),
          MethodUsed: methodUsed,
          Error: probeError ?? PeekuErrors.Create(PeekuErrorCode.Internal, "ValuePattern probe failed."),
          Evidence: CreateValueActionEvidence(
            operation: "type",
            valuePatternSupported: false,
            expectedValue: req.Text,
            actualValue: null,
            status: "probe_failed",
            verificationPerformed: false,
            verificationMatched: null));
      }

      if (!valuePatternSupported)
      {
        var unsupported = EvaluateValueVerification(
          operation: "type",
          valuePatternSupported: false,
          expectedValue: req.Text,
          actualValue: null);

        return new ActionResult(
          Ok: true,
          Meta: scope.Meta(warning: CombineWarnings(resolved.Warning, unsupported.Warning)),
          MethodUsed: methodUsed,
          Evidence: unsupported.Evidence);
      }

      var baseText = "";
      if (req.Append)
      {
        if (!TryReadValue(resolved.Element, out var current, out var readError))
        {
          return new ActionResult(
            Ok: false,
            Meta: scope.Meta(warning: resolved.Warning),
            MethodUsed: methodUsed,
            Error: readError,
            Evidence: CreateValueActionEvidence(
              operation: "type",
              valuePatternSupported: true,
              expectedValue: req.Text,
              actualValue: null,
              status: "read_base_failed",
              verificationPerformed: false,
              verificationMatched: null));
        }

        baseText = current ?? "";
      }

      var expectedValue = req.Append ? baseText + req.Text : req.Text;
      if (delayMs <= 0)
      {
        if (!TrySetValue(resolved.Element, expectedValue, out var setError))
        {
          return new ActionResult(
            Ok: false,
            Meta: scope.Meta(warning: resolved.Warning),
            MethodUsed: methodUsed,
            Error: setError,
            Evidence: CreateValueActionEvidence(
              operation: "type",
              valuePatternSupported: true,
              expectedValue: expectedValue,
              actualValue: null,
              status: "set_failed",
              verificationPerformed: false,
              verificationMatched: null));
        }
      }
      else
      {
        var typed = req.Append ? baseText : "";
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
              Error: setError,
              Evidence: CreateValueActionEvidence(
                operation: "type",
                valuePatternSupported: true,
                expectedValue: expectedValue,
                actualValue: typed,
                status: "set_failed",
                verificationPerformed: false,
                verificationMatched: null));
          }

          await Task.Delay(delayMs, ct).ConfigureAwait(false);
        }
      }

      if (!TryReadValue(resolved.Element, out var actualValue, out var finalReadError))
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(warning: resolved.Warning),
          MethodUsed: methodUsed,
          Error: finalReadError,
          Evidence: CreateValueActionEvidence(
            operation: "type",
            valuePatternSupported: true,
            expectedValue: expectedValue,
            actualValue: null,
            status: "read_failed",
            verificationPerformed: false,
            verificationMatched: null));
      }

      var verificationResult = EvaluateValueVerification(
        operation: "type",
        valuePatternSupported: true,
        expectedValue: expectedValue,
        actualValue: actualValue);

      if (!verificationResult.Ok)
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(warning: resolved.Warning),
          MethodUsed: methodUsed,
          Error: verificationResult.Error,
          Evidence: verificationResult.Evidence);
      }

      return new ActionResult(
        Ok: true,
        Meta: scope.Meta(warning: resolved.Warning),
        MethodUsed: methodUsed,
        Evidence: verificationResult.Evidence);
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
