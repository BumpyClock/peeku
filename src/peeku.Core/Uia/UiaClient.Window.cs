namespace peeku;

public sealed partial class UiaClient
{
  // Window management (S3): UiaClient delegates to the shared WindowManage static,
  // same as WindowsClient. UiaClient implements IPeekuClient as a full surface.

  public Task<WindowActionResult> WindowMoveAsync(WindowMoveRequest req, CancellationToken ct = default)
    => WindowManage.MoveAsync(req, ct);

  public Task<WindowActionResult> WindowResizeAsync(WindowResizeRequest req, CancellationToken ct = default)
    => WindowManage.ResizeAsync(req, ct);

  public Task<WindowActionResult> WindowSetBoundsAsync(WindowBoundsRequest req, CancellationToken ct = default)
    => WindowManage.SetBoundsAsync(req, ct);

  public Task<WindowActionResult> WindowMinimizeAsync(WindowStateRequest req, CancellationToken ct = default)
    => WindowManage.MinimizeAsync(req, ct);

  public Task<WindowActionResult> WindowMaximizeAsync(WindowStateRequest req, CancellationToken ct = default)
    => WindowManage.MaximizeAsync(req, ct);

  public Task<WindowActionResult> WindowRestoreAsync(WindowStateRequest req, CancellationToken ct = default)
    => WindowManage.RestoreAsync(req, ct);

  public Task<WindowActionResult> WindowCloseAsync(WindowCloseRequest req, CancellationToken ct = default)
    => WindowManage.CloseAsync(req, ct);
}
