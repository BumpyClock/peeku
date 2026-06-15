using peeku;
using Xunit;

namespace peeku.Core.Tests;

/// <summary>
/// Unit tests for <see cref="Win32Screen.ToAbsolute(VirtualScreenRect, int, int)"/>.
/// All tests use the pure overload (injected rect) so they are desktop-free.
/// </summary>
public sealed class Win32ScreenTests
{
  // Single-monitor primary starting at (0,0), 1920×1080.
  private static readonly VirtualScreenRect Primary = new(X: 0, Y: 0, Width: 1920, Height: 1080);

  // Dual-monitor with a second monitor left of primary: virtual origin at (-1920, 0), total 3840×1080.
  private static readonly VirtualScreenRect DualLeft = new(X: -1920, Y: 0, Width: 3840, Height: 1080);

  [Fact]
  public void TopLeft_of_virtualscreen_maps_to_0_0()
  {
    var (ax, ay) = Win32Screen.ToAbsolute(Primary, screenX: 0, screenY: 0);
    Assert.Equal(0, ax);
    Assert.Equal(0, ay);
  }

  [Fact]
  public void BottomRight_of_virtualscreen_maps_to_65535_65535()
  {
    // Far corner is (vsX + vsCx - 1, vsY + vsCy - 1).
    var (ax, ay) = Win32Screen.ToAbsolute(Primary, screenX: Primary.X + Primary.Width - 1, screenY: Primary.Y + Primary.Height - 1);
    Assert.Equal(65535, ax);
    Assert.Equal(65535, ay);
  }

  [Fact]
  public void Negative_origin_maps_leftmost_point_to_0_0()
  {
    // Virtual origin is -1920 on X; a point at exactly that X should map to 0.
    var (ax, _) = Win32Screen.ToAbsolute(DualLeft, screenX: -1920, screenY: 0);
    Assert.Equal(0, ax);
  }

  [Fact]
  public void Negative_origin_maps_primary_topleft_correctly()
  {
    // Primary starts at screenX=0 in a DualLeft setup where vsX=-1920, vsCx=3840.
    // ax = (0 - (-1920)) * 65535 / (3840 - 1) = 1920 * 65535 / 3839
    var expected = (int)Math.Round(1920.0 * 65535.0 / (3840 - 1));
    var (ax, _) = Win32Screen.ToAbsolute(DualLeft, screenX: 0, screenY: 0);
    Assert.Equal(expected, ax);
  }

  [Fact]
  public void Negative_origin_far_corner_maps_to_65535()
  {
    var farX = DualLeft.X + DualLeft.Width - 1;  // -1920 + 3840 - 1 = 1919
    var farY = DualLeft.Y + DualLeft.Height - 1; // 0 + 1080 - 1 = 1079
    var (ax, ay) = Win32Screen.ToAbsolute(DualLeft, screenX: farX, screenY: farY);
    Assert.Equal(65535, ax);
    Assert.Equal(65535, ay);
  }

  [Fact]
  public void Center_of_primary_single_monitor_rounds_correctly()
  {
    // The formula is: ax = round((x - vsX) * 65535 / (vsCx - 1))
    // For x=960, vsX=0, vsCx=1920: ax = round(960 * 65535 / 1919)
    var expectedX = (int)Math.Round(960.0 * 65535.0 / (Primary.Width - 1));
    var expectedY = (int)Math.Round(540.0 * 65535.0 / (Primary.Height - 1));
    var (ax, ay) = Win32Screen.ToAbsolute(Primary, screenX: 960, screenY: 540);
    Assert.Equal(expectedX, ax);
    Assert.Equal(expectedY, ay);
  }
}
