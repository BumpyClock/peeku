using System.Diagnostics;
using System.Reflection;

namespace peeku.Cli;

/// <summary>
/// Single owner of daemon spawn/connect lifecycle so the manual <c>daemon start</c> command and the
/// auto-spawn path in <see cref="CliPeekuClient.CreateDefault"/> share one implementation and cannot
/// drift. Owns the canonical pipe name, build-version stamp, the disable gate, and spawn+connect.
/// </summary>
internal static class DaemonLifecycle
{
  // Per-ping connect/probe budget while waiting for a freshly spawned daemon to answer.
  private static readonly TimeSpan PerTryProbe = TimeSpan.FromMilliseconds(250);

  // Pause between connection attempts while polling a cold-starting daemon.
  private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(120);

  /// <summary>Canonical per-user pipe name. The single source — server, manual start, and auto-spawn all use it.</summary>
  internal static string DefaultPipeName()
  {
    var user = Environment.UserName;
    var safeUser = string.IsNullOrWhiteSpace(user) ? "user" : user.Trim();
    return $"peeku.{safeUser}.v1";
  }

  /// <summary>Informational build version stamped into the marker (falls back to assembly version, then 0.0.0).</summary>
  internal static string BuildVersion()
  {
    var info = Assembly.GetExecutingAssembly()
      .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
      ?.InformationalVersion;
    if (!string.IsNullOrWhiteSpace(info))
    {
      return info.Trim();
    }

    var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString();
    return string.IsNullOrWhiteSpace(version) ? "0.0.0" : version;
  }

  /// <summary>
  /// True when <c>PEEKU_NO_DAEMON</c> is set to a truthy value. An agent or CI can export this to
  /// force the pure in-process path regardless of any running daemon. Treats unset, empty,
  /// <c>0</c>, and <c>false</c> as "daemon allowed"; anything else disables it.
  /// </summary>
  internal static bool IsDisabledByEnv()
  {
    var v = Environment.GetEnvironmentVariable("PEEKU_NO_DAEMON");
    if (string.IsNullOrWhiteSpace(v))
    {
      return false;
    }

    var t = v.Trim();
    return !t.Equals("0", StringComparison.Ordinal)
        && !t.Equals("false", StringComparison.OrdinalIgnoreCase);
  }

  /// <summary>
  /// True when auto-spawn is viable: the real <c>peeku-daemon</c> executable exists next to the CLI.
  /// When only the <c>dotnet run</c> fallback is available we must NOT auto-spawn (build-on-every-call).
  /// </summary>
  internal static bool CanAutoSpawn() => DaemonProcessLauncher.HasDaemonExecutable();

  /// <summary>
  /// Spawns the daemon in the background and writes its marker. Returns the marker. The caller is
  /// responsible for ensuring no live daemon already exists. Throws if the process fails to launch.
  /// </summary>
  internal static DaemonMarker Spawn()
  {
    var pipeName = DefaultPipeName();
    var launcher = new DaemonProcessLauncher(Directory.GetCurrentDirectory());
    var process = launcher.StartBackground(pipeName);
    var marker = new DaemonMarker(pipeName, process.Id, DateTimeOffset.UtcNow, "1", BuildVersion());
    marker.Save();
    return marker;
  }

  /// <summary>
  /// Spawns the daemon (writing its marker), then polls <c>TryPingAsync</c> until the daemon answers
  /// or <paramref name="budget"/> is exhausted. Returns a connected RPC client, or <c>null</c> if the
  /// daemon did not become reachable in time (it may still be coming up — the next CLI call will find
  /// it warm via the persisted marker) or the spawn itself failed.
  /// </summary>
  internal static DaemonJsonRpcClient? TrySpawnAndConnect(TimeSpan budget)
  {
    DaemonMarker marker;
    try
    {
      marker = Spawn();
    }
    catch
    {
      // Launch failed (missing exe, access denied, …). Caller falls back to in-proc.
      return null;
    }

    var rpc = new DaemonJsonRpcClient(marker.PipeName, PerTryProbe);
    var sw = Stopwatch.StartNew();
    while (sw.Elapsed < budget)
    {
      try
      {
        using var cts = new CancellationTokenSource(PerTryProbe);
        if (rpc.TryPingAsync(cts.Token).GetAwaiter().GetResult())
        {
          return rpc;
        }
      }
      catch
      {
        // Pipe not up yet — keep polling until the budget runs out.
      }

      Thread.Sleep(PollInterval);
    }

    return null;
  }
}
