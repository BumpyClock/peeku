using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Graphics.Capture;

namespace peeku;

[SupportedOSPlatform("windows10.0.18362.0")]
internal static class GraphicsCaptureItemFactory
{
  internal static bool TryCreate(Target target, CancellationToken ct, out GraphicsCaptureItem? item, out PeekuError? error)
  {
    item = null;
    error = null;

    try
    {
      ct.ThrowIfCancellationRequested();

      var interop = GetInterop();

      return target switch
      {
        Target.FocusedWindow => TryCreateForFocusedWindow(interop, ct, out item, out error),
        Target.WindowByHwnd hwnd => TryCreateForHwnd(interop, hwnd.HwndHex, out item, out error),
        Target.WindowByQuery query => TryCreateForQuery(interop, query.Query, ct, out item, out error),
        Target.Screen screen => TryCreateForScreenIndex(interop, screen.ScreenIndex, out item, out error),
        Target.Desktop => TryCreateForPrimaryMonitor(interop, out item, out error),
        _ => Fail(PeekuErrorCode.NotSupported, "Target kind not supported for capture.", out error),
      };
    }
    catch (OperationCanceledException)
    {
      return Fail(PeekuErrorCode.Canceled, "Operation cancelled.", out error);
    }
    catch (Exception ex)
    {
      return Fail(
        PeekuErrorCode.Internal,
        "Failed to create capture item.",
        out error,
        details: new { exception = ex.GetType().FullName, ex.Message, ex.HResult });
    }
  }

  private static bool TryCreateForFocusedWindow(
    IGraphicsCaptureItemInterop interop,
    CancellationToken ct,
    out GraphicsCaptureItem? item,
    out PeekuError? error)
  {
    item = null;
    error = null;

    var win = Win32Windows.GetFocusedWindow();
    if (win is null)
    {
      return Fail(PeekuErrorCode.WindowNotFound, "No foreground window.", out error);
    }

    return TryCreateForHwnd(interop, win.HwndHex, out item, out error);
  }

  private static bool TryCreateForQuery(
    IGraphicsCaptureItemInterop interop,
    WindowQuery query,
    CancellationToken ct,
    out GraphicsCaptureItem? item,
    out PeekuError? error)
  {
    item = null;
    error = null;

    var candidates = Win32Windows.ListWindows(
      new WindowsListRequest(
        TitleContains: query.TitleContains,
        ProcessName: query.ProcessName,
        Limit: 200),
      ct);

    var match = candidates.FirstOrDefault(w => query.ProcessId is null || w.ProcessId == query.ProcessId.Value);
    if (match is null)
    {
      return Fail(PeekuErrorCode.WindowNotFound, "No window matched query.", out error);
    }

    return TryCreateForHwnd(interop, match.HwndHex, out item, out error);
  }

  private static bool TryCreateForHwnd(
    IGraphicsCaptureItemInterop interop,
    string hwndHex,
    out GraphicsCaptureItem? item,
    out PeekuError? error)
  {
    item = null;
    error = null;

    if (!TryParseHwndHex(hwndHex, out var hwnd))
    {
      return Fail(PeekuErrorCode.InvalidArgument, "Invalid hwndHex.", out error, details: new { hwndHex });
    }

    var iid = typeof(GraphicsCaptureItem).GUID;
    item = interop.CreateForWindow(hwnd, ref iid);
    return true;
  }

  private static bool TryCreateForScreenIndex(
    IGraphicsCaptureItemInterop interop,
    int screenIndex,
    out GraphicsCaptureItem? item,
    out PeekuError? error)
  {
    item = null;
    error = null;

    var monitors = Win32Monitors.List();
    if (screenIndex < 0 || screenIndex >= monitors.Count)
    {
      return Fail(PeekuErrorCode.InvalidArgument, "Invalid screen index.", out error, details: new { screenIndex, count = monitors.Count });
    }

    var iid = typeof(GraphicsCaptureItem).GUID;
    item = interop.CreateForMonitor(monitors[screenIndex], ref iid);
    return true;
  }

  private static bool TryCreateForPrimaryMonitor(
    IGraphicsCaptureItemInterop interop,
    out GraphicsCaptureItem? item,
    out PeekuError? error)
  {
    item = null;
    error = null;

    var monitor = Win32Monitors.Primary();
    if (monitor == 0)
    {
      return Fail(PeekuErrorCode.Unavailable, "Primary monitor not found.", out error);
    }

    var iid = typeof(GraphicsCaptureItem).GUID;
    item = interop.CreateForMonitor(monitor, ref iid);
    return true;
  }

  private static IGraphicsCaptureItemInterop GetInterop()
  {
    var classId = default(nint);
    var factoryPtr = default(nint);

    try
    {
      var className = "Windows.Graphics.Capture.GraphicsCaptureItem";
      var hr = WindowsCreateString(className, className.Length, out classId);
      Marshal.ThrowExceptionForHR(hr);

      var iid = typeof(IGraphicsCaptureItemInterop).GUID;
      hr = RoGetActivationFactory(classId, ref iid, out factoryPtr);
      Marshal.ThrowExceptionForHR(hr);

      return (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factoryPtr);
    }
    finally
    {
      if (factoryPtr != 0)
      {
        _ = Marshal.Release(factoryPtr);
      }

      if (classId != 0)
      {
        _ = WindowsDeleteString(classId);
      }
    }
  }

  private static bool TryParseHwndHex(string hwndHex, out nint hwnd)
  {
    hwnd = 0;
    if (string.IsNullOrWhiteSpace(hwndHex))
    {
      return false;
    }

    var s = hwndHex.Trim();
    if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
    {
      s = s[2..];
    }

    if (!long.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
    {
      return false;
    }

    hwnd = unchecked((nint)value);
    return hwnd != 0;
  }

  private static bool Fail(PeekuErrorCode code, string message, out PeekuError? error, object? details = null)
  {
    error = PeekuErrors.Create(code, message, details);
    return false;
  }

  [ComImport]
  [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
  [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  private interface IGraphicsCaptureItemInterop
  {
    GraphicsCaptureItem CreateForWindow(nint window, [In] ref Guid iid);
    GraphicsCaptureItem CreateForMonitor(nint monitor, [In] ref Guid iid);
  }

  [DllImport("combase.dll")]
  private static extern int RoGetActivationFactory(nint activatableClassId, ref Guid iid, out nint factory);

  [DllImport("combase.dll", CharSet = CharSet.Unicode)]
  private static extern int WindowsCreateString(string sourceString, int length, out nint hstring);

  [DllImport("combase.dll")]
  private static extern int WindowsDeleteString(nint hstring);
}
