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
  Option<int?> ProcessId);

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

    cmd.Add(focused);
    cmd.Add(desktop);
    cmd.Add(screenIndex);
    cmd.Add(hwnd);

    if (allowQuery)
    {
      cmd.Add(titleContains);
      cmd.Add(processName);
      cmd.Add(processId);
    }

    return new CliTargetOptions(
      Focused: focused,
      Desktop: desktop,
      ScreenIndex: screenIndex,
      Hwnd: hwnd,
      TitleContains: titleContains,
      ProcessName: processName,
      ProcessId: processId);
  }

  internal static Target ParseOrDefaultFocused(ParseResult parse, CliTargetOptions o)
    => Parse(parse, o, defaultFocused: true) ?? Target.Focused();

  internal static Target? ParseOptional(ParseResult parse, CliTargetOptions o)
    => Parse(parse, o, defaultFocused: false);

  private static Target? Parse(ParseResult parse, CliTargetOptions o, bool defaultFocused)
  {
    var focused = parse.GetValue(o.Focused);
    var desktop = parse.GetValue(o.Desktop);
    var screenIndex = parse.GetValue(o.ScreenIndex);
    var hwndRaw = parse.GetValue(o.Hwnd);

    var titleContains = parse.GetValue(o.TitleContains);
    var processName = parse.GetValue(o.ProcessName);
    var processId = parse.GetValue(o.ProcessId);

    var hasQuery = !string.IsNullOrWhiteSpace(titleContains) || !string.IsNullOrWhiteSpace(processName) || processId is not null;

    var count = 0;
    if (focused) count++;
    if (desktop) count++;
    if (screenIndex is not null) count++;
    if (!string.IsNullOrWhiteSpace(hwndRaw)) count++;
    if (hasQuery) count++;

    if (count == 0)
    {
      return defaultFocused ? Target.Focused() : null;
    }

    if (count > 1)
    {
      throw new ArgumentException("Provide exactly one target: --focused|--desktop|--screenIndex|--hwnd|query options.");
    }

    if (focused) return Target.Focused();
    if (desktop) return new Target.Desktop();
    if (screenIndex is not null) return new Target.Screen(screenIndex.Value);

    if (!string.IsNullOrWhiteSpace(hwndRaw))
    {
      var normalized = Win32WindowState.TryNormalizeHwndHex(hwndRaw!, out var n) ? n : hwndRaw!.Trim();
      return new Target.WindowByHwnd(normalized);
    }

    return new Target.WindowByQuery(new WindowQuery(
      TitleContains: string.IsNullOrWhiteSpace(titleContains) ? null : titleContains!.Trim(),
      ProcessName: string.IsNullOrWhiteSpace(processName) ? null : processName!.Trim(),
      ProcessId: processId));
  }
}

