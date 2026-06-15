using peeku;

namespace peeku.Daemon;

public sealed partial class DaemonPeekuClient
{
  public Task<AppLaunchResult> AppLaunchAsync(AppLaunchRequest req, CancellationToken ct = default)
  {
    ThrowIfDisposed();
    return AppLifecycle.LaunchAsync(req, ct);
  }

  public Task<AppQuitResult> AppQuitAsync(AppQuitRequest req, CancellationToken ct = default)
  {
    ThrowIfDisposed();
    return AppLifecycle.QuitAsync(req, ct);
  }
}
