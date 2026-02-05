using System.Threading;

namespace peeku.Daemon;

/// <summary>
/// Tracks daemon shutdown requests shared across components.
/// </summary>
/// <example>
/// <code>
/// var shutdown = new DaemonShutdown();
/// shutdown.Request();
/// var requested = shutdown.IsRequested;
/// </code>
/// </example>
public sealed class DaemonShutdown
{
  private int _requested;

  public bool IsRequested => Interlocked.CompareExchange(ref _requested, 0, 0) == 1;

  public void Request() => Interlocked.Exchange(ref _requested, 1);
}
