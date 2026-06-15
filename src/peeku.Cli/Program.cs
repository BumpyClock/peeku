using System.CommandLine;
using System.CommandLine.Invocation;
using System.Reflection;
using peeku;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace peeku.Cli;

internal static class Program
{
  private static async Task<int> Main(string[] args)
  {
    if (args.Length == 0)
    {
      args = ["--help"];
    }

    var root = new RootCommand("peeku - Windows UI automation + capture tooling");

    var formatOpt = new Option<string>("--format") { Description = "Output format: json|pretty (default json)" };
    formatOpt.Recursive = true;
    formatOpt.DefaultValueFactory = _ => "json";
    formatOpt.Validators.Add(r =>
    {
      var v = r.GetValueOrDefault<string>() ?? "json";
      if (!string.Equals(v, "pretty", StringComparison.OrdinalIgnoreCase) &&
          !string.Equals(v, "json", StringComparison.OrdinalIgnoreCase))
      {
        r.AddError("Invalid --format. Allowed: pretty|json");
      }
    });

    var timeoutOpt = new Option<TimeSpan>("--timeout") { Description = "Default timeout (e.g. 00:00:10)" };
    timeoutOpt.Recursive = true;
    timeoutOpt.DefaultValueFactory = _ => TimeSpan.FromSeconds(10);

    var logLevelOpt = new Option<string>("--log-level") { Description = "Log level: trace|debug|info|warn|error" };
    logLevelOpt.Recursive = true;
    logLevelOpt.DefaultValueFactory = _ => "info";
    logLevelOpt.Validators.Add(r =>
    {
      var v = r.GetValueOrDefault<string>() ?? "info";
      if (!string.Equals(v, "trace", StringComparison.OrdinalIgnoreCase) &&
          !string.Equals(v, "debug", StringComparison.OrdinalIgnoreCase) &&
          !string.Equals(v, "info", StringComparison.OrdinalIgnoreCase) &&
          !string.Equals(v, "warn", StringComparison.OrdinalIgnoreCase) &&
          !string.Equals(v, "error", StringComparison.OrdinalIgnoreCase))
      {
        r.AddError("Invalid --log-level. Allowed: trace|debug|info|warn|error");
      }
    });

    var logFileOpt = new Option<string?>("--log-file") { Description = "Optional log file path" };
    logFileOpt.Recursive = true;

    var traceIdOpt = new Option<string?>("--trace-id") { Description = "Optional trace id (else generated)" };
    traceIdOpt.Recursive = true;

    var profileOpt = new Option<string?>("--profile") { Description = "Optional profile name (reserved)" };
    profileOpt.Recursive = true;

    var serverOpt = new Option<bool>("--server") { Description = "Run daemon server in foreground" };
    serverOpt.Recursive = true;

    var daemonOpt = new Option<bool>("--daemon") { Description = "Manage daemon (spawn/stop)" };
    daemonOpt.Recursive = true;

    var stopOpt = new Option<bool>("--stop") { Description = "Stop daemon (requires --daemon)" };
    stopOpt.Recursive = true;

    root.Add(formatOpt);
    root.Add(timeoutOpt);
    root.Add(logLevelOpt);
    root.Add(logFileOpt);
    root.Add(traceIdOpt);
    root.Add(profileOpt);
    root.Add(serverOpt);
    root.Add(daemonOpt);
    root.Add(stopOpt);

    CliCommandTree.AddCommands(root);

    var parse = root.Parse(args);

    var formatRaw = parse.GetValue(formatOpt) ?? "json";
    var format = string.Equals(formatRaw, "pretty", StringComparison.OrdinalIgnoreCase)
      ? OutputFormat.Pretty
      : OutputFormat.Json;

    var timeout = parse.GetValue(timeoutOpt);
    var traceId = parse.GetValue(traceIdOpt) ?? Guid.NewGuid().ToString("n");

    var levelRaw = parse.GetValue(logLevelOpt) ?? "info";
    var level = levelRaw.ToLowerInvariant() switch
    {
      "trace" => LogEventLevel.Verbose,
      "debug" => LogEventLevel.Debug,
      "info" => LogEventLevel.Information,
      "warn" => LogEventLevel.Warning,
      "error" => LogEventLevel.Error,
      _ => LogEventLevel.Information,
    };

    Log.Logger = CreateLogger(level, format, traceId, parse.GetValue(logFileOpt));
    CliContextAccessor.Set(new CliContext(format, timeout, traceId, Log.Logger));

    try
    {
      var runServer = parse.GetValue(serverOpt);
      var runDaemon = parse.GetValue(daemonOpt);
      var stopDaemon = parse.GetValue(stopOpt);

      if (runServer)
      {
        return await RunServerAsync().ConfigureAwait(false);
      }

      if (stopDaemon && !runDaemon)
      {
        // Usage error: --stop requires --daemon.
        Log.Logger.Error("Stop requested without daemon flag");
        return ExitCodes.Usage;
      }

      if (runDaemon)
      {
        return await RunDaemonAsync(stopDaemon).ConfigureAwait(false);
      }

      // Parser/usage errors (unknown flags, missing required, validator AddError) never reach a
      // handler. Surface them as exit 2 (usage) instead of InvokeAsync's default 1. (plan §5)
      if (parse.Errors.Count > 0)
      {
        // Let InvokeAsync render the diagnostic/help text to stderr, then override the code.
        await parse.InvokeAsync(
          new InvocationConfiguration
          {
            Output = Console.Out,
            Error = Console.Error,
          },
          CancellationToken.None).ConfigureAwait(false);
        return ExitCodes.Usage;
      }

      return await parse.InvokeAsync(
        new InvocationConfiguration
        {
          Output = Console.Out,
          Error = Console.Error,
        },
        CancellationToken.None).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
      // Safety net: SCL 2.0.2 has no exception middleware, so an unhandled exception that
      // escapes a handler would print a raw stack trace with no JSON envelope. Convert it to
      // a clean error envelope on stdout and a mapped exit code. (Handlers should still fail
      // gracefully; this guards against the ones that don't.)
      var ctx = CliContextAccessor.Current;
      var error = ex switch
      {
        ArgumentException => PeekuErrors.Create(PeekuErrorCode.InvalidArgument, ex.Message),
        OperationCanceledException => PeekuErrors.Create(PeekuErrorCode.Canceled, "Operation cancelled"),
        _ => PeekuErrors.Create(PeekuErrorCode.Internal, ex.Message, new { exception = ex.GetType().FullName }),
      };
      return CliErrors.Write(ctx, error);
    }
    finally
    {
      CliContextAccessor.Clear();
      Log.CloseAndFlush();
    }
  }

