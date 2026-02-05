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

    nint factoryPtr = 0;
    try
    {
      ct.ThrowIfCancellationRequested();

      factoryPtr = GetInteropPtr();
      var interop = new GraphicsCaptureItemInterop(factoryPtr);

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
        details: new { exception = ex.GetType().FullName, ex.Message, ex.HResult, exceptionString = ex.ToString() });
    }
    finally
    {
      if (factoryPtr != 0)
      {
        _ = Marshal.Release(factoryPtr);
      }
    }
  }

  private static bool TryCreateForFocusedWindow(
    GraphicsCaptureItemInterop interop,
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
    GraphicsCaptureItemInterop interop,
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
    GraphicsCaptureItemInterop interop,
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

    var iid = IGraphicsCaptureItemIid;
    var hr = interop.CreateForWindow(hwnd, ref iid, out var itemPtr);
    if (hr < 0)
    {
      var hrHex = $"0x{(hr & 0xFFFFFFFF):X8}";
      var hrMessage = Marshal.GetExceptionForHR(hr)?.Message;
      return Fail(PeekuErrorCode.Internal, "CreateForWindow failed.", out error, details: new { hwndHex, hr, hrHex, hrMessage, iid = iid.ToString("D") });
    }

    try
    {
      if (itemPtr == 0)
      {
        return Fail(PeekuErrorCode.Internal, "CreateForWindow returned null item.", out error, details: new { hwndHex, hr });
      }

      item = global::WinRT.MarshalInspectable<GraphicsCaptureItem>.FromAbi(itemPtr);

      return true;
    }
    finally
    {
      if (itemPtr != 0)
      {
        global::WinRT.MarshalInspectable<GraphicsCaptureItem>.DisposeAbi(itemPtr);
      }
    }
  }

  private static bool TryCreateForScreenIndex(
    GraphicsCaptureItemInterop interop,
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

    var iid = IGraphicsCaptureItemIid;
    var hr = interop.CreateForMonitor(monitors[screenIndex], ref iid, out var itemPtr);
    if (hr < 0)
    {
      var hrHex = $"0x{(hr & 0xFFFFFFFF):X8}";
      var hrMessage = Marshal.GetExceptionForHR(hr)?.Message;
      return Fail(PeekuErrorCode.Internal, "CreateForMonitor failed.", out error, details: new { screenIndex, hr, hrHex, hrMessage });
    }

    try
    {
      item = Marshal.GetObjectForIUnknown(itemPtr) as GraphicsCaptureItem;
      if (item is null)
      {
        return Fail(PeekuErrorCode.Internal, "CreateForMonitor returned null.", out error, details: new { screenIndex });
      }

      return true;
    }
    finally
    {
      if (itemPtr != 0)
      {
        _ = Marshal.Release(itemPtr);
      }
    }
  }

  private static bool TryCreateForPrimaryMonitor(
    GraphicsCaptureItemInterop interop,
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

    var iid = IGraphicsCaptureItemIid;
    var hr = interop.CreateForMonitor(monitor, ref iid, out var itemPtr);
    if (hr < 0)
    {
      var hrHex = $"0x{(hr & 0xFFFFFFFF):X8}";
      var hrMessage = Marshal.GetExceptionForHR(hr)?.Message;
      return Fail(PeekuErrorCode.Internal, "CreateForMonitor failed.", out error, details: new { monitor, hr, hrHex, hrMessage });
    }

    try
    {
      item = Marshal.GetObjectForIUnknown(itemPtr) as GraphicsCaptureItem;
      if (item is null)
      {
        return Fail(PeekuErrorCode.Internal, "CreateForMonitor returned null.", out error, details: new { monitor });
      }

      return true;
    }
    finally
    {
      if (itemPtr != 0)
      {
        _ = Marshal.Release(itemPtr);
      }
    }
  }

  private static nint GetInteropPtr()
  {
    var classId = default(nint);
    var factoryPtr = default(nint);

    try
    {
      var className = "Windows.Graphics.Capture.GraphicsCaptureItem";
      var hr = WindowsCreateString(className, className.Length, out classId);
      Marshal.ThrowExceptionForHR(hr);

      var iid = typeof(GraphicsCaptureItemInterop).GUID;
      hr = RoGetActivationFactory(classId, ref iid, out factoryPtr);
      Marshal.ThrowExceptionForHR(hr);

      return factoryPtr;
    }
    finally
    {
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

  // IID for Windows.Graphics.Capture.IGraphicsCaptureItem
  // Source: C++/WinRT generated header (guid_v<IGraphicsCaptureItem>).
  private static readonly Guid IGraphicsCaptureItemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

  [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
  private readonly struct GraphicsCaptureItemInterop
  {
    private readonly nint _thisPtr;

    internal GraphicsCaptureItemInterop(nint thisPtr)
    {
      _thisPtr = thisPtr;
    }

    internal int CreateForWindow(nint window, ref Guid iid, out nint result)
    {
      // Win32 interop factory: IUnknown vtable layout.
      var del = GetVtableDelegate<CreateForWindowDelegate>(3);
      return del(_thisPtr, window, ref iid, out result);
    }

    internal int CreateForMonitor(nint monitor, ref Guid iid, out nint result)
    {
      var del = GetVtableDelegate<CreateForMonitorDelegate>(4);
      return del(_thisPtr, monitor, ref iid, out result);
    }

    private TDelegate GetVtableDelegate<TDelegate>(int methodIndex) where TDelegate : Delegate
    {
      var vtbl = Marshal.ReadIntPtr(_thisPtr);
      var fnPtr = Marshal.ReadIntPtr(vtbl, methodIndex * IntPtr.Size);
      return Marshal.GetDelegateForFunctionPointer<TDelegate>(fnPtr);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateForWindowDelegate(nint thisPtr, nint window, [In] ref Guid iid, out nint result);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateForMonitorDelegate(nint thisPtr, nint monitor, [In] ref Guid iid, out nint result);
  }

  [DllImport("combase.dll")]
  private static extern int RoGetActivationFactory(nint activatableClassId, ref Guid iid, out nint factory);

  [DllImport("combase.dll", CharSet = CharSet.Unicode)]
  private static extern int WindowsCreateString(string sourceString, int length, out nint hstring);

  [DllImport("combase.dll")]
  private static extern int WindowsDeleteString(nint hstring);
}
