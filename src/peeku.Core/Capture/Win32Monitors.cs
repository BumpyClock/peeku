using System.Runtime.InteropServices;

namespace peeku;

internal static class Win32Monitors
{
  internal static IReadOnlyList<nint> List()
  {
    var monitors = new List<nint>(capacity: 8);

    MonitorEnumProc cb = (hMon, _, _, _) =>
    {
      monitors.Add(hMon);
      return true;
    };

    _ = EnumDisplayMonitors(0, 0, cb, 0);
    return monitors;
  }

  internal static nint Primary()
    => MonitorFromWindow(0, MONITOR_DEFAULTTOPRIMARY);

  private const uint MONITOR_DEFAULTTOPRIMARY = 1;

  private delegate bool MonitorEnumProc(nint hMonitor, nint hdcMonitor, nint lprcMonitor, nint dwData);

  [DllImport("user32.dll")]
  private static extern bool EnumDisplayMonitors(nint hdc, nint lprcClip, MonitorEnumProc lpfnEnum, nint dwData);

  [DllImport("user32.dll")]
  private static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);
}

