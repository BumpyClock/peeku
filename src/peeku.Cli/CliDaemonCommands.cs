using System.CommandLine;
using System.CommandLine.Parsing;
using System.Reflection;
using peeku;

namespace peeku.Cli;

/// <summary>
/// Registers the <c>daemon</c> subcommand and its four children:
/// <c>start</c>, <c>stop</c>, <c>status</c> (default), and <c>serve</c>.
/// </summary>
internal static class CliDaemonCommands
{
  // Short connect/ping budget so a stale marker no longer costs a full --timeout wait.
  private static readonly TimeSpan DaemonProbeBudget = TimeSpan.FromMilliseconds(300);

  internal static Command CreateDaemonCommand()
  {
    var daemon = new Command("daemon", "Manage the peeku background daemon");

    daemon.Add(CreateStartCommand());
    daemon.Add(CreateStopCommand());
    daemon.Add(CreateStatusCommand());
    daemon.Add(CreateServeCommand());

    // `peeku daemon` with no sub-verb runs status (the safe, read-only default).
    daemon.SetAction(async (ParseResult parse, CancellationToken ct) =>
      await RunStatusAsync(parse, ct).ConfigureAwait(false));

    return daemon;
  }

  // ── start ────────────────────────────────────────────────────────────────

  private static Command CreateStartCommand()
  {
    var cmd = new Command("start", "Start daemon in background (idempotent)");

    cmd.SetAction(async (ParseResult _, CancellationToken ct) =>
    {
      var ctx = CliContextAccessor.Current;

      if (DaemonMarker.TryLoad(out var existing))
      {
        if (existing.IsAlive())
        {
          using var pingCts = new CancellationTokenSource(DaemonProbeBudget);
          var pingClient = new DaemonJsonRpcClient(existing.PipeName, DaemonProbeBudget);
          if (await pingClient.TryPingAsync(pingCts.Token).ConfigureAwait(false))
          {
            // Already running — idempotent success.
            CliOutput.Write(new DaemonStartResult(Ok: true, Pid: existing.Pid, PipeName: existing.PipeName), ctx.Format);
            return ExitCodes.Success;
          }
        }

        DaemonMarker.TryDeleteStale();
      }

      try
      {
        var pipeName = DefaultPipeName();
        var launcher = new DaemonProcessLauncher(Directory.GetCurrentDirectory());
        var process = launcher.StartBackground(pipeName);
        var marker = new DaemonMarker(pipeName, process.Id, DateTimeOffset.UtcNow, "1", BuildVersion());
        marker.Save();
        CliOutput.Write(new DaemonStartResult(Ok: true, Pid: process.Id, PipeName: pipeName), ctx.Format);
        return ExitCodes.Success;
      }
      catch (Exception ex)
      {
        ctx.Logger.Error(ex, "Daemon start failed");
        return CliErrors.Write(ctx, PeekuErrors.Create(PeekuErrorCode.Unavailable, "Daemon could not be started.", new { exception = ex.GetType().FullName, ex.Message }));
      }
    });

    return cmd;
  }

  // ── stop ─────────────────────────────────────────────────────────────────

