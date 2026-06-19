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

      var methodUsed = ActionMethodRouter.Route(req.Method, uiaSupported: true, inputSupported: true, out var routeError);
      if (routeError is not null)
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(),
          MethodUsed: methodUsed,
          Error: routeError);
      }

      // Coords path: explicit X/Y given → skip element resolution entirely (canvas escape hatch).
      var hasCoords = req.X.HasValue && req.Y.HasValue;
      if (hasCoords)
      {
        // Validate: coords + selector both given is ambiguous.
        if (req.Element is not null || req.Selector is not null)
        {
          return new ActionResult(
            Ok: false,
            Meta: scope.Meta(),
            MethodUsed: methodUsed,
            Error: PeekuErrors.Create(
              PeekuErrorCode.InvalidArgument,
              "Provide coordinates (--x/--y) OR an element/selector, not both."));
        }

        // Resolve target window hwnd.
        var coordTarget = req.Target;
        var coordWindow = Win32Windows.ResolveTargetWindow(coordTarget ?? new Target.FocusedWindow(), ct);
        var coordHwnd = coordWindow?.Hwnd ?? IntPtr.Zero;

        int screenX, screenY;
        if (req.GlobalCoords)
        {
          // Already screen-absolute physical pixels.
          screenX = req.X!.Value;
          screenY = req.Y!.Value;
        }
        else
        {
          // Window-relative: minimized guard first.
          if (coordHwnd != IntPtr.Zero && Win32Windows.IsWindowMinimized(coordHwnd))
          {
            return new ActionResult(
              Ok: false,
              Meta: scope.Meta(),
              MethodUsed: methodUsed,
              Error: PeekuErrors.Create(
                PeekuErrorCode.InvalidArgument,
                "target window is minimized; restore it before window-relative coordinate click, or use --globalCoords"));
          }

          if (coordHwnd == IntPtr.Zero || !Win32Windows.GetWindowRect(coordHwnd, out var winRect))
          {
            return new ActionResult(
              Ok: false,
              Meta: scope.Meta(),
              MethodUsed: methodUsed,
              Error: PeekuErrors.Create(PeekuErrorCode.WindowNotFound, "Target window not found or GetWindowRect failed."));
          }

          screenX = winRect.Left + req.X!.Value;
          screenY = winRect.Top  + req.Y!.Value;
        }

        var (coordClickOk, coordClickError, coordClickEvidence) = await SyntheticPointer.ClickAtPointAsync(
          screenX, screenY, coordHwnd, req.Right, req.Double, ct).ConfigureAwait(false);

        if (!coordClickOk)
        {
          return new ActionResult(
            Ok: false,
            Meta: scope.Meta(),
            MethodUsed: ActionMethod.Input,
            Error: coordClickError);
        }

        var coordEvidenceJson = coordClickEvidence is not null
          ? System.Text.Json.JsonSerializer.SerializeToElement(coordClickEvidence)
          : (System.Text.Json.JsonElement?)null;

        return new ActionResult(
          Ok: true,
          Meta: scope.Meta(),
          MethodUsed: ActionMethod.Input,
          Evidence: coordEvidenceJson);
      }

      var resolved = await ActionSelectionResolver.ResolveAsync(UiaSnapshotAsync, ResolveRootWithWarning, req.Element, req.Selector, req.Target, ct).ConfigureAwait(false);
      if (!resolved.Ok || resolved.Selection is null)
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(warning: resolved.Warning),
          MethodUsed: methodUsed,
          Error: resolved.Error ?? PeekuErrors.Create(PeekuErrorCode.Internal, "Selection resolution failed."));
      }

      if (methodUsed == ActionMethod.Input || req.Foreground)
      {
        // Resolve the target window hwnd for focus capture.
        var inputTarget = resolved.Selection.Target;
        var targetWindow = Win32Windows.ResolveTargetWindow(inputTarget, ct);
        var targetHwnd = targetWindow?.Hwnd ?? IntPtr.Zero;

        // We need the element's rect — get it via a fresh UIA walk.
        using var inputAutomation = new UIA3Automation();
        var inputRoot = ResolveRoot(resolved.Selection.Target, inputAutomation, ct, out var inputRootWarning);
        if (inputRoot is null)
        {
          return new ActionResult(
            Ok: false,
            Meta: scope.Meta(warning: CombineWarnings(resolved.Warning, inputRootWarning)),
            MethodUsed: methodUsed,
            Error: PeekuErrors.Create(PeekuErrorCode.WindowNotFound, "Target window not found."));
        }

        var inputElement = FindByRefId(inputRoot, resolved.Selection.Element.RefId, maxNodes: 20_000, ct);
        if (inputElement is null)
        {
          return new ActionResult(
            Ok: false,
            Meta: scope.Meta(warning: CombineWarnings(resolved.Warning, inputRootWarning)),
            MethodUsed: methodUsed,
            Error: PeekuErrors.Create(
              PeekuErrorCode.ElementNotFound,
              "Element not found for click.",
              new { refId = resolved.Selection.Element.RefId, snapshotId = resolved.Selection.SnapshotId }));
        }

        var uiaRect = inputElement.BoundingRectangle;
        var rect = new Rect(uiaRect.X, uiaRect.Y, uiaRect.Width, uiaRect.Height);

        var (clickOk, clickError, clickEvidence) = await SyntheticPointer.ClickAsync(rect, targetHwnd, req.Right, req.Double, ct).ConfigureAwait(false);
        if (!clickOk)
        {
          return new ActionResult(
            Ok: false,
            Meta: scope.Meta(warning: CombineWarnings(resolved.Warning, inputRootWarning)),
            MethodUsed: methodUsed,
            Error: clickError);
        }

        var evidenceJson = clickEvidence is not null
          ? System.Text.Json.JsonSerializer.SerializeToElement(clickEvidence)
          : (System.Text.Json.JsonElement?)null;

        return new ActionResult(
          Ok: true,
          Meta: scope.Meta(warning: CombineWarnings(resolved.Warning, inputRootWarning)),
          MethodUsed: methodUsed,
          Evidence: evidenceJson);
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

      if (!TryClickViaUiaPatterns(element, out var uiaClickError))
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(warning: CombineWarnings(resolved.Warning, rootWarning)),
          MethodUsed: methodUsed,
          Error: uiaClickError);
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

      var resolved = await ActionSelectionResolver.ResolveAsync(UiaSnapshotAsync, ResolveRootWithWarning, req.Element, req.Selector, req.Target, ct).ConfigureAwait(false);
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

      var resolved = await ActionSelectionResolver.ResolveAsync(UiaSnapshotAsync, ResolveRootWithWarning, req.Element, req.Selector, req.Target, ct).ConfigureAwait(false);
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

      methodUsed = ActionMethodRouter.Route(req.Method, uiaSupported: true, inputSupported: true, out var routeError);
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

      var resolved = await ActionSelectionResolver.ResolveAsync(UiaSnapshotAsync, ResolveRootWithWarning, req.Element, req.Selector, req.Target, ct).ConfigureAwait(false);
      if (!resolved.Ok || resolved.Selection is null)
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(warning: resolved.Warning),
          MethodUsed: methodUsed,
          Error: resolved.Error ?? PeekuErrors.Create(PeekuErrorCode.Internal, "Selection resolution failed."));
      }

      if (methodUsed == ActionMethod.Input || req.Foreground)
      {
        // Resolve the target window hwnd for focus capture.
        var inputTarget = resolved.Selection.Target;
        var targetWindow = Win32Windows.ResolveTargetWindow(inputTarget, ct);
        var targetHwnd = targetWindow?.Hwnd ?? IntPtr.Zero;

        // Get the element rect via a fresh UIA walk (same pattern as click Input branch).
        using var inputAutomation = new UIA3Automation();
        var inputRoot = ResolveRoot(resolved.Selection.Target, inputAutomation, ct, out var inputRootWarning);
        if (inputRoot is null)
        {
          return new ActionResult(
            Ok: false,
            Meta: scope.Meta(warning: CombineWarnings(resolved.Warning, inputRootWarning)),
            MethodUsed: methodUsed,
            Error: PeekuErrors.Create(PeekuErrorCode.WindowNotFound, "Target window not found."));
        }

        var inputElement = FindByRefId(inputRoot, resolved.Selection.Element.RefId, maxNodes: 20_000, ct);
        Rect? rect = null;
        if (inputElement is not null)
        {
          var uiaRect = inputElement.BoundingRectangle;
          rect = new Rect(uiaRect.X, uiaRect.Y, uiaRect.Width, uiaRect.Height);
        }

        // Warn when Append=false — synthetic keystrokes insert at caret and cannot replace.
        var typeWarning = !req.Append
          ? "Input path inserts at the caret and does not honor Append=false (replace). Use the Uia path for replace semantics."
          : null;

        var (typeOk, typeError, typeEvidence) = await SyntheticText.TypeUnicodeAsync(
          targetHwnd, rect, req.Text, req.DelayMs.GetValueOrDefault(0), ct).ConfigureAwait(false);
        if (!typeOk)
        {
          return new ActionResult(
            Ok: false,
            Meta: scope.Meta(warning: CombineWarnings(resolved.Warning, CombineWarnings(inputRootWarning, typeWarning))),
            MethodUsed: methodUsed,
            Error: typeError);
        }

        var evidenceJson = typeEvidence is not null
          ? System.Text.Json.JsonSerializer.SerializeToElement(typeEvidence)
          : (System.Text.Json.JsonElement?)null;

        return new ActionResult(
          Ok: true,
          Meta: scope.Meta(warning: CombineWarnings(resolved.Warning, CombineWarnings(inputRootWarning, typeWarning))),
          MethodUsed: methodUsed,
          Evidence: evidenceJson);
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

      var resolved = await ActionSelectionResolver.ResolveAsync(UiaSnapshotAsync, ResolveRootWithWarning, req.Element, req.Selector, req.Target, ct).ConfigureAwait(false);
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

  public Task<ActionResult> PressAsync(PressRequest req, CancellationToken ct = default)
    => KeyPress.PressAsync(req, ct);

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
