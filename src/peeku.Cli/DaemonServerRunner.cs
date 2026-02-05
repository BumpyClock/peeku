namespace peeku.Cli;

/// <summary>
/// Starts the daemon server in the foreground and returns its exit code.
/// Example: <code>var code = await runner.RunAsync("peeku.user.v1", ct);</code>
/// </summary>
internal sealed class DaemonServerRunner
{
  internal async Task<int> RunAsync(string pipeName, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(pipeName))
    {
      throw new ArgumentException("Pipe name is required", nameof(pipeName));
    }

#if PEEKU_DAEMON
    return await global::peeku.Daemon.Program.Main(new[] { "--pipeName", pipeName.Trim() }).ConfigureAwait(false);
#else
    var launcher = new DaemonProcessLauncher(Directory.GetCurrentDirectory());
    var process = launcher.StartForeground(pipeName.Trim());
    await process.WaitForExitAsync(ct).ConfigureAwait(false);
    return process.ExitCode;
#endif
  }
}
