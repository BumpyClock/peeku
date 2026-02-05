using System.CommandLine;
using System.CommandLine.Parsing;

namespace peeku.Cli;

internal static class CliCommandTree
{
  internal static void AddCommands(RootCommand root)
  {
    root.Add(CreateDoctorCommand());
    root.Add(CreateWindowsCommand());
    root.Add(CreateCaptureCommand());
  }

  private static Command CreateDoctorCommand()
  {
    var cmd = new Command("doctor", "Self-checks (optionally deep: capture + UIA)");

    var deepOpt = new Option<bool>("--deep") { Description = "Run slower checks (capture + UIA)" };
    cmd.Add(deepOpt);

    cmd.SetAction(async (ParseResult parse, CancellationToken ct) =>
    {
      var ctx = CliContextAccessor.Current;
      using var cts = CreateTimeoutCts(ctx.Timeout, ct);

      var client = CliPeekuClient.CreateDefault();
      var res = await client.DoctorAsync(new global::peeku.DoctorRequest(Deep: parse.GetValue(deepOpt)), cts.Token).ConfigureAwait(false);
      CliOutput.Write(res, ctx.Format);
      return res.Ok ? 0 : 1;
    });

    return cmd;
  }

  private static Command CreateWindowsCommand()
  {
    var windows = new Command("windows", "Window discovery");
    windows.Add(CreateWindowsListCommand());
    windows.Add(CreateWindowsFocusedCommand());
    return windows;
  }

  private static Command CreateWindowsListCommand()
  {
    var cmd = new Command("list", "List windows with optional filters");

    var includeMinimizedOpt = new Option<bool>("--includeMinimized")
    {
      Description = "Include minimized windows"
    };

    var titleContainsOpt = new Option<string?>("--titleContains")
    {
      Description = "Filter windows where title contains text"
    };

    var processNameOpt = new Option<string?>("--processName")
    {
      Description = "Filter windows by process name"
    };

    cmd.Add(includeMinimizedOpt);
    cmd.Add(titleContainsOpt);
    cmd.Add(processNameOpt);

    cmd.SetAction(async (ParseResult parse, CancellationToken ct) =>
    {
      var ctx = CliContextAccessor.Current;
      using var cts = CreateTimeoutCts(ctx.Timeout, ct);

      var client = CliPeekuClient.CreateDefault();
      var req = new global::peeku.WindowsListRequest(
        TitleContains: parse.GetValue(titleContainsOpt),
        ProcessName: parse.GetValue(processNameOpt),
        Limit: 50);

      var res = await client.WindowsListAsync(req, cts.Token).ConfigureAwait(false);

      if (res.Ok && !parse.GetValue(includeMinimizedOpt))
      {
        var windows = new List<global::peeku.WindowInfo>(capacity: Math.Clamp(res.Windows.Count, 0, 256));
        var skipped = 0;
        foreach (var w in res.Windows)
        {
          if (Win32WindowState.IsMinimized(w.HwndHex))
          {
            skipped++;
            continue;
          }

          windows.Add(w);
        }

        var warning = CombineWarnings(res.Meta.Warning, skipped > 0 ? $"Skipped {skipped} minimized windows" : null);
        res = new global::peeku.WindowListResult(
          Ok: res.Ok,
          Meta: res.Meta with { Warning = warning },
          Windows: windows,
          Error: res.Error);
      }

      CliOutput.Write(res, ctx.Format);
      return res.Ok ? 0 : 1;
    });

    return cmd;
  }

  private static Command CreateWindowsFocusedCommand()
  {
    var cmd = new Command("focused", "Get focused window");

    cmd.SetAction(async (ParseResult _, CancellationToken ct) =>
    {
      var ctx = CliContextAccessor.Current;
      using var cts = CreateTimeoutCts(ctx.Timeout, ct);

      var client = CliPeekuClient.CreateDefault();
      var res = await client.WindowsFocusedAsync(cts.Token).ConfigureAwait(false);
      CliOutput.Write(res, ctx.Format);
      return res.Ok ? 0 : 1;
    });

    return cmd;
  }

  private static Command CreateCaptureCommand()
  {
    var capture = new Command("capture", "Capture screenshots/images");
    capture.Add(CreateCaptureImageCommand());
    return capture;
  }

  private static Command CreateCaptureImageCommand()
  {
    var cmd = new Command("image", "Capture an image (defaults to focused window)");

    var hwndOpt = new Option<string?>("--hwnd") { Description = "Capture a specific window by HWND hex (e.g. 0x0000000000123456)" };
    hwndOpt.Validators.Add(r =>
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

    var outOpt = new Option<string?>("--out")
    {
      Description = "Optional output file path (else temp path)"
    };

    var includeBase64Opt = new Option<bool>("--includeBase64")
    {
      Description = "Include base64-encoded PNG in response"
    };

    cmd.Add(hwndOpt);
    cmd.Add(outOpt);
    cmd.Add(includeBase64Opt);

    cmd.SetAction(async (ParseResult parse, CancellationToken ct) =>
    {
      var ctx = CliContextAccessor.Current;
      using var cts = CreateTimeoutCts(ctx.Timeout, ct);

      var hwndRaw = parse.GetValue(hwndOpt);
      var target = string.IsNullOrWhiteSpace(hwndRaw)
        ? global::peeku.Target.Focused()
        : new global::peeku.Target.WindowByHwnd(Win32WindowState.TryNormalizeHwndHex(hwndRaw!, out var normalized) ? normalized : hwndRaw!);

      var req = new global::peeku.CaptureImageRequest(
        Target: target,
        OutPath: parse.GetValue(outOpt),
        IncludeBase64: parse.GetValue(includeBase64Opt));

      var client = CliPeekuClient.CreateDefault();
      var res = await client.CaptureImageAsync(req, cts.Token).ConfigureAwait(false);
      CliOutput.Write(res, ctx.Format);
      return res.Ok ? 0 : 1;
    });

    return cmd;
  }

  private static CancellationTokenSource CreateTimeoutCts(TimeSpan timeout, CancellationToken ct)
  {
    var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    if (timeout > TimeSpan.Zero)
    {
      cts.CancelAfter(timeout);
    }

    return cts;
  }

  private static string? CombineWarnings(string? a, string? b)
  {
    if (string.IsNullOrWhiteSpace(a))
    {
      return string.IsNullOrWhiteSpace(b) ? null : b;
    }

    if (string.IsNullOrWhiteSpace(b))
    {
      return a;
    }

    return $"{a} {b}";
  }
}
