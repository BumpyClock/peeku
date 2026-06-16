using FlaUI.Core.AutomationElements;
using peeku;

namespace peeku.Daemon;

public sealed partial class DaemonPeekuClient
{
  public Task<WaitResult> WaitAsync(WaitRequest req, CancellationToken ct = default)
  {
    if (req is null)
    {
      return WaitSnapshotAsync(req!, ct);
    }

    // Condition-bearing waits need live pattern state; NotExists also uses live path.
    if (UiaLiveWait.RequiresLivePath(req.Condition) ||
        (req.Selector is not null && !req.Selector.PreferCachedSnapshot))
    {
      return WaitLiveAsync(req, ct);
    }

    return WaitSnapshotAsync(req, ct);
  }

  private Task<WaitResult> WaitSnapshotAsync(WaitRequest req, CancellationToken ct)
    => UiaWait.WaitAsync(req, UiaSnapshotAsync, ct);

  private Task<WaitResult> WaitLiveAsync(WaitRequest req, CancellationToken ct)
  {
    var scope = Results.Start();
    try
    {
      ThrowIfDisposed();
      ct.ThrowIfCancellationRequested();

      if (req is null)
      {
        return Task.FromResult(new WaitResult(
          Ok: false,
          Meta: scope.Meta(),
          Found: false,
          Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Request is required.")));
      }

      if (req.Selector is null || string.IsNullOrWhiteSpace(req.Selector.Expr))
      {
        return Task.FromResult(new WaitResult(
          Ok: false,
          Meta: scope.Meta(),
          Found: false,
          Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Selector is required.")));
      }

      var timeout = req.Timeout;
      if (timeout < TimeSpan.Zero)
      {
        timeout = TimeSpan.Zero;
      }

      var poll = TimeSpan.FromMilliseconds(200);
      DateTimeOffset? deadline = timeout <= TimeSpan.Zero ? null : DateTimeOffset.UtcNow + timeout;
      var targetUsed = req.Target ?? Target.Focused();
      var warning = default(string);

      while (true)
      {
        ct.ThrowIfCancellationRequested();

        var root = ResolveRoot(targetUsed, ct, out var rootWarning);
        warning = CombineWarnings(warning, rootWarning);
        if (root is null)
        {
          return Task.FromResult(new WaitResult(
            Ok: false,
            Meta: scope.Meta(warning: warning),
            Found: false,
            Error: PeekuErrors.Create(PeekuErrorCode.WindowNotFound, "Target window not found.")));
        }

        IReadOnlyList<AutomationElement> matches;
        try
        {
          matches = UiaLiveSelectors.Select(root, req.Selector, limit: 1, ct);
        }
        catch (ArgumentException ex)
        {
          return Task.FromResult(new WaitResult(
            Ok: false,
            Meta: scope.Meta(warning: warning),
            Found: false,
            Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Invalid selector.", new { selector = req.Selector.Expr, error = ex.Message })));
        }

        // NotExists: success when element is absent.
        if (req.Condition == WaitCondition.NotExists)
        {
          if (matches.Count == 0)
          {
            return Task.FromResult(new WaitResult(
              Ok: true,
              Meta: scope.Meta(warning: warning),
              Found: true));
          }
        }
        else if (matches.Count > 0 && WaitPredicates.Evaluate(matches[0], req.Condition, req.ExpectedValue))
        {
          var refId = StoreHandle(matches[0]);
          return Task.FromResult(new WaitResult(
            Ok: true,
            Meta: scope.Meta(warning: warning),
            Found: true,
            Element: new ElementRef(refId)));
        }

        if (deadline is not null && DateTimeOffset.UtcNow >= deadline.Value)
        {
          return Task.FromResult(new WaitResult(
            Ok: true,
            Meta: scope.Meta(warning: CombineWarnings(warning, "Timed out waiting for selector.")),
            Found: false));
        }

        if (timeout <= TimeSpan.Zero)
        {
          return Task.FromResult(new WaitResult(
            Ok: true,
            Meta: scope.Meta(warning: warning),
            Found: false));
        }

        if (ct.WaitHandle.WaitOne(poll))
        {
          ct.ThrowIfCancellationRequested();
        }
      }
    }
    catch (OperationCanceledException)
    {
      return Task.FromResult(new WaitResult(
        Ok: false,
        Meta: scope.Meta(),
        Found: false,
        Error: PeekuErrors.Create(PeekuErrorCode.Canceled, "Operation cancelled.")));
    }
    catch (Exception ex)
    {
      return Task.FromResult(new WaitResult(
        Ok: false,
        Meta: scope.Meta(),
        Found: false,
        Error: PeekuErrors.Create(
          PeekuErrorCode.Internal,
          "Wait failed.",
          new { exception = ex.GetType().FullName, ex.Message, ex.HResult })));
    }
  }
}
