using System.Diagnostics;

namespace peeku.Cli;

/// <summary>
/// Launches the daemon process either in the background or foreground based on a pipe name.
/// Example: <code>var process = launcher.StartBackground("peeku.user.v1");</code>
/// </summary>
internal sealed class DaemonProcessLauncher
{
  private readonly string _workingDirectory;

  internal DaemonProcessLauncher(string workingDirectory)
  {
    if (string.IsNullOrWhiteSpace(workingDirectory))
    {
      throw new ArgumentException("Working directory is required", nameof(workingDirectory));
    }

    _workingDirectory = workingDirectory;
  }

  internal Process StartBackground(string pipeName)
    => StartProcess(pipeName, true);

  internal Process StartForeground(string pipeName)
    => StartProcess(pipeName, false);

  private Process StartProcess(string pipeName, bool background)
  {
    if (string.IsNullOrWhiteSpace(pipeName))
    {
      throw new ArgumentException("Pipe name is required", nameof(pipeName));
    }

    var (fileName, args) = ResolveCommand(pipeName.Trim());
    var info = new ProcessStartInfo
    {
      FileName = fileName,
      Arguments = args,
      WorkingDirectory = _workingDirectory,
    };

    if (background)
    {
      // CRITICAL for auto-spawn: launch detached so the long-lived daemon does NOT inherit the
      // launching CLI's handles. With UseShellExecute=false, .NET starts the child with
      // bInheritHandles=TRUE and duplicates EVERY inheritable handle into it — including the pipe
      // behind `peeku ... | jq`. The daemon would then hold that pipe's write end open and the
      // reader would never see EOF (HANG until the daemon dies). ShellExecuteEx
      // (UseShellExecute=true) launches without inheriting handles, so the CLI's stdout closes
      // normally on exit. WindowStyle=Hidden keeps the console subsystem daemon off-screen.
      info.UseShellExecute = true;
      info.WindowStyle = ProcessWindowStyle.Hidden;
    }
    else
    {
      // Foreground `serve`: keep the user's console attached so they see logs and Ctrl-C works.
      info.UseShellExecute = false;
      info.WindowStyle = ProcessWindowStyle.Normal;
    }

    var process = Process.Start(info);
    if (process is null)
    {
      throw new InvalidOperationException("Failed to start daemon process");
    }

    return process;
  }

  private static (string FileName, string Arguments) ResolveCommand(string pipeName)
  {
    if (TryGetDaemonExecutable(out var daemonExe))
    {
      return (daemonExe, $"--pipeName \"{pipeName}\"");
    }

    return ("dotnet", $"run --project src/peeku.Daemon -c Release -- --pipeName \"{pipeName}\"");
  }

  /// <summary>
  /// True when the real <c>peeku-daemon</c> executable sits next to the CLI. Auto-spawn gates on
  /// this: the <c>dotnet run</c> fallback is acceptable for an explicit <c>daemon start</c>, but
  /// triggering a build-and-run on every CLI call would be a disaster, so auto-spawn must skip it.
  /// </summary>
  internal static bool HasDaemonExecutable() => TryGetDaemonExecutable(out _);

  private static bool TryGetDaemonExecutable(out string path)
  {
    var baseDir = AppContext.BaseDirectory;
    var exePath = Path.Combine(baseDir, "peeku-daemon.exe");
    if (File.Exists(exePath))
    {
      path = exePath;
      return true;
    }

    var altPath = Path.Combine(baseDir, "peeku-daemon");
    if (File.Exists(altPath))
    {
      path = altPath;
      return true;
    }

    path = "";
    return false;
  }
}
