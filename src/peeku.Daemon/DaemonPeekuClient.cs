using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using peeku;

namespace peeku.Daemon;

/// <summary>
/// Daemon-scoped client with UIA automation reuse and handle fast-paths.
/// </summary>
/// <example>
/// <code>
/// using var client = new DaemonPeekuClient();
/// var result = await client.WindowsFocusedAsync();
/// </code>
/// </example>
public sealed partial class DaemonPeekuClient : IPeekuClient, IDisposable
{
  // Largest MaxNodes any single snapshot caps at (see Snapshot.cs / Helpers.cs / FindGet.cs callers).
  // The handle cache MUST hold at least this many entries: a single UiaSnapshotAsync stores one
  // handle per node, so a capacity below this evicts the snapshot's own earliest nodes (root + early
  // children) DURING the same snapshot, making a follow-up action miss immediately. Tie the cache
  // capacity to this so the two cannot drift apart.
  internal const int SnapshotMaxNodes = 5000;

  // Cache capacity = snapshot ceiling + headroom for cross-snapshot reuse within the warm daemon.
  internal const int HandleCacheCapacity = 8000;

  // Observe->act loops can span minutes across separate CLI calls; never 0 (= always expired).
  internal static readonly TimeSpan HandleCacheTtl = TimeSpan.FromMinutes(5);

  static DaemonPeekuClient()
  {
    // Loud invariant: the cache must never be smaller than the snapshot ceiling, or a single
    // snapshot evicts its own nodes. Fails fast at type load if a future edit lets them drift.
    if (HandleCacheCapacity < SnapshotMaxNodes)
    {
      throw new InvalidOperationException("HandleCacheCapacity must be >= SnapshotMaxNodes so a single snapshot cannot evict its own nodes.");
    }
  }

  private readonly UIA3Automation _automation;
  private readonly HandleIdCache<AutomationElement> _handles;
  private int _disposed;

  public DaemonPeekuClient()
    : this(HandleCacheCapacity, HandleCacheTtl)
  {
  }

  // Test-only: lets a test force a tiny capacity / zero TTL to exercise the durable-id re-walk
  // fallback after the handle cache evicts a snapshot's elements.
  internal DaemonPeekuClient(int handleCacheCapacity, TimeSpan handleCacheTtl)
  {
    _automation = new UIA3Automation();
    _handles = new HandleIdCache<AutomationElement>(capacity: handleCacheCapacity, ttl: handleCacheTtl);
  }

  public Task<DoctorResult> DoctorAsync(DoctorRequest req, CancellationToken ct = default)
    => throw new NotImplementedException();

  public void Dispose()
  {
    if (Interlocked.Exchange(ref _disposed, 1) == 1)
    {
      return;
    }

    _automation.Dispose();
  }

  private void ThrowIfDisposed()
  {
    if (Volatile.Read(ref _disposed) == 1)
    {
      throw new ObjectDisposedException(nameof(DaemonPeekuClient), "Daemon client is disposed.");
    }
  }
}
