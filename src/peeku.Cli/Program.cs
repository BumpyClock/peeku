using System.CommandLine;
using System.CommandLine.Invocation;
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

    var formatOpt = new Option<string>("--format") { Description = "Output format: pretty|json" };
    formatOpt.Recursive = true;
    formatOpt.DefaultValueFactory = _ => "pretty";
    formatOpt.Validators.Add(r =>
    {
      var v = r.GetValueOrDefault<string>() ?? "pretty";
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

    root.Add(formatOpt);
    root.Add(timeoutOpt);
    root.Add(logLevelOpt);
    root.Add(logFileOpt);
    root.Add(traceIdOpt);
    root.Add(profileOpt);

    CliCommandTree.AddCommands(root);

    var parse = root.Parse(args);

    var formatRaw = parse.GetValue(formatOpt) ?? "pretty";
    var format = string.Equals(formatRaw, "json", StringComparison.OrdinalIgnoreCase)
      ? OutputFormat.Json
      : OutputFormat.Pretty;

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
      return await parse.InvokeAsync(
        new InvocationConfiguration
        {
          Output = Console.Out,
          Error = Console.Error,
        },
        CancellationToken.None).ConfigureAwait(false);
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
}
