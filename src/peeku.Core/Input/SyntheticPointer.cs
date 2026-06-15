namespace peeku;

/// <summary>
/// Orchestrates a synthetic mouse click via <c>SendInput</c>.
/// Background (UIA-pattern) actions are the default; call this only when
/// <see cref="ClickRequest.Foreground"/> is <c>true</c> (or implied by <c>--method input</c>).
///
/// Contract:
/// <list type="bullet">
///   <item>Captures prior foreground window via <see cref="WindowFocusScope"/>.</item>
///   <item>Brings <paramref name="targetHwnd"/> to foreground with the hardened dance.</item>
///   <item>Settles 50 ms so focus lands inside the window.</item>
///   <item>Sends move → button-down → button-up as one <c>INPUT[]</c> (double = two down/up pairs).</item>
///   <item>Disposes scope (restores prior foreground), whether click succeeded or not.</item>
/// </list>
/// </summary>
internal static class SyntheticPointer
{
  private const string ElevationHint =
    "SendInput delivered 0 events; target may be elevated (higher integrity level). " +
    "Run peeku elevated to drive elevated windows.";

  /// <summary>
  /// Performs a left-click at the centre of <paramref name="rect"/> in a <c>WindowFocusScope</c>.
  /// Returns a completed <see cref="ActionResult"/>; never throws.
  /// </summary>
  internal static Task<(bool Ok, PeekuError? Error, object? Evidence)> ClickAsync(
    Rect rect,
    IntPtr targetHwnd,
    CancellationToken ct)
    => ClickAsync(rect, targetHwnd, right: false, doubleClick: false, ct);

  /// <summary>
  /// Performs a synthetic click at the centre of <paramref name="rect"/>.
  /// <paramref name="right"/> selects right-button; <paramref name="doubleClick"/> sends two down/up
  /// pairs in a single <c>INPUT[]</c> (time=0, within <c>GetDoubleClickTime()</c>, no sleep needed).
  /// </summary>
  internal static async Task<(bool Ok, PeekuError? Error, object? Evidence)> ClickAsync(
    Rect rect,
    IntPtr targetHwnd,
    bool right,
    bool doubleClick,
    CancellationToken ct)
  {
    // Guard: zero or degenerate rect must fail before any SendInput.
    if (rect.Width <= 0 || rect.Height <= 0)
    {
      return (false,
        PeekuErrors.Create(
          PeekuErrorCode.InvalidArgument,
          $"Element rect is zero or offscreen (width={rect.Width}, height={rect.Height}); cannot compute click centre."),
        null);
    }

    var cx = (int)Math.Round(rect.X + rect.Width  / 2.0);
    var cy = (int)Math.Round(rect.Y + rect.Height / 2.0);

    return await SendClickAtAbsoluteAsync(cx, cy, targetHwnd, right, doubleClick, ct).ConfigureAwait(false);
  }

  /// <summary>
  /// Performs a synthetic click at explicit screen-absolute physical pixel coordinates.
  /// Skips element resolution entirely — the canvas escape hatch.
  /// </summary>
  internal static Task<(bool Ok, PeekuError? Error, object? Evidence)> ClickAtPointAsync(
    int screenX,
    int screenY,
    IntPtr targetHwnd,
    bool right,
    bool doubleClick,
    CancellationToken ct)
    => SendClickAtAbsoluteAsync(screenX, screenY, targetHwnd, right, doubleClick, ct);

  private static async Task<(bool Ok, PeekuError? Error, object? Evidence)> SendClickAtAbsoluteAsync(
    int screenX,
    int screenY,
    IntPtr targetHwnd,
    bool right,
    bool doubleClick,
    CancellationToken ct)
  {
    var (ax, ay) = Win32Screen.ToAbsolute(screenX, screenY);

    var downFlag = right ? HotkeyInputInjector.MOUSEEVENTF_RIGHTDOWN : HotkeyInputInjector.MOUSEEVENTF_LEFTDOWN;
    var upFlag   = right ? HotkeyInputInjector.MOUSEEVENTF_RIGHTUP   : HotkeyInputInjector.MOUSEEVENTF_LEFTUP;

    HotkeyInputInjector.INPUT[] inputs;
    if (doubleClick)
    {
      // Two down/up pairs in ONE INPUT[]; time=0 on all events keeps inter-event gap
      // within GetDoubleClickTime() — the OS double-click detector fires on the second
      // button-down when the interval between them is below the system threshold.
      inputs =
      [
        HotkeyInputInjector.MouseMove(ax, ay),
        HotkeyInputInjector.MouseButtonDown(downFlag),
        HotkeyInputInjector.MouseButtonUp(upFlag),
        HotkeyInputInjector.MouseButtonDown(downFlag),
        HotkeyInputInjector.MouseButtonUp(upFlag),
      ];
    }
    else
    {
      inputs =
      [
        HotkeyInputInjector.MouseMove(ax, ay),
        HotkeyInputInjector.MouseButtonDown(downFlag),
        HotkeyInputInjector.MouseButtonUp(upFlag),
      ];
    }

    using var focusScope = new WindowFocusScope();

    if (!Win32Windows.BringToForegroundReliable(targetHwnd))
    {
      // Best-effort; proceed even if focus dance partially failed.
    }

    // Brief settle so the OS registers the window as foreground before events land.
    await Task.Delay(50, ct).ConfigureAwait(false);

    var injected = HotkeyInputInjector.SendInputsCounted(inputs);
    var expected = (uint)inputs.Length;

    if (injected == 0)
    {
      return (false,
        PeekuErrors.Create(PeekuErrorCode.Internal, ElevationHint),
        null);
    }

    if (injected < expected)
    {
      return (false,
        PeekuErrors.Create(
          PeekuErrorCode.Internal,
          $"SendInput partial delivery: sent={injected} expected={expected}."),
        null);
    }

    var evidence = new { injected, expected };
    return (true, null, evidence);
  }
}