  private static Command CreateStopCommand()
  {
    var cmd = new Command("stop", "Stop the running daemon");

    cmd.SetAction(async (ParseResult _, CancellationToken ct) =>
    {
      var ctx = CliContextAccessor.Current;

      if (!DaemonMarker.TryLoad(out var marker))
      {
        ctx.Logger.Error("Daemon marker not found");
        return CliErrors.Write(ctx, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Daemon is not running."));
      }

      if (!marker.IsAlive())
      {
        DaemonMarker.TryDeleteStale();
        return CliErrors.Write(ctx, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Daemon is not running (stale marker removed)."));
      }

      try
      {
        var rpc = new DaemonJsonRpcClient(marker.PipeName, DaemonProbeBudget);
        using var shutdownCts = new CancellationTokenSource(DaemonProbeBudget);
        var res = await rpc.CallAsync<DaemonOkResult>("server.shutdown", new Dictionary<string, object?>(), shutdownCts.Token).ConfigureAwait(false);
        if (!res.Ok)
        {
          ctx.Logger.Error("Daemon shutdown returned not ok");
          return CliErrors.Write(ctx, PeekuErrors.Create(PeekuErrorCode.Unavailable, "Daemon shutdown failed."));
        }

        DaemonMarker.TryDeleteStale();
        CliOutput.Write(new DaemonOkResult(Ok: true, Meta: Results.Start(ctx.TraceId).Meta()), ctx.Format);
        return ExitCodes.Success;
      }
      catch (Exception ex)
      {
        ctx.Logger.Error(ex, "Daemon shutdown failed");
        return CliErrors.Write(ctx, PeekuErrors.Create(PeekuErrorCode.Unavailable, "Daemon unreachable.", new { exception = ex.GetType().FullName, ex.Message }));
      }
    });

    return cmd;
  }

  // ── status ────────────────────────────────────────────────────────────────

  private static Command CreateStatusCommand()
  {
    var cmd = new Command("status", "Query daemon status (always exits 0)");

    cmd.SetAction(async (ParseResult parse, CancellationToken ct) =>
      await RunStatusAsync(parse, ct).ConfigureAwait(false));

    return cmd;
  }

  private static async Task<int> RunStatusAsync(ParseResult _, CancellationToken ct)
  {
    var ctx = CliContextAccessor.Current;

    if (!DaemonMarker.TryLoad(out var marker))
    {
      CliOutput.Write(new DaemonStatusResult(
        Running: false,
        Pid: null,
        PipeName: null,
        StartedAt: null,
        ProtocolVersion: null,
        BuildVersion: null), ctx.Format);
      return ExitCodes.Success;
    }

    if (!marker.IsAlive())
    {
      DaemonMarker.TryDeleteStale();
      CliOutput.Write(new DaemonStatusResult(
        Running: false,
        Pid: null,
        PipeName: null,
        StartedAt: null,
        ProtocolVersion: null,
        BuildVersion: null), ctx.Format);
      return ExitCodes.Success;
    }

    var running = false;
    try
    {
      using var pingCts = new CancellationTokenSource(DaemonProbeBudget);
      var rpc = new DaemonJsonRpcClient(marker.PipeName, DaemonProbeBudget);
      running = await rpc.TryPingAsync(pingCts.Token).ConfigureAwait(false);
    }
    catch
    {
      running = false;
    }

    CliOutput.Write(new DaemonStatusResult(
      Running: running,
      Pid: marker.Pid,
      PipeName: marker.PipeName,
      StartedAt: marker.StartedAt,
      ProtocolVersion: marker.ProtocolVersion,
      BuildVersion: marker.BuildVersion), ctx.Format);

    return ExitCodes.Success;
  }

  // ── serve ─────────────────────────────────────────────────────────────────

  private static Command CreateServeCommand()
  {
    var cmd = new Command("serve", "Run daemon server in foreground (blocks until Ctrl-C)");

    cmd.SetAction(async (ParseResult _, CancellationToken ct) =>
    {
      var runner = new DaemonServerRunner();
      var pipeName = DefaultPipeName();
      return await runner.RunAsync(pipeName, ct).ConfigureAwait(false);
    });

    return cmd;
  }

  // ── helpers ───────────────────────────────────────────────────────────────

  private static string DefaultPipeName()
  {
    var user = Environment.UserName;
    var safeUser = string.IsNullOrWhiteSpace(user) ? "user" : user.Trim();
    return $"peeku.{safeUser}.v1";
  }

  private static string BuildVersion()
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

  // ── result records ────────────────────────────────────────────────────────

  private sealed record DaemonStartResult(bool Ok, int Pid, string PipeName);

  private sealed record DaemonStatusResult(
    bool Running,
    int? Pid,
    string? PipeName,
    DateTimeOffset? StartedAt,
    string? ProtocolVersion,
    string? BuildVersion);
}
