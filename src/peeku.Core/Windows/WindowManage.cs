using System.Text.Json;

namespace peeku;

/// <summary>
/// Shared window manipulation operations used by every IPeekuClient implementation.
/// Uses SetWindowPos/ShowWindow/PostMessage — no SendInput, no focus steal.
/// </summary>
internal static class WindowManage
{
  // ─────────────────────────────────────────────────────────────────────────────
  // Move
  // ─────────────────────────────────────────────────────────────────────────────

  internal static Task<WindowActionResult> MoveAsync(WindowMoveRequest req, CancellationToken ct)
  {
    var scope = Results.Start();
    try
    {
      ct.ThrowIfCancellationRequested();

      if (req is null)
        return Fail(scope, PeekuErrorCode.InvalidArgument, "Request is required.");

      var resolved = Win32Windows.ResolveTargetWindow(req.Target, ct);
      if (resolved is null)
        return Fail(scope, PeekuErrorCode.WindowNotFound, "Target window not found.");

      var (hwnd, _) = resolved.Value;

      // If maximized, restore first — SetWindowPos on a maximized window is a no-op for position.
      if (Win32Windows.IsWindowMaximized(hwnd))
        Win32Windows.ShowWindowManage(hwnd, Win32Windows.SW_RESTORE_PUBLIC);

      var flags = Win32Windows.SWP_NOSIZE | Win32Windows.SWP_NOZORDER | Win32Windows.SWP_NOACTIVATE;
      var ok = Win32Windows.SetWindowPos(hwnd, IntPtr.Zero, req.X, req.Y, 0, 0, flags);
      if (!ok)
        return Fail(scope, PeekuErrorCode.Internal, "SetWindowPos failed.");

      return Task.FromResult(new WindowActionResult(Ok: true, Meta: scope.Meta()));
    }
    catch (OperationCanceledException)
    {
      return Fail(scope, PeekuErrorCode.Canceled, "Operation cancelled.");
    }
    catch (Exception ex)
    {
      return Fail(scope, PeekuErrorCode.Internal, "Window move failed.", ex);
    }
  }

  // ─────────────────────────────────────────────────────────────────────────────
  // Resize
  // ─────────────────────────────────────────────────────────────────────────────

