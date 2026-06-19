namespace peeku;

/// <summary>
/// Orchestrates synthetic UNICODE text injection via <c>SendInput</c> with <c>KEYEVENTF_UNICODE</c>.
/// Mirrors <see cref="SyntheticPointer"/> in structure and contract:
/// <list type="bullet">
///   <item>Captures prior foreground window via <see cref="WindowFocusScope"/>.</item>
///   <item>Brings <paramref name="targetHwnd"/> to foreground; settles 50 ms.</item>
///   <item>Click-focuses the resolved element rect (when provided) before sending keys.</item>
///   <item>Iterates UTF-16 code units of <paramref name="text"/> — surrogate halves each sent individually.</item>
///   <item>Detects <c>injected==0</c> → elevation hint; partial delivery → Internal error.</item>
///   <item>Touches zero FlaUI/COM after the first <c>await</c>. Never throws.</item>
/// </list>
/// </summary>
internal static class SyntheticText
{
  private const string ElevationHint =
    "SendInput delivered 0 events; target may be elevated (higher integrity level). " +
    "Run peeku elevated to drive elevated windows.";

  /// <summary>
  /// Injects <paramref name="text"/> as UNICODE keystrokes into <paramref name="targetHwnd"/>.
  /// When <paramref name="focusRect"/> is non-null, click-focuses the field before sending keys.
  /// </summary>
  internal static async Task<(bool Ok, PeekuError? Error, object? Evidence)> TypeUnicodeAsync(
    IntPtr targetHwnd,
    Rect? focusRect,
    string text,
    int delayMs,
    CancellationToken ct)
  {
    // Guard: empty text → InvalidArgument (before any SendInput).
    if (string.IsNullOrEmpty(text))
    {
      return (false,
        PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Text is required."),
        null);
    }

    // Capture everything we need before the first await — no FlaUI/COM after that point.
    var capturedHwnd = targetHwnd;
    var capturedFocusRect = focusRect;
    var capturedText = text;
    var capturedDelayMs = delayMs;

    using var focusScope = new WindowFocusScope();

    if (!Win32Windows.BringToForegroundReliable(capturedHwnd))
    {
      // Best-effort; proceed even if focus dance partially failed.
    }

    // Brief settle so the OS registers the window as foreground before events land.
    await Task.Delay(50, ct).ConfigureAwait(false);

    // Click-to-focus the resolved field rect when available (blocking review fix: element focus).
    // This ensures keystrokes land in the selector-resolved field, not whatever control
    // previously held focus inside the window.
    if (capturedFocusRect is not null)
    {
      var (clickOk, clickError, _) = await SyntheticPointer.ClickAsync(
        capturedFocusRect, capturedHwnd, right: false, doubleClick: false, ct).ConfigureAwait(false);
      if (!clickOk)
      {
        return (false, clickError, null);
      }
    }

    // Iterate UTF-16 code units (not runes) — surrogate halves are each sent individually.
    // KEYEVENTF_UNICODE delivers WM_CHAR with the raw code unit; apps that handle surrogate
    // pairs via WM_CHAR will reassemble the rune.
    uint injected = 0;
    uint expected = 0;

    for (var i = 0; i < capturedText.Length; i++)
    {
      ct.ThrowIfCancellationRequested();

      var codeUnit = (ushort)capturedText[i];
      var inputs = HotkeyInputInjector.BuildUnicodeChar(codeUnit);
      expected += (uint)inputs.Length;

      var sent = HotkeyInputInjector.SendInputsCounted(inputs);
      injected += sent;

      if (capturedDelayMs > 0)
      {
        await Task.Delay(capturedDelayMs, ct).ConfigureAwait(false);
      }
    }

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

    var evidence = new { injected, expected, chars = capturedText.Length };
    return (true, null, evidence);
  }
}
