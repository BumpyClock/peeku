using peeku;

namespace peeku.Cli;

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
  internal static int For(PeekuError? error)
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
      // TODO(deadline-flag): the CLI currently surfaces wall-clock timeouts as Canceled
      // (CreateTimeoutCts cancels the linked token; daemon maps cancellation -> Canceled).
      // Best-effort: map the Canceled code to 8. A future deadline-flag refinement should
      // split wall-clock Timeout (4) from Ctrl-C Canceled (8) — see plan §5.
      "Canceled" => Canceled,
      _ => GenericFailure,
    };
  }
}
