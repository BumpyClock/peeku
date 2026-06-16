using System.Diagnostics;

namespace peeku;

/// <summary>
/// Shared launch and quit operations for the app lifecycle verb group (S4).
/// </summary>
internal static class AppLifecycle
{
  // ── Launch ───────────────────────────────────────────────────────────────────

  internal static async Task<AppLaunchResult> LaunchAsync(AppLaunchRequest req, CancellationToken ct)
  {
    var scope = Results.Start();
    try
    {
      ct.ThrowIfCancellationRequested();

      if (req is null || string.IsNullOrWhiteSpace(req.Target))
      {
        return Fail(scope, PeekuErrorCode.InvalidArgument, "Target (path or AUMID) is required.");
      }

      var psi = BuildProcessStartInfo(req.Target, req.Args);
      Process process;

      try
      {
        process = Process.Start(psi)
          ?? throw new InvalidOperationException("Process.Start returned null.");
      }
      catch (Exception ex) when (IsLaunchFailure(ex))
      {
        return Fail(scope, PeekuErrorCode.InvalidArgument,
          $"app not found for '{req.Target}'; verify the path or AUMID",
          new { exception = ex.GetType().Name, ex.Message });
      }

      int pid = process.Id;

      if (req.WaitUntilReady)
      {
        await WaitForReadyAsync(process, req.WaitMs, ct).ConfigureAwait(false);
      }

      WindowInfo? firstWindow = null;
      if (!req.NoFocus)
      {
        firstWindow = TryGetFirstWindow(pid);
        if (firstWindow is not null)
        {
          Win32Windows.BringToForegroundReliable(firstWindow.HwndHex);
        }
      }

      if (firstWindow is null)
      {
        firstWindow = TryGetFirstWindow(pid);
      }

      return new AppLaunchResult(Ok: true, Meta: scope.Meta(), ProcessId: pid, Window: firstWindow);
    }
    catch (OperationCanceledException)
    {
      return Fail(scope, PeekuErrorCode.Canceled, "Operation cancelled.");
    }
    catch (Exception ex)
    {
      return Fail(scope, PeekuErrorCode.Internal, "App launch failed.",
        new { exception = ex.GetType().FullName, ex.Message, ex.HResult });
    }
  }

  // ── Quit ────────────────────────────────────────────────────────────────────

  internal static async Task<AppQuitResult> QuitAsync(AppQuitRequest req, CancellationToken ct)
  {
    var scope = Results.Start();
    try
    {
      ct.ThrowIfCancellationRequested();

      if (req is null)
      {
        return QuitFail(scope, PeekuErrorCode.InvalidArgument, "Request is required.");
      }

      if (req.All)
      {
        return await QuitAllAsync(req, scope, ct).ConfigureAwait(false);
      }

      // Single-process quit: resolve by pid or processName.
      if (req.ProcessId is null && string.IsNullOrWhiteSpace(req.ProcessName))
      {
        return QuitFail(scope, PeekuErrorCode.InvalidArgument,
          "Provide processId or processName (or use --all).");
      }

      Process? target = null;
      if (req.ProcessId is not null)
      {
        try
        {
          target = Process.GetProcessById(req.ProcessId.Value);
        }
        catch (ArgumentException)
        {
          return QuitFail(scope, PeekuErrorCode.NotFound,
            $"No process found with pid {req.ProcessId.Value}.");
        }
      }
      else
      {
        var candidates = Process.GetProcessesByName(req.ProcessName!);
        if (candidates.Length == 0)
        {
          return QuitFail(scope, PeekuErrorCode.NotFound,
            $"No process found with name '{req.ProcessName}'.");
        }

        target = candidates[0];
        foreach (var extra in candidates.Skip(1))
        {
          extra.Dispose();
        }
      }

      using (target)
      {
        var (closed, killed) = await QuitProcessAsync(target, req.Force, req.WaitMs, ct).ConfigureAwait(false);
        return new AppQuitResult(Ok: true, Meta: scope.Meta(), Closed: closed, Killed: killed);
      }
    }
    catch (OperationCanceledException)
    {
      return QuitFail(scope, PeekuErrorCode.Canceled, "Operation cancelled.");
    }
    catch (Exception ex)
    {
      return QuitFail(scope, PeekuErrorCode.Internal, "App quit failed.",
        new { exception = ex.GetType().FullName, ex.Message, ex.HResult });
    }
  }

  // ── Relaunch ─────────────────────────────────────────────────────────────────

