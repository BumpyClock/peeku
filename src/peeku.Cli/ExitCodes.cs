using peeku;

namespace peeku.Cli;

/// <summary>
/// Wraps a timeout-linked <see cref="CancellationTokenSource"/> and exposes whether the
/// wall-clock deadline (CancelAfter) fired rather than the caller's invocation token.
/// Use <see cref="Create"/> at every CLI call site that needs a per-call deadline.
/// </summary>
internal sealed class TimeoutScope : IDisposable
{
  private readonly CancellationTokenSource _cts;
  private readonly CancellationToken _invocationToken;

  private TimeoutScope(CancellationTokenSource cts, CancellationToken invocationToken)
  {
    _cts = cts;
    _invocationToken = invocationToken;
  }

  /// <summary>Token to pass to the client call.</summary>
  internal CancellationToken Token => _cts.Token;

  /// <summary>
  /// True when our CancelAfter deadline fired but the caller's invocation token did not.
  /// Distinguishes wall-clock timeout from Ctrl-C.
  /// </summary>
  internal bool DeadlineElapsed => _cts.IsCancellationRequested && !_invocationToken.IsCancellationRequested;

  /// <summary>
  /// Creates a linked CTS with an optional CancelAfter deadline.
  /// </summary>
  internal static TimeoutScope Create(TimeSpan timeout, CancellationToken ct)
  {
    var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    if (timeout > TimeSpan.Zero)
    {
      cts.CancelAfter(timeout);
    }

    return new TimeoutScope(cts, ct);
  }

  public void Dispose() => _cts.Dispose();
}

/// <summary>
/// Maps a <see cref="PeekuError"/> to a stable process exit code.
/// Keyed off the STRING <c>error.Code</c> (never <c>(int)PeekuErrorCode</c> — the enum int
/// values deliberately differ from the assigned exit codes; see cli-refinement-plan.md §5).
/// </summary>
internal static class ExitCodes
{
  internal const int Success = 0;
  internal const int GenericFailure = 1;
  internal const int Usage = 2;
  internal const int NotFound = 3;
  internal const int Timeout = 4;
  internal const int Unavailable = 5;
  internal const int PermissionDenied = 6;
  internal const int NotSupported = 7;
  internal const int Canceled = 8;

  /// <summary>
  /// Maps an error to its exit code. Null error or an unknown code maps to <see cref="GenericFailure"/>.
  /// </summary>
  internal static int For(PeekuError? error) => For(error, deadlineElapsed: false);

  /// <summary>
  /// Maps an error to its exit code, distinguishing wall-clock timeout from Ctrl-C cancellation.
  /// When <paramref name="deadlineElapsed"/> is true and <c>error.Code == "Canceled"</c>,
  /// returns <see cref="Timeout"/> (4) rather than <see cref="Canceled"/> (8).
  /// </summary>
  internal static int For(PeekuError? error, bool deadlineElapsed)
  {
    if (error is null)
    {
      return GenericFailure;
    }

    return error.Code switch
    {
      "Unknown" => GenericFailure,
      "Internal" => GenericFailure,
      "InvalidArgument" => Usage,
      "NotFound" => NotFound,
      "ElementNotFound" => NotFound,
      "WindowNotFound" => NotFound,
      "SnapshotNotFound" => NotFound,
      "Timeout" => Timeout,
      "Unavailable" => Unavailable,
      "PermissionDenied" => PermissionDenied,
      "NotSupported" => NotSupported,
      "Canceled" => deadlineElapsed ? Timeout : Canceled,
      _ => GenericFailure,
    };
  }
}
