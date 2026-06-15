using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace peeku;

internal static class Win32Windows
{
  internal static IReadOnlyList<WindowInfo> ListWindows(WindowsListRequest req, CancellationToken ct)
    => ListWindows(req, processId: null, ct);

  /// <summary>
  /// Enumerates top-level windows. When <paramref name="processId"/> is set, the pid filter is
  /// applied INSIDE the EnumWindows callback (before the Limit cap), so a busy desktop with many
  /// other top-level windows can never push the target pid's windows past the cap and hide them.
  /// That matters because the durable refId re-walk (ResolveRootsForPid) depends on always reaching
  /// the target pid's real window. The unscoped overload keeps its existing cap-then-filter behavior.
  /// </summary>
  internal static IReadOnlyList<WindowInfo> ListWindows(WindowsListRequest req, int? processId, CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();

    if (req.Limit <= 0)
    {
      return Array.Empty<WindowInfo>();
    }

    var windows = new List<WindowInfo>(capacity: Math.Clamp(req.Limit, 0, 256));
    var titleContains = string.IsNullOrWhiteSpace(req.TitleContains) ? null : req.TitleContains;
    var processName = string.IsNullOrWhiteSpace(req.ProcessName) ? null : req.ProcessName;
    var pidFilter = processId is > 0 ? processId.Value : (int?)null;
    var cancelled = false;
    Exception? fatal = null;

    EnumWindowsProc cb = (hwnd, _) =>
    {
      try
      {
        ct.ThrowIfCancellationRequested();

        var info = TryGetWindowInfo(hwnd);
        if (info is null)
        {
          return true;
        }

        // Pid filter runs before the limit so the target pid's windows are never starved by the cap.
        if (pidFilter is not null && info.ProcessId != pidFilter.Value)
        {
          return true;
        }

        if (titleContains is not null)
        {
          if (info.Title is null)
          {
            return true;
          }

          if (!info.Title.Contains(titleContains, StringComparison.OrdinalIgnoreCase))
          {
            return true;
          }
        }

        if (processName is not null)
        {
          if (info.ProcessName is null)
          {
            return true;
          }

          if (!string.Equals(info.ProcessName, processName, StringComparison.OrdinalIgnoreCase))
          {
            return true;
          }
        }

        windows.Add(info);

        if (windows.Count >= req.Limit)
        {
          return false;
        }

        return true;
      }
      catch (OperationCanceledException)
      {
        cancelled = true;
        return false;
      }
      catch (Exception ex)
      {
        fatal = ex;
        return false;
      }
    };

    _ = EnumWindows(cb, IntPtr.Zero);

    if (cancelled)
    {
      ct.ThrowIfCancellationRequested();
    }

    if (fatal is not null)
    {
      throw new InvalidOperationException("EnumWindows callback failed.", fatal);
    }

    return windows;
  }

  internal static WindowInfo? GetFocusedWindow()
  {
    var hwnd = GetForegroundWindow();
    if (hwnd == IntPtr.Zero)
    {
      return null;
    }

    return TryGetWindowInfo(hwnd);
  }

  /// <summary>
  /// Brings the given window to the foreground so keyboard focus lands inside it. Delegates to the
  /// hardened path (the unlock dance) because a bare <see cref="SetForegroundWindow"/> silently fails
  /// when the caller isn't already the foreground process — the common case for a CLI/daemon.
  /// </summary>
  internal static bool BringToForeground(IntPtr hwnd)
    => BringToForegroundReliable(hwnd);

  /// <summary>
  /// Hardened foreground activation. <see cref="SetForegroundWindow"/> silently no-ops unless the
  /// calling thread owns the current foreground window, so we do the standard "unlock dance":
  /// AllowSetForegroundWindow, then AttachThreadInput to the current foreground thread (which makes
  /// Windows treat us as same-input-context and permits the focus change), restore the window if
  /// minimized, SetForegroundWindow, then detach. Best-effort: each step is guarded; returns whether
  /// the target ended up foreground.
  /// </summary>
  internal static bool BringToForegroundReliable(IntPtr hwnd)
  {
    if (hwnd == IntPtr.Zero)
    {
      return false;
    }

    // Already foreground: nothing to do.
    if (GetForegroundWindow() == hwnd)
    {
      EnsureRestored(hwnd);
      return true;
    }

    _ = AllowSetForegroundWindow(ASFW_ANY);

    var foreground = GetForegroundWindow();
    var targetThread = GetWindowThreadProcessId(hwnd, out _);
    var foregroundThread = foreground == IntPtr.Zero
      ? 0u
      : GetWindowThreadProcessId(foreground, out _);
    var currentThread = GetCurrentThreadId();

    var attachedForeground = false;
    var attachedTarget = false;
    try
    {
      // Attach our input queue to the current foreground thread (and the target's) so the focus
      // change is permitted. Skip attaching to ourselves (AttachThreadInput rejects self-attach).
      if (foregroundThread != 0 && foregroundThread != currentThread)
      {
        attachedForeground = AttachThreadInput(currentThread, foregroundThread, true);
      }

      if (targetThread != 0 && targetThread != currentThread && targetThread != foregroundThread)
      {
        attachedTarget = AttachThreadInput(currentThread, targetThread, true);
      }

      EnsureRestored(hwnd);
      _ = BringWindowToTop(hwnd);
      var ok = SetForegroundWindow(hwnd);

      return ok || GetForegroundWindow() == hwnd;
    }
    finally
    {
      if (attachedTarget)
      {
        _ = AttachThreadInput(currentThread, targetThread, false);
      }

      if (attachedForeground)
      {
        _ = AttachThreadInput(currentThread, foregroundThread, false);
      }
    }
  }

