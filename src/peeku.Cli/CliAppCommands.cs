using System.CommandLine;
using System.CommandLine.Parsing;
using peeku;

namespace peeku.Cli;

internal static class CliAppCommands
{
  internal static Command CreateAppCommand()
  {
    var app = new Command("app", "App lifecycle: launch and quit applications");
    app.Add(CreateLaunchCommand());
    app.Add(CreateQuitCommand());
    return app;
  }

  private static Command CreateLaunchCommand()
  {
    var cmd = new Command("launch", "Launch an application by path or AUMID");

    var targetArg = new Argument<string>("target")
    {
      Description = "File path, URI, or AUMID (e.g. Microsoft.WindowsCalculator_8wekyb3d8bbwe!App)",
      Arity = ArgumentArity.ExactlyOne,
    };

    var argsOpt = new Option<string?>("--args") { Description = "Optional command-line arguments to pass" };

    var waitUntilReadyOpt = new Option<bool>("--waitUntilReady")
    {
      Description = "Wait for input-idle and a top-level window before returning"
    };
    waitUntilReadyOpt.DefaultValueFactory = _ => false;

    var waitMsOpt = new Option<int>("--waitMs")
    {
      Description = "Max ms to wait when --waitUntilReady is set (default 5000)"
    };
    waitMsOpt.DefaultValueFactory = _ => 5000;

    var noFocusOpt = new Option<bool>("--noFocus")
    {
      Description = "Skip post-launch BringToForeground activation"
    };
    noFocusOpt.DefaultValueFactory = _ => false;

    cmd.Add(targetArg);
    cmd.Add(argsOpt);
    cmd.Add(waitUntilReadyOpt);
    cmd.Add(waitMsOpt);
    cmd.Add(noFocusOpt);

    cmd.SetAction(async (ParseResult parse, CancellationToken ct) =>
    {
      var ctx = CliContextAccessor.Current;
      using var scope = TimeoutScope.Create(ctx.Timeout, ct);

      var client = CliPeekuClient.CreateDefault();
      var req = new AppLaunchRequest(
        Target: parse.GetValue(targetArg) ?? "",
        Args: parse.GetValue(argsOpt),
        WaitUntilReady: parse.GetValue(waitUntilReadyOpt),
        WaitMs: parse.GetValue(waitMsOpt),
        NoFocus: parse.GetValue(noFocusOpt));

      var res = await client.AppLaunchAsync(req, scope.Token).ConfigureAwait(false);
      CliOutput.Write(res, ctx.Format);
      return res.Ok ? 0 : ExitCodes.For(res.Error, scope.DeadlineElapsed);
    });

    return cmd;
  }

  private static Command CreateQuitCommand()
  {
    var cmd = new Command("quit", "Quit an application (graceful WM_CLOSE then force-kill on timeout)");

    var processIdOpt = new Option<int?>("--pid") { Description = "Process ID to quit" };
    var processNameOpt = new Option<string?>("--processName") { Description = "Process name to quit" };

    var forceOpt = new Option<bool>("--force")
    {
      Description = "Kill immediately without WM_CLOSE grace period"
    };
    forceOpt.DefaultValueFactory = _ => false;

    var allOpt = new Option<bool>("--all")
    {
      Description = "Quit all processes matching --processName"
    };
    allOpt.DefaultValueFactory = _ => false;

    var exceptOpt = new Option<string[]>("--except")
    {
      Description = "Process names to skip when using --all",
      AllowMultipleArgumentsPerToken = true,
    };
    exceptOpt.DefaultValueFactory = _ => Array.Empty<string>();

    var waitMsOpt = new Option<int>("--waitMs")
    {
      Description = "Grace period before force-kill in ms (default 3000)"
    };
    waitMsOpt.DefaultValueFactory = _ => 3000;

    cmd.Add(processIdOpt);
    cmd.Add(processNameOpt);
    cmd.Add(forceOpt);
    cmd.Add(allOpt);
    cmd.Add(exceptOpt);
    cmd.Add(waitMsOpt);

    cmd.SetAction(async (ParseResult parse, CancellationToken ct) =>
    {
      var ctx = CliContextAccessor.Current;
      using var scope = TimeoutScope.Create(ctx.Timeout, ct);

      var exceptValues = parse.GetValue(exceptOpt) ?? Array.Empty<string>();

      var client = CliPeekuClient.CreateDefault();
      var req = new AppQuitRequest(
        ProcessId: parse.GetValue(processIdOpt),
        ProcessName: parse.GetValue(processNameOpt),
        Force: parse.GetValue(forceOpt),
        All: parse.GetValue(allOpt),
        Except: exceptValues.Length > 0 ? exceptValues : null,
        WaitMs: parse.GetValue(waitMsOpt));

      var res = await client.AppQuitAsync(req, scope.Token).ConfigureAwait(false);
      CliOutput.Write(res, ctx.Format);
      return res.Ok ? 0 : ExitCodes.For(res.Error, scope.DeadlineElapsed);
    });

    return cmd;
  }
}