  internal static async Task<AppRelaunchResult> RelaunchAsync(AppRelaunchRequest req, CancellationToken ct)
  {
    var scope = Results.Start();
    try
    {
      ct.ThrowIfCancellationRequested();

      if (req is null)
      {
        return RelaunchFail(scope, PeekuErrorCode.InvalidArgument, "Request is required.");
      }

      if (req.ProcessId is null && string.IsNullOrWhiteSpace(req.ProcessName))
      {
        return RelaunchFail(scope, PeekuErrorCode.InvalidArgument,
          "Provide processId or processName.");
      }

      Process? target = null;
      if (req.ProcessId is not null)
      {
        try
        {
          target = Process.GetProcessById(req.ProcessId.Value);
        }
        catch (ArgumentException)
        {
          return RelaunchFail(scope, PeekuErrorCode.NotFound,
            $"No process found with pid {req.ProcessId.Value}.");
        }
      }
      else
      {
        var candidates = Process.GetProcessesByName(req.ProcessName!);
        if (candidates.Length == 0)
        {
          return RelaunchFail(scope, PeekuErrorCode.NotFound,
            $"No process found with name '{req.ProcessName}'.");
        }

        target = candidates[0];
        foreach (var extra in candidates.Skip(1))
        {
          extra.Dispose();
        }
      }

      string? executablePath;
      try
      {
        executablePath = target.MainModule?.FileName;
      }
      catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
      {
        target.Dispose();
        return RelaunchFail(scope, PeekuErrorCode.InvalidArgument,
          "cannot resolve executable path for relaunch; process may be elevated or packaged");
      }

      if (string.IsNullOrWhiteSpace(executablePath))
      {
        target.Dispose();
        return RelaunchFail(scope, PeekuErrorCode.InvalidArgument,
          "cannot resolve executable path for relaunch; process may be elevated or packaged");
      }

      using (target)
      {
        await QuitProcessAsync(target, force: false, req.WaitMs, ct).ConfigureAwait(false);
      }

      var launchReq = new AppLaunchRequest(
        Target: executablePath,
        WaitUntilReady: req.WaitUntilReady,
        WaitMs: req.WaitMs,
        NoFocus: req.NoFocus);

      var launched = await LaunchAsync(launchReq, ct).ConfigureAwait(false);
      if (!launched.Ok)
      {
        return new AppRelaunchResult(
          Ok: false,
          Meta: scope.Meta(),
          ExecutablePath: executablePath,
          Error: launched.Error);
      }

      return new AppRelaunchResult(
        Ok: true,
        Meta: scope.Meta(),
        ProcessId: launched.ProcessId,
        ExecutablePath: executablePath,
        Window: launched.Window);
    }
    catch (OperationCanceledException)
    {
      return RelaunchFail(scope, PeekuErrorCode.Canceled, "Operation cancelled.");
    }
    catch (Exception ex)
    {
      return RelaunchFail(scope, PeekuErrorCode.Internal, "App relaunch failed.",
        new { exception = ex.GetType().FullName, ex.Message, ex.HResult });
    }
  }

  // ── List ─────────────────────────────────────────────────────────────────────

  internal static Task<AppListResult> ListAsync(AppListRequest req, CancellationToken ct)
  {
    var scope = Results.Start();
    try
    {
      ct.ThrowIfCancellationRequested();

      var limit = req?.Limit ?? 100;
      var foregroundPid = Win32Windows.GetForegroundProcessId();

      // Pull all visible top-level windows (use a large cap so grouping sees everything).
      var allWindows = Win32Windows.ListWindows(new WindowsListRequest(Limit: 10000), ct);

      // Group by pid; pick representative window (prefer first with non-empty title, else first).
      var grouped = allWindows
        .GroupBy(w => w.ProcessId)
        .Select(g =>
        {
          var rep = g.FirstOrDefault(w => !string.IsNullOrWhiteSpace(w.Title)) ?? g.First();
          var pid = g.Key;
          string processName = rep.ProcessName ?? "";
          return new AppInfo(
            ProcessId: pid,
            ProcessName: processName,
            Title: rep.Title,
            Active: pid == foregroundPid);
        })
        .Take(limit)
        .ToList();

      return Task.FromResult(new AppListResult(Ok: true, Meta: scope.Meta(), Apps: grouped));
    }
    catch (OperationCanceledException)
    {
      return Task.FromResult(ListFail(scope, PeekuErrorCode.Canceled, "Operation cancelled."));
    }
    catch (Exception ex)
    {
      return Task.FromResult(ListFail(scope, PeekuErrorCode.Internal, "App list failed.",
        new { exception = ex.GetType().FullName, ex.Message, ex.HResult }));
    }
  }

  // ── Helpers ─────────────────────────────────────────────────────────────────

  private static ProcessStartInfo BuildProcessStartInfo(string target, string? args)
  {
    // UWP / packaged apps use shell:AppsFolder\<AUMID> URI.
    // Detect: contains '!' (typical AUMID separator) or caller passes it already as shell:AppsFolder\.
    var isAumid = target.Contains('!') ||
                  target.StartsWith("shell:AppsFolder", StringComparison.OrdinalIgnoreCase);

    var fileName = isAumid && !target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)
      ? $"shell:AppsFolder\\{target}"
      : target;