  internal static Task<WindowActionResult> ResizeAsync(WindowResizeRequest req, CancellationToken ct)
  {
    var scope = Results.Start();
    try
    {
      ct.ThrowIfCancellationRequested();

      if (req is null)
        return Fail(scope, PeekuErrorCode.InvalidArgument, "Request is required.");

      var resolved = Win32Windows.ResolveTargetWindow(req.Target, ct);
      if (resolved is null)
        return Fail(scope, PeekuErrorCode.WindowNotFound, "Target window not found.");

      var (hwnd, _) = resolved.Value;

      // If maximized, restore first so the resize lands on the restored rect.
      if (Win32Windows.IsWindowMaximized(hwnd))
        Win32Windows.ShowWindowManage(hwnd, Win32Windows.SW_RESTORE_PUBLIC);

      var flags = Win32Windows.SWP_NOMOVE | Win32Windows.SWP_NOZORDER | Win32Windows.SWP_NOACTIVATE;
      var ok = Win32Windows.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, req.Width, req.Height, flags);
      if (!ok)
        return Fail(scope, PeekuErrorCode.Internal, "SetWindowPos failed.");

      return Task.FromResult(new WindowActionResult(Ok: true, Meta: scope.Meta()));
    }
    catch (OperationCanceledException)
    {
      return Fail(scope, PeekuErrorCode.Canceled, "Operation cancelled.");
    }
    catch (Exception ex)
    {
      return Fail(scope, PeekuErrorCode.Internal, "Window resize failed.", ex);
    }
  }

  // ─────────────────────────────────────────────────────────────────────────────
  // SetBounds
  // ─────────────────────────────────────────────────────────────────────────────

  internal static Task<WindowActionResult> SetBoundsAsync(WindowBoundsRequest req, CancellationToken ct)
  {
    var scope = Results.Start();
    try
    {
      ct.ThrowIfCancellationRequested();

      if (req is null)
        return Fail(scope, PeekuErrorCode.InvalidArgument, "Request is required.");

      var resolved = Win32Windows.ResolveTargetWindow(req.Target, ct);
      if (resolved is null)
        return Fail(scope, PeekuErrorCode.WindowNotFound, "Target window not found.");

      var (hwnd, _) = resolved.Value;

      // If maximized, restore first.
      if (Win32Windows.IsWindowMaximized(hwnd))
        Win32Windows.ShowWindowManage(hwnd, Win32Windows.SW_RESTORE_PUBLIC);

      var flags = Win32Windows.SWP_NOZORDER | Win32Windows.SWP_NOACTIVATE;
      var ok = Win32Windows.SetWindowPos(hwnd, IntPtr.Zero, req.X, req.Y, req.Width, req.Height, flags);
      if (!ok)
        return Fail(scope, PeekuErrorCode.Internal, "SetWindowPos failed.");

      return Task.FromResult(new WindowActionResult(Ok: true, Meta: scope.Meta()));
    }
    catch (OperationCanceledException)
    {
      return Fail(scope, PeekuErrorCode.Canceled, "Operation cancelled.");
    }
    catch (Exception ex)
    {
      return Fail(scope, PeekuErrorCode.Internal, "Window set-bounds failed.", ex);
    }
  }

  // ─────────────────────────────────────────────────────────────────────────────
  // Minimize / Maximize / Restore
  // ─────────────────────────────────────────────────────────────────────────────

  internal static Task<WindowActionResult> MinimizeAsync(WindowStateRequest req, CancellationToken ct)
    => ShowWindowOp(req, Win32Windows.SW_MINIMIZE, "minimize", ct);

  internal static Task<WindowActionResult> MaximizeAsync(WindowStateRequest req, CancellationToken ct)
    => ShowWindowOp(req, Win32Windows.SW_MAXIMIZE, "maximize", ct);

  internal static Task<WindowActionResult> RestoreAsync(WindowStateRequest req, CancellationToken ct)
    => ShowWindowOp(req, Win32Windows.SW_RESTORE_PUBLIC, "restore", ct);

  private static Task<WindowActionResult> ShowWindowOp(WindowStateRequest req, int cmd, string opName, CancellationToken ct)
  {
    var scope = Results.Start();
    try
    {
      ct.ThrowIfCancellationRequested();

      if (req is null)
        return Fail(scope, PeekuErrorCode.InvalidArgument, "Request is required.");

      var resolved = Win32Windows.ResolveTargetWindow(req.Target, ct);
      if (resolved is null)
        return Fail(scope, PeekuErrorCode.WindowNotFound, "Target window not found.");

      var (hwnd, _) = resolved.Value;
      Win32Windows.ShowWindowManage(hwnd, cmd);

      return Task.FromResult(new WindowActionResult(Ok: true, Meta: scope.Meta()));
    }
    catch (OperationCanceledException)
    {
      return Fail(scope, PeekuErrorCode.Canceled, "Operation cancelled.");
    }
    catch (Exception ex)
    {
      return Fail(scope, PeekuErrorCode.Internal, $"Window {opName} failed.", ex);
    }
  }

  // ─────────────────────────────────────────────────────────────────────────────
  // Close (PostMessage WM_CLOSE, poll IsWindow until gone or timeout)
  // ─────────────────────────────────────────────────────────────────────────────

  internal static async Task<WindowActionResult> CloseAsync(WindowCloseRequest req, CancellationToken ct)
  {
    var scope = Results.Start();
    try
    {
      ct.ThrowIfCancellationRequested();

      if (req is null)
        return MakeFailResult(scope, PeekuErrorCode.InvalidArgument, "Request is required.");

      var resolved = Win32Windows.ResolveTargetWindow(req.Target, ct);
      if (resolved is null)
        return MakeFailResult(scope, PeekuErrorCode.WindowNotFound, "Target window not found.");

      var (hwnd, _) = resolved.Value;

      // PostMessage is graceful (not SendMessage, not TerminateProcess).
      Win32Windows.PostMessage(hwnd, Win32Windows.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

      // Poll IsWindow until the hwnd is gone or waitMs elapses.
      var waitMs = Math.Max(0, req.WaitMs);
      var deadline = DateTimeOffset.UtcNow.AddMilliseconds(waitMs);
      var closed = false;

      while (DateTimeOffset.UtcNow < deadline)
      {
        ct.ThrowIfCancellationRequested();
        if (!Win32Windows.IsWindow(hwnd))
        {
          closed = true;
          break;
        }

        await Task.Delay(50, ct).ConfigureAwait(false);
      }

      // Final check after loop (handles waitMs=0 case).
      if (!closed)
        closed = !Win32Windows.IsWindow(hwnd);

      var evidence = JsonSerializer.SerializeToElement(new { closed });
      return new WindowActionResult(Ok: true, Meta: scope.Meta(), Evidence: evidence);
    }
    catch (OperationCanceledException)
    {
      return MakeFailResult(scope, PeekuErrorCode.Canceled, "Operation cancelled.");
    }
    catch (Exception ex)
    {
      return MakeFailResult(scope, PeekuErrorCode.Internal, "Window close failed.", ex);
    }
  }

  // ─────────────────────────────────────────────────────────────────────────────
  // Helpers
  // ─────────────────────────────────────────────────────────────────────────────

  private static Task<WindowActionResult> Fail(ResultScope scope, PeekuErrorCode code, string msg, Exception? ex = null)
    => Task.FromResult(MakeFailResult(scope, code, msg, ex));

  private static WindowActionResult MakeFailResult(ResultScope scope, PeekuErrorCode code, string msg, Exception? ex = null)
  {
    object? details = ex is null ? null : new { exception = ex.GetType().FullName, ex.Message, ex.HResult };
    return new WindowActionResult(
      Ok: false,
      Meta: scope.Meta(),
      Error: PeekuErrors.Create(code, msg, details));
  }
}
