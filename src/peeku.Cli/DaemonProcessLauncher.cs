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
      UseShellExecute = false,
      CreateNoWindow = background,
      WindowStyle = background ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal,
    };

    var process = Process.Start(info);
    if (process is null)
    {
      throw new InvalidOperationException("Failed to start daemon process");
    }

    return process;
  }

  private static (string FileName, string Arguments) ResolveCommand(string pipeName)
  {
    var daemonExe = FindDaemonExecutable();
    if (daemonExe.Length > 0)
    {
      return (daemonExe, $"--pipeName \"{pipeName}\"");
    }

    return ("dotnet", $"run --project src/peeku.Daemon -c Release -- --pipeName \"{pipeName}\"");
  }

  private static string FindDaemonExecutable()
  {
    var baseDir = AppContext.BaseDirectory;
    var exePath = Path.Combine(baseDir, "peeku-daemon.exe");
    if (File.Exists(exePath))
    {
      return exePath;
    }

    var altPath = Path.Combine(baseDir, "peeku-daemon");
    if (File.Exists(altPath))
    {
      return altPath;
    }

    return "";
  }
}
