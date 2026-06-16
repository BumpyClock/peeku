using System.Runtime.InteropServices;

namespace peeku;

/// <summary>
/// Win32 <c>SetWinEventHook</c> backstop for foreground-window changes
/// (<c>EVENT_SYSTEM_FOREGROUND</c>). It complements UIA focus events: a window can become
/// foreground without a focusable child raising a UIA focus event, and WinEvents often arrive
/// sooner. Runs a dedicated STA message-pump thread because <c>WINEVENT_OUTOFCONTEXT</c>
/// delivery requires a message loop on the hooking thread; the callback fires on that thread.
/// The callback forwards only a sparse (hwnd, pid) pair — it never touches UIA/COM, so this
/// thread's apartment never conflicts with the UIA automation living elsewhere.
///
/// Lifecycle is leak-safe: <see cref="Dispose"/> posts <c>WM_QUIT</c>, joins the thread, and the
/// pump unhooks before exiting. The delegate passed to the OS is held in a field for the hook's
/// whole lifetime so the GC cannot collect it out from under the native callback (no pinning is
/// needed — <c>WINEVENT_OUTOFCONTEXT</c> runs the callback as managed code on the pump thread).
/// </summary>
internal sealed class WinEventHook : IDisposable
{
  private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
  private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
  private const uint WM_QUIT = 0x0012;
  private const uint PM_NOREMOVE = 0x0000;

  private readonly Action<nint, int> _onForeground;
  private readonly Thread _thread;
  private readonly WinEventDelegate _proc; // held in a field so the GC keeps it alive for the hook lifetime
  private readonly ManualResetEventSlim _ready = new(initialState: false);
  private uint _threadId;
  private nint _hook;
  private int _disposed;

  public WinEventHook(Action<nint, int> onForeground)
  {
    _onForeground = onForeground;
    _proc = WinEventProc; // one stable instance: the same delegate is registered and disposed
    _thread = new Thread(PumpLoop)
    {
      IsBackground = true,
      Name = "peeku-winevent-foreground",
    };
    _thread.SetApartmentState(ApartmentState.STA);
    _thread.Start();

    // Block until the hook is installed (or failed) so we don't miss events at the seam and so
    // _threadId + the message queue exist before any Dispose can PostThreadMessage to us.
    _ready.Wait();
  }

  private void PumpLoop()
  {
    _threadId = GetCurrentThreadId();

    // Force the thread message queue into existence before signalling ready, otherwise a
    // PostThreadMessage from Dispose could race ahead of the first GetMessage and be lost.
    _ = PeekMessage(out _, nint.Zero, 0, 0, PM_NOREMOVE);

    _hook = SetWinEventHook(
      EVENT_SYSTEM_FOREGROUND,
      EVENT_SYSTEM_FOREGROUND,
      nint.Zero,
      _proc,
      idProcess: 0,
      idThread: 0,
      WINEVENT_OUTOFCONTEXT);

    _ready.Set();

    if (_hook == nint.Zero)
    {
      return; // hook failed; nothing to pump. Dispose's Join returns at once.
    }

    // WINEVENT_OUTOFCONTEXT posts the callbacks to this queue. GetMessage returns 0 on WM_QUIT
    // (posted by Dispose) and -1 on error; either breaks the loop.
    while (GetMessage(out _, nint.Zero, 0, 0) > 0)
    {
    }

    UnhookWinEvent(_hook);
    _hook = nint.Zero;
  }

  private void WinEventProc(
    nint hWinEventHook,
    uint eventType,
    nint hwnd,
    int idObject,
    int idChild,
    uint dwEventThread,
    uint dwmsEventTime)
  {
    // Foreground events target the window itself: OBJID_WINDOW (0) / CHILDID_SELF (0).
    if (hwnd == nint.Zero || idObject != 0 || idChild != 0)
    {
      return;
    }

    _ = GetWindowThreadProcessId(hwnd, out var pid);

    try
    {
      _onForeground(hwnd, (int)pid);
    }
    catch
    {
      // A faulting sink must never tear down the pump thread.
    }
  }

  public void Dispose()
  {
    if (Interlocked.Exchange(ref _disposed, 1) != 0)
    {
      return;
    }

    // Break GetMessage on the pump thread; the pump then unhooks and exits. _ready.Wait in the
    // ctor guarantees the queue exists by now, so this post is delivered (not raced away).
    if (_threadId != 0)
    {
      _ = PostThreadMessage(_threadId, WM_QUIT, nint.Zero, nint.Zero);
    }

    _thread.Join(TimeSpan.FromSeconds(2));
    _ready.Dispose();
  }

  private delegate void WinEventDelegate(
    nint hWinEventHook,
    uint eventType,
    nint hwnd,
    int idObject,
    int idChild,
    uint dwEventThread,
    uint dwmsEventTime);

  [DllImport("user32.dll", SetLastError = true)]
  private static extern nint SetWinEventHook(
    uint eventMin,
    uint eventMax,
    nint hmodWinEventProc,
    WinEventDelegate lpfnWinEventProc,
    uint idProcess,
    uint idThread,
    uint dwFlags);

  [DllImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool UnhookWinEvent(nint hWinEventHook);

  [DllImport("user32.dll")]
  private static extern int GetMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

  [DllImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool PeekMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

  [DllImport("user32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool PostThreadMessage(uint idThread, uint Msg, nint wParam, nint lParam);

  [DllImport("user32.dll")]
  private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

  [DllImport("kernel32.dll")]
  private static extern uint GetCurrentThreadId();

  [StructLayout(LayoutKind.Sequential)]
  private struct MSG
  {
    public nint Hwnd;
    public uint Message;
    public nint WParam;
    public nint LParam;
    public uint Time;
    public int PtX;
    public int PtY;
  }
}
