using peeku;

namespace peeku.Daemon;

public sealed partial class DaemonPeekuClient
{
  public IAsyncEnumerable<ObservationEvent> ObserveAsync(ObserveRequest req, CancellationToken ct = default)
  {
    ThrowIfDisposed();
    return UiaObserve.ObserveAsync(req, ct);
  }

  public Task<BatchResult> BatchAsync(BatchRequest req, CancellationToken ct = default)
  {
    ThrowIfDisposed();
    return BatchRunner.RunAsync(this, req, ct);
  }
}
