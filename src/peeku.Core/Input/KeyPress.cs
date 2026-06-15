namespace peeku;

/// <summary>
/// Shared synthetic-keypress operation used by every IPeekuClient implementation (in-proc + daemon).
/// Presses one or more NAMED keys in sequence (enter, tab, esc, arrows, f1-f12, a-z, 0-9, …), distinct
/// from <c>hotkey</c> which sends a modifier chord. Keyboard input via SendInput is global/foreground,
/// so when a target window is provided it is brought to the foreground first (the hardened activation
/// path) so the keys land there; otherwise they go to the current foreground window.
/// Reuses <see cref="HotkeyInputInjector"/>'s VK table for resolution (no duplicate key mapping).
/// </summary>
internal static class KeyPress
{
  internal static async Task<ActionResult> PressAsync(PressRequest req, CancellationToken ct = default)
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
          MethodUsed: ActionMethod.Input,
          Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Request is required."));
      }

      if (req.Keys is null || req.Keys.Count == 0)
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(),
          MethodUsed: ActionMethod.Input,
          Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "At least one key is required."));
      }

      if (req.Count < 1)
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(),
          MethodUsed: ActionMethod.Input,
          Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Count must be >= 1."));
      }

      if (req.DelayMs is < 0)
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(),
          MethodUsed: ActionMethod.Input,
          Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "DelayMs must be >= 0."));
      }

      if (req.HoldMs is < 0)
      {
        return new ActionResult(
          Ok: false,
          Meta: scope.Meta(),
          MethodUsed: ActionMethod.Input,
          Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "HoldMs must be >= 0."));
      }

      // Resolve every key up-front so an unknown name fails before any keystroke is sent (atomic).
      var resolved = new HotkeyInputInjector.KeySpec[req.Keys.Count];
      for (var i = 0; i < req.Keys.Count; i++)
      {
        if (!HotkeyInputInjector.TryResolveKey(req.Keys[i], out var key, out var keyError))
        {
          return new ActionResult(
            Ok: false,
            Meta: scope.Meta(),
            MethodUsed: ActionMethod.Input,
            Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, keyError ?? $"Unknown key name: '{req.Keys[i]}'."));
        }

        resolved[i] = key;
      }

      // Keyboard input is global; if a concrete target window is given, focus it first so keys land
      // there. A null target (or desktop/screen) sends to whatever currently has focus.
      string? warning = null;
      if (req.Target is not null and not Target.Desktop and not Target.Screen)
      {
        var window = Win32Windows.ResolveTargetWindow(req.Target, ct);
        if (window is null)
        {
          return new ActionResult(
            Ok: false,
            Meta: scope.Meta(),
            MethodUsed: ActionMethod.Input,
            Error: PeekuErrors.Create(PeekuErrorCode.WindowNotFound, "Target window not found."));
        }

        if (!Win32Windows.BringToForegroundReliable(window.Value.Hwnd))
        {
          warning = "Target window could not be brought to foreground; keys sent to current focus.";
        }
        else
        {
          // Brief settle so focus is inside the window before keystrokes land.
          await Task.Delay(50, ct).ConfigureAwait(false);
        }
      }

      var delayMs = req.DelayMs.GetValueOrDefault(0);
      var holdMs = req.HoldMs.GetValueOrDefault(0);

      for (var rep = 0; rep < req.Count; rep++)
      {
        for (var i = 0; i < resolved.Length; i++)
        {
          ct.ThrowIfCancellationRequested();

          await HotkeyInputInjector.SendKeyAsync(resolved[i], holdMs, ct).ConfigureAwait(false);

          if (delayMs > 0)
          {
            await Task.Delay(delayMs, ct).ConfigureAwait(false);
          }
        }
      }

      return new ActionResult(
        Ok: true,
        Meta: scope.Meta(warning: warning),
        MethodUsed: ActionMethod.Input);
    }
    catch (OperationCanceledException)
    {
      return new ActionResult(
        Ok: false,
        Meta: scope.Meta(),
        MethodUsed: ActionMethod.Input,
        Error: PeekuErrors.Create(PeekuErrorCode.Canceled, "Operation cancelled."));
    }
    catch (Exception ex)
    {
      return new ActionResult(
        Ok: false,
        Meta: scope.Meta(),
        MethodUsed: ActionMethod.Input,
        Error: PeekuErrors.Create(
          PeekuErrorCode.Internal,
          "Press failed.",
          new { exception = ex.GetType().FullName, ex.Message, ex.HResult }));
    }
  }
}
