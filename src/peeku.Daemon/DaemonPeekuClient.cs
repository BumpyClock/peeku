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
  private readonly UIA3Automation _automation;
  private readonly HandleIdCache<AutomationElement> _handles;
  private int _disposed;

  public DaemonPeekuClient()
  {
    _automation = new UIA3Automation();
    _handles = new HandleIdCache<AutomationElement>(capacity: 2000, ttl: TimeSpan.FromMinutes(2));
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
