using System.Globalization;
using System.Runtime.InteropServices;

namespace peeku.Cli;

internal static class Win32WindowState
{
  internal static bool IsMinimized(string hwndHex)
  {
    if (!TryParseHwndHex(hwndHex, out var hwnd))
    {
      return false;
    }

    return IsIconic(hwnd);
  }

  internal static bool TryNormalizeHwndHex(string input, out string normalized)
  {
    if (!TryParseHwndHex(input, out var hwnd))
    {
      normalized = "";
      return false;
    }

    normalized = ToHwndHex(hwnd);
    return true;
  }

  private static bool TryParseHwndHex(string input, out nint hwnd)
  {
    hwnd = 0;
    if (string.IsNullOrWhiteSpace(input))
    {
      return false;
    }

    var s = input.Trim();
    if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
    {
      s = s[2..];
    }

    if (s.Length == 0)
    {
      return false;
    }

    if (!ulong.TryParse(s, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var v))
    {
      return false;
    }

    hwnd = new IntPtr(unchecked((long)v));
    return true;
  }

  private static string ToHwndHex(nint hwnd)
    => IntPtr.Size <= 4
      ? $"0x{hwnd.ToInt64():X8}"
      : $"0x{hwnd.ToInt64():X16}";

  [DllImport("user32.dll")]
  private static extern bool IsIconic(nint hWnd);
}

