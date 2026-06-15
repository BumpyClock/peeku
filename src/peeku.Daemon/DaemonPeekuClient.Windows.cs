using peeku;

namespace peeku.Daemon;

public sealed partial class DaemonPeekuClient
{
  public Task<WindowListResult> WindowsListAsync(WindowsListRequest req, CancellationToken ct = default)
  {
    var scope = Results.Start();
    try
    {
      ThrowIfDisposed();
      var windows = Win32Windows.ListWindows(req, ct);
      return Task.FromResult(new WindowListResult(
        Ok: true,
        Meta: scope.Meta(),
        Windows: windows));
    }
    catch (OperationCanceledException)
    {
      return Task.FromResult(new WindowListResult(
        Ok: false,
        Meta: scope.Meta(),
        Windows: Array.Empty<WindowInfo>(),
        Error: PeekuErrors.Create(PeekuErrorCode.Canceled, "Operation cancelled.")));
    }
    catch (Exception ex)
    {
      return Task.FromResult(new WindowListResult(
        Ok: false,
        Meta: scope.Meta(),
        Windows: Array.Empty<WindowInfo>(),
        Error: PeekuErrors.Create(
          PeekuErrorCode.Internal,
          "Windows list failed.",
          new { exception = ex.GetType().FullName, ex.Message, ex.HResult })));
    }
  }

  public Task<FocusedWindowResult> WindowsFocusedAsync(CancellationToken ct = default)
  {
    var scope = Results.Start();
    try
    {
      ThrowIfDisposed();
      ct.ThrowIfCancellationRequested();
      var window = Win32Windows.GetFocusedWindow();
      return Task.FromResult(new FocusedWindowResult(
        Ok: true,
        Meta: scope.Meta(warning: window is null ? "No foreground window." : null),
        Window: window));
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
          "Windows focused failed.",
          new { exception = ex.GetType().FullName, ex.Message, ex.HResult })));
    }
  }

  public Task<FocusedWindowResult> WindowFocusAsync(WindowFocusRequest req, CancellationToken ct = default)
  {
    ThrowIfDisposed();
    return WindowFocus.WindowFocusAsync(req, ct);
  }

  public Task<CaptureImageResult> CaptureImageAsync(CaptureImageRequest req, CancellationToken ct = default)
    => CaptureImage.CaptureImageAsync(req, ct);
}
