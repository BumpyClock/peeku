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

  public Task<AppRelaunchResult> AppRelaunchAsync(AppRelaunchRequest req, CancellationToken ct = default)
  {
    ThrowIfDisposed();
    return AppLifecycle.RelaunchAsync(req, ct);
  }

  public Task<AppListResult> AppListAsync(AppListRequest req, CancellationToken ct = default)
  {
    ThrowIfDisposed();
    return AppLifecycle.ListAsync(req, ct);
  }
}
