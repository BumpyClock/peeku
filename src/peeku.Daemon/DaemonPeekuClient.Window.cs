using peeku;

namespace peeku.Daemon;

public sealed partial class DaemonPeekuClient
{
  public Task<WindowActionResult> WindowMoveAsync(WindowMoveRequest req, CancellationToken ct = default)
  {
    ThrowIfDisposed();
    return WindowManage.MoveAsync(req, ct);
  }

  public Task<WindowActionResult> WindowResizeAsync(WindowResizeRequest req, CancellationToken ct = default)
  {
    ThrowIfDisposed();
    return WindowManage.ResizeAsync(req, ct);
  }

  public Task<WindowActionResult> WindowSetBoundsAsync(WindowBoundsRequest req, CancellationToken ct = default)
  {
    ThrowIfDisposed();
    return WindowManage.SetBoundsAsync(req, ct);
  }

  public Task<WindowActionResult> WindowMinimizeAsync(WindowStateRequest req, CancellationToken ct = default)
  {
    ThrowIfDisposed();
    return WindowManage.MinimizeAsync(req, ct);
  }

  public Task<WindowActionResult> WindowMaximizeAsync(WindowStateRequest req, CancellationToken ct = default)
  {
    ThrowIfDisposed();
    return WindowManage.MaximizeAsync(req, ct);
  }

  public Task<WindowActionResult> WindowRestoreAsync(WindowStateRequest req, CancellationToken ct = default)
  {
    ThrowIfDisposed();
    return WindowManage.RestoreAsync(req, ct);
  }

  public Task<WindowActionResult> WindowCloseAsync(WindowCloseRequest req, CancellationToken ct = default)
  {
    ThrowIfDisposed();
    return WindowManage.CloseAsync(req, ct);
  }
}
