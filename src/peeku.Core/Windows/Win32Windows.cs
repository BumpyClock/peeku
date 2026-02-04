using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace peeku;

internal static class Win32Windows
{
  internal static IReadOnlyList<WindowInfo> ListWindows(WindowsListRequest req, CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();

    if (req.Limit <= 0)
    {
      return Array.Empty<WindowInfo>();
    }

    var windows = new List<WindowInfo>(capacity: Math.Clamp(req.Limit, 0, 256));
    var titleContains = string.IsNullOrWhiteSpace(req.TitleContains) ? null : req.TitleContains;
    var processName = string.IsNullOrWhiteSpace(req.ProcessName) ? null : req.ProcessName;
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
  private static extern IntPtr GetForegroundWindow();
}
