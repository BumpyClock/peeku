namespace peeku;

/// <summary>
/// Shared window-focus operation used by every IPeekuClient implementation (in-proc + daemon).
/// Resolves the target to a concrete top-level window and brings it to the foreground using the
/// hardened activation path (the AttachThreadInput/AllowSetForegroundWindow unlock dance), because a
/// bare SetForegroundWindow silently fails when the caller isn't the foreground process. Returns the
/// focused window's info on success.
/// </summary>
internal static class WindowFocus
{
  internal static Task<FocusedWindowResult> WindowFocusAsync(WindowFocusRequest req, CancellationToken ct = default)
  {
    var scope = Results.Start();
    try
    {
      ct.ThrowIfCancellationRequested();

      if (req is null || req.Target is null)
      {
        return Task.FromResult(new FocusedWindowResult(
          Ok: false,
          Meta: scope.Meta(),
          Window: null,
          Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Target is required.")));
      }

      var resolved = Win32Windows.ResolveTargetWindow(req.Target, ct);
      if (resolved is null)
      {
        return Task.FromResult(new FocusedWindowResult(
          Ok: false,
          Meta: scope.Meta(),
          Window: null,
          Error: PeekuErrors.Create(PeekuErrorCode.WindowNotFound, "Target window not found.")));
      }

      var (hwnd, info) = resolved.Value;
      var ok = Win32Windows.BringToForegroundReliable(hwnd);

      return Task.FromResult(new FocusedWindowResult(
        Ok: ok,
        Meta: scope.Meta(warning: ok ? null : "SetForegroundWindow did not take effect (window may be blocked or off-screen)."),
        Window: info,
        Error: ok ? null : PeekuErrors.Create(PeekuErrorCode.Internal, "Failed to bring window to foreground.")));
    }
    catch (OperationCanceledException)
    {
      return Task.FromResult(new FocusedWindowResult(
        Ok: false,
        Meta: scope.Meta(),
        Window: null,
        Error: PeekuErrors.Create(PeekuErrorCode.Canceled, "Operation cancelled.")));
    }
    catch (Exception ex)
    {
      return Task.FromResult(new FocusedWindowResult(
        Ok: false,
        Meta: scope.Meta(),
        Window: null,
        Error: PeekuErrors.Create(
          PeekuErrorCode.Internal,
          "Window focus failed.",
          new { exception = ex.GetType().FullName, ex.Message, ex.HResult })));
    }
  }
}
