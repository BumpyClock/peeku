namespace peeku;

/// <summary>
/// Snap-preset math using the PRIMARY monitor work area.
///
/// Limitation (P1b): uses SPI_GETWORKAREA which returns the PRIMARY monitor work area only.
/// Per-monitor snap (rcWork via GetMonitorInfo) is deferred to P2.
///
/// Snap math derived from public documentation:
/// https://docs.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfow
/// Work area is the desktop rectangle excluding toolbars/docks. Snap partitions it by halves/thirds.
/// </summary>
internal static class Win32Arrange
{
  internal enum SnapPreset
  {
    Left,
    Right,
    Top,
    Bottom,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    Maximize,
  }

  /// <summary>
  /// Returns the (x, y, width, height) rectangle for the given snap preset on the primary monitor.
  /// Returns false when the work area cannot be retrieved.
  /// </summary>
  internal static bool TryGetSnapRect(SnapPreset preset, out int x, out int y, out int w, out int h)
  {
    x = y = w = h = 0;

    if (!Win32Windows.TryGetPrimaryWorkArea(out var wa))
      return false;

    var waX = wa.Left;
    var waY = wa.Top;
    var waW = wa.Right - wa.Left;
    var waH = wa.Bottom - wa.Top;

    switch (preset)
    {
      case SnapPreset.Left:
        x = waX; y = waY; w = waW / 2; h = waH;
        break;
      case SnapPreset.Right:
        w = waW / 2; x = waX + w; y = waY; h = waH;
        break;
      case SnapPreset.Top:
        x = waX; y = waY; w = waW; h = waH / 2;
        break;
      case SnapPreset.Bottom:
        h = waH / 2; x = waX; y = waY + h; w = waW;
        break;
      case SnapPreset.TopLeft:
        x = waX; y = waY; w = waW / 2; h = waH / 2;
        break;
      case SnapPreset.TopRight:
        w = waW / 2; x = waX + w; y = waY; h = waH / 2;
        break;
      case SnapPreset.BottomLeft:
        h = waH / 2; x = waX; y = waY + h; w = waW / 2;
        break;
      case SnapPreset.BottomRight:
        w = waW / 2; h = waH / 2; x = waX + w; y = waY + h;
        break;
      case SnapPreset.Maximize:
        x = waX; y = waY; w = waW; h = waH;
        break;
      default:
        return false;
    }

    return true;
  }

  internal static bool TryParseSnap(string? raw, out SnapPreset preset)
  {
    preset = default;
    if (string.IsNullOrWhiteSpace(raw))
      return false;

    return Enum.TryParse(raw.Trim(), ignoreCase: true, out preset);
  }
}