    return new ProcessStartInfo
    {
      UseShellExecute = true,
      FileName = fileName,
      Arguments = args ?? "",
    };
  }

  private static bool IsLaunchFailure(Exception ex)
    => ex is System.ComponentModel.Win32Exception
        or System.IO.FileNotFoundException
        or System.IO.DirectoryNotFoundException
        or InvalidOperationException { Message: not null };

  private static async Task WaitForReadyAsync(Process process, int waitMs, CancellationToken ct)
  {
    // WaitForInputIdle only works for GUI processes. Wrap in try/catch and fall back
    // to window-poll which works for both Win32 and UWP.
    var guiReady = false;
    try
    {
      guiReady = process.WaitForInputIdle(Math.Max(0, waitMs));
    }
    catch
    {
      // Non-GUI process (console) or packaged app: fall through to window poll.
    }

    if (!guiReady)
    {
      // Poll for a top-level window to appear.
      var deadline = DateTimeOffset.UtcNow.AddMilliseconds(Math.Max(0, waitMs));
      while (DateTimeOffset.UtcNow < deadline)
      {
        ct.ThrowIfCancellationRequested();
        var windows = Win32Windows.ListWindowsForProcess(process.Id);
        if (windows.Count > 0)
        {
          break;
        }

        await Task.Delay(150, ct).ConfigureAwait(false);
      }
    }
  }

  private static WindowInfo? TryGetFirstWindow(int pid)
  {
    try
    {
      var windows = Win32Windows.ListWindowsForProcess(pid);
      return windows.Count > 0 ? windows[0] : null;
    }
    catch
    {
      return null;
    }
  }

  private static async Task<(int Closed, int Killed)> QuitProcessAsync(
    Process process,
    bool force,
    int waitMs,
    CancellationToken ct)
  {
    if (force)
    {
      process.Kill(entireProcessTree: true);
      return (0, 1);
    }

    // Graceful: post WM_CLOSE to each top-level window of this pid.
    var windows = Win32Windows.ListWindowsForProcess(process.Id);
    foreach (var w in windows)
    {
      Win32Windows.PostCloseMessage(w.HwndHex);
    }

    // Poll for process exit.
    var deadline = DateTimeOffset.UtcNow.AddMilliseconds(Math.Max(0, waitMs));
    while (DateTimeOffset.UtcNow < deadline)
    {
      ct.ThrowIfCancellationRequested();
      if (process.HasExited)
      {
        return (1, 0);
      }

      await Task.Delay(150, ct).ConfigureAwait(false);
      try
      {
        process.Refresh();
      }
      catch
      {
        return (1, 0);
      }
    }

    // Timeout: force kill.
    try
    {
      process.Kill(entireProcessTree: true);
    }
    catch
    {
      // Already dead.
    }

    return (0, 1);
  }

  private static async Task<AppQuitResult> QuitAllAsync(AppQuitRequest req, ResultScope scope, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(req.ProcessName))
    {
      return QuitFail(scope, PeekuErrorCode.InvalidArgument,
        "--all requires --processName to scope the process group.");
    }

    var except = req.Except?.Select(n => n.Trim()).Where(n => n.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase)
                 ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    var processes = Process.GetProcessesByName(req.ProcessName);
    var closed = 0;
    var killed = 0;

    foreach (var p in processes)
    {
      using (p)
      {
        ct.ThrowIfCancellationRequested();

        var name = p.ProcessName;
        if (except.Contains(name))
        {
          continue;
        }

        var (c, k) = await QuitProcessAsync(p, req.Force, req.WaitMs, ct).ConfigureAwait(false);
        closed += c;
        killed += k;
      }
    }

    return new AppQuitResult(Ok: true, Meta: scope.Meta(), Closed: closed, Killed: killed);
  }

  private static AppLaunchResult Fail(ResultScope scope, PeekuErrorCode code, string message, object? details = null)
    => new(Ok: false, Meta: scope.Meta(), Error: PeekuErrors.Create(code, message, details));

  private static AppQuitResult QuitFail(ResultScope scope, PeekuErrorCode code, string message, object? details = null)
    => new(Ok: false, Meta: scope.Meta(), Error: PeekuErrors.Create(code, message, details));

  private static AppRelaunchResult RelaunchFail(ResultScope scope, PeekuErrorCode code, string message, object? details = null)
    => new(Ok: false, Meta: scope.Meta(), Error: PeekuErrors.Create(code, message, details));

  private static AppListResult ListFail(ResultScope scope, PeekuErrorCode code, string message, object? details = null)
    => new(Ok: false, Meta: scope.Meta(), Apps: Array.Empty<AppInfo>(), Error: PeekuErrors.Create(code, message, details));
}