  private static ILogger CreateLogger(LogEventLevel level, OutputFormat format, string traceId, string? logFile)
  {
    var loggerConfig = new LoggerConfiguration()
      .MinimumLevel.Is(level)
      .Enrich.WithProperty("traceId", traceId)
      .Enrich.FromLogContext();

    if (format == OutputFormat.Json)
    {
      loggerConfig = loggerConfig.WriteTo.Console(new RenderedCompactJsonFormatter(), standardErrorFromLevel: LogEventLevel.Verbose);
    }
    else
    {
      loggerConfig = loggerConfig.WriteTo.Console(standardErrorFromLevel: LogEventLevel.Verbose);
    }

    if (!string.IsNullOrWhiteSpace(logFile))
    {
      loggerConfig = loggerConfig.WriteTo.File(new RenderedCompactJsonFormatter(), logFile);
    }

    return loggerConfig.CreateLogger();
  }

  private static async Task<int> RunServerAsync()
  {
    var runner = new DaemonServerRunner();
    var pipeName = DefaultPipeName();
    return await runner.RunAsync(pipeName, CancellationToken.None).ConfigureAwait(false);
  }

  private static async Task<int> RunDaemonAsync(bool stop)
  {
    return stop
      ? await StopDaemonAsync().ConfigureAwait(false)
      : await StartDaemonAsync().ConfigureAwait(false);
  }

  private static async Task<int> StartDaemonAsync()
  {
    var ctx = CliContextAccessor.Current;
    if (DaemonMarker.TryLoad(out var existing))
    {
      // PID-liveness before the pipe ping: a dead/recycled PID means a stale marker; drop it
      // and respawn rather than paying a full --timeout connect wait. (plan §6)
      if (existing.IsAlive())
      {
        var pingClient = new DaemonJsonRpcClient(existing.PipeName, DaemonProbeBudget);
        using var cts = new CancellationTokenSource(DaemonProbeBudget);
        if (await pingClient.TryPingAsync(cts.Token).ConfigureAwait(false))
        {
          return 0;
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
      return 0;
    }
    catch (Exception ex)
    {
      ctx.Logger.Error(ex, "Daemon start failed");
      // Daemon could not be spawned/reached -> unreachable.
      return ExitCodes.Unavailable;
    }
  }

  private static async Task<int> StopDaemonAsync()
  {
    var ctx = CliContextAccessor.Current;
    if (!DaemonMarker.TryLoad(out var marker))
    {
      ctx.Logger.Error("Daemon marker not found");
      // Nothing to stop -> daemon unreachable.
      return ExitCodes.Unavailable;
    }

    // PID-liveness before the pipe round-trip: if the daemon is already dead, just clear the
    // stale marker (idempotent success) instead of paying a connect timeout. (plan §6)
    if (!marker.IsAlive())
    {
      DaemonMarker.TryDeleteStale();
      return 0;
    }

    try
    {
      var rpc = new DaemonJsonRpcClient(marker.PipeName, DaemonProbeBudget);
      using var cts = new CancellationTokenSource(DaemonProbeBudget);
      var res = await rpc.CallAsync<DaemonOkResult>("server.shutdown", new Dictionary<string, object?>(), cts.Token).ConfigureAwait(false);
      if (!res.Ok)
      {
        ctx.Logger.Error("Daemon shutdown returned not ok");
        return ExitCodes.Unavailable;
      }

      DaemonMarker.TryDeleteStale();
      return 0;
    }
    catch (Exception ex)
    {
      ctx.Logger.Error(ex, "Daemon shutdown failed");
      return ExitCodes.Unavailable;
    }
  }

  private static string DefaultPipeName()
  {
    var user = Environment.UserName;
    var safeUser = string.IsNullOrWhiteSpace(user) ? "user" : user.Trim();
    return $"peeku.{safeUser}.v1";
  }

  // Short connect/ping budget for daemon management so a stale marker no longer costs a full
  // command --timeout (PID-liveness already filters dead markers). (plan §6)
  private static readonly TimeSpan DaemonProbeBudget = TimeSpan.FromMilliseconds(300);

  /// <summary>
  /// Build version stamped into the daemon marker. Reads <see cref="AssemblyInformationalVersion"/>
  /// (e.g. <c>0.1.0+sha</c>) so the CLI marker and the daemon self-report agree. (plan §6)
  /// </summary>
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
}
