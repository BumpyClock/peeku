namespace peeku;

internal static class UiaWait
{
  private static readonly TimeSpan DefaultPoll = TimeSpan.FromMilliseconds(200);

  internal static Task<WaitResult> WaitAsync(
    WaitRequest req,
    Func<UiaSnapshotRequest, CancellationToken, Task<UiaSnapshotResult>> snapshotAsync,
    CancellationToken ct)
    => WaitAsync(req, snapshotAsync, DefaultPoll, static (d, c) => Task.Delay(d, c), ct);

  internal static async Task<WaitResult> WaitAsync(
    WaitRequest req,
    Func<UiaSnapshotRequest, CancellationToken, Task<UiaSnapshotResult>> snapshotAsync,
    TimeSpan pollInterval,
    Func<TimeSpan, CancellationToken, Task> delayAsync,
    CancellationToken ct)
  {
    var scope = Results.Start();
    try
    {
      ct.ThrowIfCancellationRequested();

      if (req is null)
      {
        return new WaitResult(
          Ok: false,
          Meta: scope.Meta(),
          Found: false,
          Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Request is required."));
      }

      if (req.Selector is null || string.IsNullOrWhiteSpace(req.Selector.Expr))
      {
        return new WaitResult(
          Ok: false,
          Meta: scope.Meta(),
          Found: false,
          Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Selector is required."));
      }

      if (snapshotAsync is null)
      {
        return new WaitResult(
          Ok: false,
          Meta: scope.Meta(),
          Found: false,
          Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Snapshot function is required."));
      }

      var timeout = req.Timeout;
      if (timeout < TimeSpan.Zero)
      {
        timeout = TimeSpan.Zero;
      }

      var poll = pollInterval <= TimeSpan.Zero ? DefaultPoll : pollInterval;
      var maxAttempts = timeout <= TimeSpan.Zero || poll <= TimeSpan.Zero
        ? 1L
        : (timeout.Ticks / poll.Ticks) + 1L;

      if (maxAttempts < 1)
      {
        maxAttempts = 1;
      }

      string? warning = null;
      var snapshotReq = new UiaSnapshotRequest(
        Target: req.Target,
        Depth: 6,
        MaxNodes: 5000,
        IncludeProperties: UiaPropertiesMode.Basic);

      for (var attempt = 0L; attempt < maxAttempts; attempt++)
      {
        ct.ThrowIfCancellationRequested();

        var snapshot = await snapshotAsync(snapshotReq, ct).ConfigureAwait(false);
        warning = snapshot.Meta.Warning;

        if (!snapshot.Ok)
        {
          return new WaitResult(
            Ok: false,
            Meta: scope.Meta(warning: warning),
            Found: false,
            Error: snapshot.Error ?? PeekuErrors.Create(PeekuErrorCode.Internal, "UIA snapshot failed."));
        }

        IReadOnlyList<ElementRef> matches;
        try
        {
          matches = UiaSelectors.Select(snapshot, req.Selector, limit: 1);
        }
        catch (ArgumentException ex)
        {
          return new WaitResult(
            Ok: false,
            Meta: scope.Meta(warning: warning),
            Found: false,
            Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Invalid selector.", new { selector = req.Selector.Expr, error = ex.Message }));
        }

        if (matches.Count > 0)
        {
          return new WaitResult(
            Ok: true,
            Meta: scope.Meta(warning: warning),
            Found: true,
            Element: matches[0]);
        }

        if (attempt < maxAttempts - 1)
        {
          await delayAsync(poll, ct).ConfigureAwait(false);
        }
      }

      var timeoutWarning = timeout > TimeSpan.Zero ? "Timed out waiting for selector." : null;
      return new WaitResult(
        Ok: true,
        Meta: scope.Meta(warning: CombineWarnings(warning, timeoutWarning)),
        Found: false);
    }
    catch (OperationCanceledException)
    {
      return new WaitResult(
        Ok: false,
        Meta: scope.Meta(),
        Found: false,
        Error: PeekuErrors.Create(PeekuErrorCode.Canceled, "Operation cancelled."));
    }
    catch (Exception ex)
    {
      return new WaitResult(
        Ok: false,
        Meta: scope.Meta(),
        Found: false,
        Error: PeekuErrors.Create(
          PeekuErrorCode.Internal,
          "Wait failed.",
          new { exception = ex.GetType().FullName, ex.Message, ex.HResult }));
    }
  }

  private static string? CombineWarnings(string? a, string? b)
  {
    if (string.IsNullOrWhiteSpace(a))
    {
      return string.IsNullOrWhiteSpace(b) ? null : b;
    }

    if (string.IsNullOrWhiteSpace(b))
    {
      return a;
    }

    return $"{a} {b}";
  }
}

