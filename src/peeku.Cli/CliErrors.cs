using peeku;

namespace peeku.Cli;

/// <summary>
/// Writes a CLI-local failure using the SAME envelope shape Core results use
/// (<c>ok=false</c>, <c>meta</c>, <c>error{code,message,details}</c>), so agents parse a
/// single contract. Replaces the hand-rolled anonymous envelopes that drifted (and the
/// phantom <c>"InvalidOperation"</c> code that was never a <see cref="PeekuErrorCode"/>).
/// </summary>
internal static class CliErrors
{
  /// <summary>
  /// Serializes a <see cref="PeekuError"/> as a failure envelope to stdout and returns the
  /// mapped exit code, so call sites can do <c>return CliErrors.Write(ctx, error);</c>.
  /// </summary>
  internal static int Write(CliContext ctx, PeekuError error)
  {
    var meta = Results.Start(ctx.TraceId).Meta();
    CliOutput.Write(new CliErrorEnvelope(false, meta, error), ctx.Format);
    return ExitCodes.For(error);
  }

  private sealed record CliErrorEnvelope(bool Ok, ResultMeta Meta, PeekuError Error);
}
