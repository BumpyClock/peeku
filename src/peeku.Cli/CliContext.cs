using Serilog;

namespace peeku.Cli;

internal enum OutputFormat
{
  Pretty = 0,
  Json = 1,
}

internal sealed record CliContext(
  OutputFormat Format,
  TimeSpan Timeout,
  string? TraceId,
  ILogger Logger);

internal static class CliContextAccessor
{
  private static readonly AsyncLocal<CliContext?> CurrentLocal = new();

  internal static CliContext Current =>
    CurrentLocal.Value ?? throw new InvalidOperationException("CliContext not initialized");

  internal static void Set(CliContext ctx) => CurrentLocal.Value = ctx;

  internal static void Clear() => CurrentLocal.Value = null;
}
