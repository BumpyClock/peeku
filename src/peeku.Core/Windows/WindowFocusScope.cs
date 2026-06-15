namespace peeku;

/// <summary>
/// Captures the foreground window on construction and restores it on <see cref="Dispose"/>.
/// Use in a <c>using</c> block around any automation sequence that steals foreground focus.
/// Restore is best-effort: if the original window was closed by the time Dispose runs, the
/// call is silently skipped.
/// </summary>
internal sealed class WindowFocusScope : IDisposable
{
  private readonly IntPtr _prior;
  private readonly Action<IntPtr> _restore;
  private readonly Func<IntPtr, bool> _isWindow;

  /// <summary>
  /// Captures the current foreground window handle.
  /// </summary>
  internal WindowFocusScope()
    : this(
        Win32Windows.GetForegroundWindow,
        hwnd => Win32Windows.BringToForegroundReliable(hwnd),
        Win32Windows.IsWindow)
  {
  }

  /// <summary>
  /// Seam constructor for unit tests — injects the capture / restore / validate callbacks
  /// so tests run without a real desktop.
  /// </summary>
  internal WindowFocusScope(
    Func<IntPtr> getForeground,
    Action<IntPtr> restore,
    Func<IntPtr, bool> isWindow)
  {
    _restore  = restore;
    _isWindow = isWindow;
    _prior    = getForeground();
  }

  /// <summary>
  /// Restores the previously focused window, if it is still a valid window handle.
  /// </summary>
  public void Dispose()
  {
    if (_prior == IntPtr.Zero)
    {
      return;
    }

    try
    {
      if (_isWindow(_prior))
      {
        _restore(_prior);
      }
    }
    catch
    {
      // Best-effort: restore is not critical.
    }
  }
}
