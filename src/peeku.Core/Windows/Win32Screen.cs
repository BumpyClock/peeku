using System.Runtime.InteropServices;

namespace peeku;

/// <summary>
/// Screen-geometry helpers: DPI awareness bootstrapping and virtual-screen coordinate math.
/// Call <see cref="EnsureProcessDpiAware"/> once at process start — before any UIA, FlaUI,
/// or capture touch — so all subsequent Win32 rect queries return physical pixels.
/// </summary>
internal static class Win32Screen
{
  private static readonly int SM_XVIRTUALSCREEN  = 76;
  private static readonly int SM_YVIRTUALSCREEN  = 77;
  private static readonly int SM_CXVIRTUALSCREEN = 78;
  private static readonly int SM_CYVIRTUALSCREEN = 79;

  // ── DPI awareness ───────────────────────────────────────────────────────────

  /// <summary>
  /// Sets per-monitor DPI awareness for the whole process.  Tries three tiers in order:
  /// <list type="number">
  ///   <item>SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2) — Win10 1703+</item>
  ///   <item>SetProcessDpiAwareness(PROCESS_PER_MONITOR_DPI_AWARE) — Win8.1+</item>
  ///   <item>SetProcessDPIAware() — Vista+</item>
  /// </list>
  /// Logs which tier took effect to stderr (plain text, no Serilog dependency).
  /// Safe to call once at process start; idempotent if called again (OS rejects the second
  /// call but that is not a fatal error).
  /// </summary>
  internal static void EnsureProcessDpiAware()
  {
    // Tier 1: DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4
    try
    {
      var v2Context = new IntPtr(-4);
      if (SetProcessDpiAwarenessContext(v2Context))
      {
        Console.Error.WriteLine("[peeku] DPI: SetProcessDpiAwarenessContext(PER_MONITOR_V2) OK");
        return;
      }
    }
    catch
    {
      // Entry point absent on older OS — fall through.
    }

    // Tier 2: shcore PROCESS_PER_MONITOR_DPI_AWARE = 2
    try
    {
      const int PROCESS_PER_MONITOR_DPI_AWARE = 2;
      var hr = SetProcessDpiAwareness(PROCESS_PER_MONITOR_DPI_AWARE);
      if (hr == 0) // S_OK
      {
        Console.Error.WriteLine("[peeku] DPI: SetProcessDpiAwareness(PER_MONITOR) OK");
        return;
      }
    }
    catch
    {
      // shcore absent or returned error — fall through.
    }

    // Tier 3: legacy user32 — always available, system-DPI only
    try
    {
      SetProcessDPIAware();
      Console.Error.WriteLine("[peeku] DPI: SetProcessDPIAware() OK (system-DPI fallback)");
    }
    catch
    {
      Console.Error.WriteLine("[peeku] DPI: all awareness calls failed; proceeding unaware");
    }
  }

  // ── Virtual-screen geometry ──────────────────────────────────────────────────

  /// <summary>
  /// Bounding rect of the entire virtual desktop (union of all monitors).
  /// X and/or Y may be NEGATIVE if a monitor is positioned left of / above the primary.
  /// </summary>
  internal static VirtualScreenRect VirtualScreenRect
  {
    get
    {
      var x  = GetSystemMetrics(SM_XVIRTUALSCREEN);
      var y  = GetSystemMetrics(SM_YVIRTUALSCREEN);
      var cx = GetSystemMetrics(SM_CXVIRTUALSCREEN);
      var cy = GetSystemMetrics(SM_CYVIRTUALSCREEN);
      return new VirtualScreenRect(x, y, cx, cy);
    }
  }

  /// <summary>
  /// Converts a physical-pixel screen coordinate to the 0..65535 absolute range
  /// used by <c>SendInput</c> with <c>MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK</c>.
  /// Uses the live virtual-screen rect from <see cref="GetSystemMetrics"/>.
  /// </summary>
  internal static (int ax, int ay) ToAbsolute(int screenX, int screenY)
    => ToAbsolute(VirtualScreenRect, screenX, screenY);

  /// <summary>
  /// Pure coordinate helper — injectable for tests (no GetSystemMetrics call).
  /// </summary>
  internal static (int ax, int ay) ToAbsolute(VirtualScreenRect vs, int screenX, int screenY)
  {
    var ax = (int)Math.Round((screenX - vs.X) * 65535.0 / (vs.Width  - 1));
    var ay = (int)Math.Round((screenY - vs.Y) * 65535.0 / (vs.Height - 1));
    return (ax, ay);
  }

  // ── P/Invokes ────────────────────────────────────────────────────────────────

  [DllImport("user32.dll")]
  private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

  [DllImport("shcore.dll")]
  private static extern int SetProcessDpiAwareness(int value);

  [DllImport("user32.dll")]
  private static extern bool SetProcessDPIAware();

  [DllImport("user32.dll")]
  internal static extern int GetSystemMetrics(int nIndex);
}

/// <summary>Value object for the virtual-screen bounding rect.</summary>
internal readonly record struct VirtualScreenRect(int X, int Y, int Width, int Height);