  private static void EnsureRestored(IntPtr hwnd)
  {
    if (IsIconic(hwnd))
    {
      _ = ShowWindow(hwnd, SW_RESTORE);
    }
  }

  /// <summary>
  /// Resolves a target window to its native handle for focus operations. Returns the handle and the
  /// window's metadata, or null when the target cannot be resolved to a concrete window.
  /// </summary>
  internal static (IntPtr Hwnd, WindowInfo Info)? ResolveTargetWindow(Target target, CancellationToken ct)
  {
    switch (target)
    {
      case Target.FocusedWindow:
      {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
          return null;
        }

        var info = TryGetWindowInfo(hwnd);
        return info is null ? null : (hwnd, info);
      }

      case Target.WindowByHwnd hwndTarget:
      {
        if (!TryParseHwndHex(hwndTarget.HwndHex, out var hwnd))
        {
          return null;
        }

        var info = TryGetWindowInfo(hwnd);
        return info is null ? null : (hwnd, info);
      }

      case Target.WindowByQuery queryTarget:
      {
        var q = queryTarget.Query;
        var candidates = ListWindows(
          new WindowsListRequest(TitleContains: q.TitleContains, ProcessName: q.ProcessName, Limit: 200),
          ct);

        var match = candidates.FirstOrDefault(w => q.ProcessId is null || w.ProcessId == q.ProcessId.Value);
        if (match is null || !TryParseHwndHex(match.HwndHex, out var hwnd))
        {
          return null;
        }

        return (hwnd, match);
      }

      default:
        // Desktop/Screen are not focusable windows.
        return null;
    }
  }

  private static bool TryParseHwndHex(string hwndHex, out IntPtr hwnd)
  {
    hwnd = IntPtr.Zero;
    if (string.IsNullOrWhiteSpace(hwndHex))
    {
      return false;
    }

    var s = hwndHex.Trim();
    if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
    {
      s = s[2..];
    }

    if (!long.TryParse(s, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var value))
    {
      return false;
    }

    hwnd = unchecked((IntPtr)value);
    return hwnd != IntPtr.Zero;
  }

  private static WindowInfo? TryGetWindowInfo(IntPtr hwnd)
  {
    var title = GetTitle(hwnd);
    _ = GetWindowThreadProcessId(hwnd, out var pidU);
    var pid = unchecked((int)pidU);

    string? processName = null;
    if (pid > 0)
    {
      try
      {
        processName = Process.GetProcessById(pid).ProcessName;
      }
      catch
      {
      }
    }

    return new WindowInfo(
      HwndHex: ToHwndHex(hwnd),
      ProcessId: pid,
      Title: string.IsNullOrWhiteSpace(title) ? null : title,
      ProcessName: processName);
  }

  private static string ToHwndHex(IntPtr hwnd)
    => IntPtr.Size <= 4
      ? $"0x{hwnd.ToInt64():X8}"
      : $"0x{hwnd.ToInt64():X16}";

  private static string? GetTitle(IntPtr hwnd)
  {
    var sb = new StringBuilder(capacity: 2048);
    var len = GetWindowText(hwnd, sb, sb.Capacity);
    if (len <= 0)
    {
      return null;
    }

    return sb.ToString(0, len);
  }

  private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

  [DllImport("user32.dll")]
  private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

  [DllImport("user32.dll", CharSet = CharSet.Unicode)]
  private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

  [DllImport("user32.dll")]
  private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

  [DllImport("user32.dll")]
  internal static extern IntPtr GetForegroundWindow();

  [DllImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  internal static extern bool IsWindow(IntPtr hWnd);

  [DllImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool SetForegroundWindow(IntPtr hWnd);

  private const int SW_RESTORE = 9;
  private const uint ASFW_ANY = 0xFFFFFFFF;

  [DllImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

  [DllImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool AllowSetForegroundWindow(uint dwProcessId);

  [DllImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool BringWindowToTop(IntPtr hWnd);

  [DllImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

  [DllImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool IsIconic(IntPtr hWnd);

  [DllImport("kernel32.dll")]
  private static extern uint GetCurrentThreadId();
}
