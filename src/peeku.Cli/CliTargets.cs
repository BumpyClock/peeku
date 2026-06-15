using System.CommandLine;
using System.CommandLine.Parsing;
using peeku;

namespace peeku.Cli;

internal sealed record CliTargetOptions(
  Option<bool> Focused,
  Option<bool> Desktop,
  Option<int?> ScreenIndex,
  Option<string?> Hwnd,
  Option<string?> TitleContains,
  Option<string?> ProcessName,
  Option<int?> ProcessId,
  Option<string?> App,
  Option<int?> Pid);

internal static class CliTargets
{
  internal static CliTargetOptions AddTo(Command cmd, bool allowQuery)
  {
    var focused = new Option<bool>("--focused") { Description = "Target focused window" };
    focused.DefaultValueFactory = _ => false;

    var desktop = new Option<bool>("--desktop") { Description = "Target primary monitor" };
    desktop.DefaultValueFactory = _ => false;

    var screenIndex = new Option<int?>("--screenIndex") { Description = "Target screen index (0-based)" };

    var hwnd = new Option<string?>("--hwnd") { Description = "Target window HWND hex (e.g. 0x0000000000123456)" };
    hwnd.Validators.Add(r =>
    {
      var v = r.GetValueOrDefault<string?>();
      if (string.IsNullOrWhiteSpace(v))
      {
        return;
      }

      if (!Win32WindowState.TryNormalizeHwndHex(v, out _))
      {
        r.AddError("Invalid --hwnd. Expected hex like 0x0000000000123456");
      }
    });

    var titleContains = new Option<string?>("--titleContains") { Description = "Query: window title contains" };
    var processName = new Option<string?>("--processName") { Description = "Query: process name" };
    var processId = new Option<int?>("--processId") { Description = "Query: process id" };
    var app = new Option<string?>("--app") { Description = "Target window by process name (alias of --processName)" };
    var pid = new Option<int?>("--pid") { Description = "Target window by process id (alias of --processId)" };

    cmd.Add(focused);
    cmd.Add(desktop);
    cmd.Add(screenIndex);
    cmd.Add(hwnd);

    if (allowQuery)
    {
      cmd.Add(titleContains);
      cmd.Add(processName);
      cmd.Add(processId);
      cmd.Add(app);
      cmd.Add(pid);
    }

    return new CliTargetOptions(
      Focused: focused,
      Desktop: desktop,
      ScreenIndex: screenIndex,
      Hwnd: hwnd,
      TitleContains: titleContains,
      ProcessName: processName,
      ProcessId: processId,
      App: app,
      Pid: pid);
  }

  /// <summary>
  /// Resolves the target, defaulting to the focused window when no target flags are given.
  /// Returns false (with <paramref name="error"/> set) on a multi-target conflict so callers
  /// can emit a clean validation envelope instead of throwing.
  /// </summary>
  internal static bool TryParseOrDefaultFocused(ParseResult parse, CliTargetOptions o, out Target target, out string? error)
  {
    if (!TryParse(parse, o, defaultFocused: true, out var t, out error))
    {
      target = Target.Focused();
      return false;
    }

    target = t ?? Target.Focused();
    return true;
  }

  /// <summary>
  /// Resolves an optional target (null when no target flags are given).
  /// Returns false (with <paramref name="error"/> set) on a multi-target conflict.
  /// </summary>
  internal static bool TryParseOptional(ParseResult parse, CliTargetOptions o, out Target? target, out string? error)
    => TryParse(parse, o, defaultFocused: false, out target, out error);

  private static bool TryParse(ParseResult parse, CliTargetOptions o, bool defaultFocused, out Target? target, out string? error)
  {
    target = null;
    error = null;

    var focused = parse.GetValue(o.Focused);
    var desktop = parse.GetValue(o.Desktop);
    var screenIndex = parse.GetValue(o.ScreenIndex);
    var hwndRaw = parse.GetValue(o.Hwnd);

    var titleContains = parse.GetValue(o.TitleContains);
    var processName = parse.GetValue(o.ProcessName);
    var processId = parse.GetValue(o.ProcessId);
    var app = parse.GetValue(o.App);
    var pid = parse.GetValue(o.Pid);

    // --app is an alias of --processName; --pid an alias of --processId. Fold them in.
    var effectiveProcessName = !string.IsNullOrWhiteSpace(processName) ? processName : app;
    var effectiveProcessId = processId ?? pid;

    var hasQuery = !string.IsNullOrWhiteSpace(titleContains) || !string.IsNullOrWhiteSpace(effectiveProcessName) || effectiveProcessId is not null;

    var count = 0;
    if (focused) count++;
    if (desktop) count++;
    if (screenIndex is not null) count++;
    if (!string.IsNullOrWhiteSpace(hwndRaw)) count++;
    if (hasQuery) count++;

    if (count == 0)
    {
      target = defaultFocused ? Target.Focused() : null;
      return true;
    }

    if (count > 1)
    {
      error = "Provide exactly one target: --focused|--desktop|--screenIndex|--hwnd|query options.";
      return false;
    }

    if (focused) { target = Target.Focused(); return true; }
    if (desktop) { target = new Target.Desktop(); return true; }
    if (screenIndex is not null) { target = new Target.Screen(screenIndex.Value); return true; }

    if (!string.IsNullOrWhiteSpace(hwndRaw))
    {
      var normalized = Win32WindowState.TryNormalizeHwndHex(hwndRaw!, out var n) ? n : hwndRaw!.Trim();
      target = new Target.WindowByHwnd(normalized);
      return true;
    }

    target = new Target.WindowByQuery(new WindowQuery(
      TitleContains: string.IsNullOrWhiteSpace(titleContains) ? null : titleContains!.Trim(),
      ProcessName: string.IsNullOrWhiteSpace(effectiveProcessName) ? null : effectiveProcessName!.Trim(),
      ProcessId: effectiveProcessId));
    return true;
  }
}

