using System.CommandLine;
using System.CommandLine.Parsing;
using peeku;

namespace peeku.Cli;

/// <summary>
/// Registers the top-level `diff` verb.
///
/// Two-target design: the AFTER target uses the standard --focused/--app/--pid/--hwnd/--desktop
/// flags; the BEFORE target uses a parallel set prefixed with "before" (--beforeFocused,
/// --beforeApp, --beforePid, --beforeHwnd, --beforeDesktop, --beforeTitleContains,
/// --beforeProcessName, --beforeProcessId, --beforeScreenIndex).
///
/// Ergonomic rationale: symmetric naming makes it obvious which flags belong to which snapshot.
/// Most common use: diff focused window before/after an action:
///   peeku diff --beforeFocused --focused
/// Or diff two different apps:
///   peeku diff --beforeApp notepad --app calc
/// </summary>
internal static class CliDiffCommands
{
  internal static void AddAll(RootCommand root)
  {
    root.Add(CreateDiffCommand());
  }

  private static Command CreateDiffCommand()
  {
    var cmd = new Command("diff", "Snapshot two targets and diff their UIA trees by refId");

    // AFTER target — standard flags
    var afterOpts = CliTargets.AddTo(cmd, allowQuery: true);

    // BEFORE target — parallel flags with "before" prefix
    var beforeFocused = new Option<bool>("--beforeFocused") { Description = "Before: target focused window" };
    beforeFocused.DefaultValueFactory = _ => false;

    var beforeDesktop = new Option<bool>("--beforeDesktop") { Description = "Before: target primary monitor" };
    beforeDesktop.DefaultValueFactory = _ => false;

    var beforeScreenIndex = new Option<int?>("--beforeScreenIndex") { Description = "Before: target screen index (0-based)" };

    var beforeHwnd = new Option<string?>("--beforeHwnd") { Description = "Before: target window HWND hex" };
    beforeHwnd.Validators.Add(r =>
    {
      var v = r.GetValueOrDefault<string?>();
      if (string.IsNullOrWhiteSpace(v)) return;
      if (!Win32WindowState.TryNormalizeHwndHex(v, out _))
      {
        r.AddError("Invalid --beforeHwnd. Expected hex like 0x0000000000123456");
      }
    });

    var beforeTitleContains = new Option<string?>("--beforeTitleContains") { Description = "Before: query window title contains" };
    var beforeProcessName = new Option<string?>("--beforeProcessName") { Description = "Before: query process name" };
    var beforeProcessId = new Option<int?>("--beforeProcessId") { Description = "Before: query process id" };
    var beforeApp = new Option<string?>("--beforeApp") { Description = "Before: target window by process name (alias of --beforeProcessName)" };
    var beforePid = new Option<int?>("--beforePid") { Description = "Before: target window by process id (alias of --beforeProcessId)" };

    cmd.Add(beforeFocused);
    cmd.Add(beforeDesktop);
    cmd.Add(beforeScreenIndex);
    cmd.Add(beforeHwnd);
    cmd.Add(beforeTitleContains);
    cmd.Add(beforeProcessName);
    cmd.Add(beforeProcessId);
    cmd.Add(beforeApp);
    cmd.Add(beforePid);

    // Shared snapshot parameters
    var depthOpt = new Option<int>("--depth") { Description = "Tree depth for both snapshots" };
    depthOpt.DefaultValueFactory = _ => 6;

    var maxNodesOpt = new Option<int>("--maxNodes") { Description = "Max nodes for both snapshots" };
    maxNodesOpt.DefaultValueFactory = _ => 5000;

    var propsOpt = new Option<string>("--includeProperties") { Description = "basic|all" };
    propsOpt.DefaultValueFactory = _ => "basic";
    propsOpt.Validators.Add(r =>
    {
      var v = r.GetValueOrDefault<string>() ?? "basic";
      if (!string.Equals(v, "basic", StringComparison.OrdinalIgnoreCase) &&
          !string.Equals(v, "all", StringComparison.OrdinalIgnoreCase))
      {
        r.AddError("Invalid --includeProperties. Allowed: basic|all");
      }
    });

    cmd.Add(depthOpt);
    cmd.Add(maxNodesOpt);
    cmd.Add(propsOpt);

    cmd.SetAction(async (ParseResult parse, CancellationToken ct) =>
    {
      var ctx = CliContextAccessor.Current;
      using var cts = CreateTimeoutCts(ctx.Timeout, ct);

      // Parse AFTER target (defaults to focused)
      if (!CliTargets.TryParseOrDefaultFocused(parse, afterOpts, out var targetAfter, out var afterTargetError))
      {
        return CliErrors.Write(ctx, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, afterTargetError ?? "Invalid after target."));
      }

      // Parse BEFORE target from the "before*" flags
      if (!TryParseBeforeTarget(parse,
        beforeFocused, beforeDesktop, beforeScreenIndex, beforeHwnd,
        beforeTitleContains, beforeProcessName, beforeProcessId, beforeApp, beforePid,
        out var targetBefore, out var beforeTargetError))
      {
        return CliErrors.Write(ctx, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, beforeTargetError ?? "Invalid before target."));
      }

      var props = ParseProps(parse.GetValue(propsOpt));

      var req = new DiffRequest(
        TargetBefore: targetBefore,
        TargetAfter: targetAfter,
        Depth: parse.GetValue(depthOpt),
        MaxNodes: parse.GetValue(maxNodesOpt),
        IncludeProperties: props);

      var client = CliPeekuClient.CreateDefault();
      var res = await client.DiffAsync(req, cts.Token).ConfigureAwait(false);
      CliOutput.Write(res, ctx.Format);
      return res.Ok ? 0 : ExitCodes.For(res.Error);
    });

    return cmd;
  }

  private static bool TryParseBeforeTarget(
    ParseResult parse,
    Option<bool> focused,
    Option<bool> desktop,
    Option<int?> screenIndex,
    Option<string?> hwnd,
    Option<string?> titleContains,
    Option<string?> processName,
    Option<int?> processId,
    Option<string?> app,
    Option<int?> pid,
    out Target target,
    out string? error)
  {
    target = Target.Focused();
    error = null;

    var isFocused = parse.GetValue(focused);
    var isDesktop = parse.GetValue(desktop);
    var screenIdx = parse.GetValue(screenIndex);
    var hwndRaw = parse.GetValue(hwnd);
    var titleContainsVal = parse.GetValue(titleContains);
    var processNameVal = parse.GetValue(processName);
    var processIdVal = parse.GetValue(processId);
    var appVal = parse.GetValue(app);
    var pidVal = parse.GetValue(pid);

    var effectiveProcessName = !string.IsNullOrWhiteSpace(processNameVal) ? processNameVal : appVal;
    var effectiveProcessId = processIdVal ?? pidVal;

    var hasQuery = !string.IsNullOrWhiteSpace(titleContainsVal)
      || !string.IsNullOrWhiteSpace(effectiveProcessName)
      || effectiveProcessId is not null;

    var count = 0;
    if (isFocused) count++;
    if (isDesktop) count++;
    if (screenIdx is not null) count++;
    if (!string.IsNullOrWhiteSpace(hwndRaw)) count++;
    if (hasQuery) count++;

    if (count == 0)
    {
      // default to focused when no before-flags set
      target = Target.Focused();
      return true;
    }

    if (count > 1)
    {
      error = "Provide exactly one before-target: --beforeFocused|--beforeDesktop|--beforeScreenIndex|--beforeHwnd|before-query options.";
      return false;
    }

    if (isFocused) { target = Target.Focused(); return true; }
    if (isDesktop) { target = new Target.Desktop(); return true; }
    if (screenIdx is not null) { target = new Target.Screen(screenIdx.Value); return true; }

    if (!string.IsNullOrWhiteSpace(hwndRaw))
    {
      var normalized = Win32WindowState.TryNormalizeHwndHex(hwndRaw!, out var n) ? n : hwndRaw!.Trim();
      target = new Target.WindowByHwnd(normalized);
      return true;
    }

    target = new Target.WindowByQuery(new WindowQuery(
      TitleContains: string.IsNullOrWhiteSpace(titleContainsVal) ? null : titleContainsVal!.Trim(),
      ProcessName: string.IsNullOrWhiteSpace(effectiveProcessName) ? null : effectiveProcessName!.Trim(),
      ProcessId: effectiveProcessId));
    return true;
  }

  private static UiaPropertiesMode ParseProps(string? raw)
    => string.Equals(raw, "all", StringComparison.OrdinalIgnoreCase)
      ? UiaPropertiesMode.All
      : UiaPropertiesMode.Basic;

  private static CancellationTokenSource CreateTimeoutCts(TimeSpan timeout, CancellationToken ct)
  {
    var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    if (timeout > TimeSpan.Zero)
    {
      cts.CancelAfter(timeout);
    }

    return cts;
  }
}
