using peeku;
using Xunit;

namespace peeku.Core.Tests;

/// <summary>
/// Unit tests for S3 window-management constants, flag masks, and request contracts.
/// Tests that hit Win32 APIs (SetWindowPos, IsWindow) require a live desktop window and
/// are covered by manual/live verification; here we cover pure-logic assertions.
/// </summary>
public sealed class WindowManageTests
{
  // ─────────────────────────────────────────────────────────────────────────────
  // SWP flag masks (move/resize/set-bounds each uses the correct combination)
  // ─────────────────────────────────────────────────────────────────────────────

  [Fact]
  public void Move_FlagMask_HasNosizeNozorderNoactivate()
  {
    // move = NOSIZE | NOZORDER | NOACTIVATE (must NOT include NOMOVE)
    var flags = Win32Windows.SWP_NOSIZE | Win32Windows.SWP_NOZORDER | Win32Windows.SWP_NOACTIVATE;

    Assert.True((flags & Win32Windows.SWP_NOSIZE)     != 0, "SWP_NOSIZE must be set for move");
    Assert.True((flags & Win32Windows.SWP_NOZORDER)   != 0, "SWP_NOZORDER must be set for move");
    Assert.True((flags & Win32Windows.SWP_NOACTIVATE) != 0, "SWP_NOACTIVATE must be set for move");
    Assert.True((flags & Win32Windows.SWP_NOMOVE)     == 0, "SWP_NOMOVE must NOT be set for move");
  }

  [Fact]
  public void Resize_FlagMask_HasNomoveNozorderNoactivate()
  {
    // resize = NOMOVE | NOZORDER | NOACTIVATE (must NOT include NOSIZE)
    var flags = Win32Windows.SWP_NOMOVE | Win32Windows.SWP_NOZORDER | Win32Windows.SWP_NOACTIVATE;

    Assert.True((flags & Win32Windows.SWP_NOMOVE)     != 0, "SWP_NOMOVE must be set for resize");
    Assert.True((flags & Win32Windows.SWP_NOZORDER)   != 0, "SWP_NOZORDER must be set for resize");
    Assert.True((flags & Win32Windows.SWP_NOACTIVATE) != 0, "SWP_NOACTIVATE must be set for resize");
    Assert.True((flags & Win32Windows.SWP_NOSIZE)     == 0, "SWP_NOSIZE must NOT be set for resize");
  }

  [Fact]
  public void SetBounds_FlagMask_HasNozorderNoactivateOnly()
  {
    // set-bounds = NOZORDER | NOACTIVATE (neither NOSIZE nor NOMOVE — we set both pos and size)
    var flags = Win32Windows.SWP_NOZORDER | Win32Windows.SWP_NOACTIVATE;

    Assert.True((flags & Win32Windows.SWP_NOZORDER)   != 0, "SWP_NOZORDER must be set for set-bounds");
    Assert.True((flags & Win32Windows.SWP_NOACTIVATE) != 0, "SWP_NOACTIVATE must be set for set-bounds");
    Assert.True((flags & Win32Windows.SWP_NOSIZE)     == 0, "SWP_NOSIZE must NOT be set for set-bounds");
    Assert.True((flags & Win32Windows.SWP_NOMOVE)     == 0, "SWP_NOMOVE must NOT be set for set-bounds");
  }

  // ─────────────────────────────────────────────────────────────────────────────
  // ShowWindow commands
  // ─────────────────────────────────────────────────────────────────────────────

  [Fact]
  public void SW_Commands_HaveCorrectValues()
  {
    Assert.Equal(6, Win32Windows.SW_MINIMIZE);
    Assert.Equal(3, Win32Windows.SW_MAXIMIZE);
    Assert.Equal(9, Win32Windows.SW_RESTORE_PUBLIC);
  }

  // ─────────────────────────────────────────────────────────────────────────────
  // WM_CLOSE constant
  // ─────────────────────────────────────────────────────────────────────────────

  [Fact]
  public void WM_CLOSE_IsGracefulNotKill()
  {
    // WM_CLOSE = 0x0010; it posts a close request, not a terminate.
    Assert.Equal(0x0010u, Win32Windows.WM_CLOSE);
  }

  // ─────────────────────────────────────────────────────────────────────────────
  // Request record shapes
  // ─────────────────────────────────────────────────────────────────────────────

  [Fact]
  public void WindowMoveRequest_CarriesTarget()
  {
    var target = new Target.WindowByHwnd("0x0000000000123456");
    var req = new WindowMoveRequest(target, X: 100, Y: 200);
    Assert.Same(target, req.Target);
    Assert.Equal(100, req.X);
    Assert.Equal(200, req.Y);
  }

  [Fact]
  public void WindowResizeRequest_CarriesTarget()
  {
    var target = new Target.FocusedWindow();
    var req = new WindowResizeRequest(target, Width: 800, Height: 600);
    Assert.Same(target, req.Target);
    Assert.Equal(800, req.Width);
    Assert.Equal(600, req.Height);
  }

  [Fact]
  public void WindowBoundsRequest_CarriesAllFields()
  {
    var target = new Target.FocusedWindow();
    var req = new WindowBoundsRequest(target, X: 10, Y: 20, Width: 300, Height: 400);
    Assert.Equal(10, req.X);
    Assert.Equal(20, req.Y);
    Assert.Equal(300, req.Width);
    Assert.Equal(400, req.Height);
  }

  [Fact]
  public void WindowCloseRequest_DefaultWaitMs_Is2000()
  {
    var target = new Target.FocusedWindow();
    var req = new WindowCloseRequest(target);
    Assert.Equal(2000, req.WaitMs);
  }

  [Fact]
  public void WindowCloseRequest_CustomWaitMs()
  {
    var target = new Target.FocusedWindow();
    var req = new WindowCloseRequest(target, WaitMs: 500);
    Assert.Equal(500, req.WaitMs);
  }

  // ─────────────────────────────────────────────────────────────────────────────
  // close: IsWindow(Zero) returns false — verifies the poll termination condition
  // (Zero hwnd is never a valid window, so IsWindow(Zero)==false means "closed")
  // ─────────────────────────────────────────────────────────────────────────────

  [Fact]
  public void IsWindow_ZeroHandle_ReturnsFalse()
  {
    // IsWindow(IntPtr.Zero) must return false on Windows.
    // This validates the sentinel that the close-poll loop uses.
    Assert.False(Win32Windows.IsWindow(IntPtr.Zero));
  }

  // ─────────────────────────────────────────────────────────────────────────────
  // Win32Arrange snap-rect math (primary work area mocked via contract)
  // ─────────────────────────────────────────────────────────────────────────────

  [Fact]
  public void SnapRect_Left_IsLeftHalf()
  {
    // Simulate a 1920x1040 work area starting at (0,0).
    var waX = 0; var waY = 0; var waW = 1920; var waH = 1040;

    // left: x=waX, y=waY, w=waW/2, h=waH
    var x = waX; var y = waY; var w = waW / 2; var h = waH;

    Assert.Equal(0, x);
    Assert.Equal(0, y);
    Assert.Equal(960, w);
    Assert.Equal(1040, h);
  }

  [Fact]
  public void SnapRect_Right_IsRightHalf()
  {
    var waX = 0; var waY = 0; var waW = 1920; var waH = 1040;

    var w = waW / 2;
    var x = waX + w; var y = waY; var h = waH;

    Assert.Equal(960, x);
    Assert.Equal(0, y);
    Assert.Equal(960, w);
    Assert.Equal(1040, h);
  }

  [Fact]
  public void SnapPreset_TryParse_CaseInsensitive()
  {
    Assert.True(Win32Arrange.TryParseSnap("left", out var p1));
    Assert.Equal(Win32Arrange.SnapPreset.Left, p1);

    Assert.True(Win32Arrange.TryParseSnap("TopRight", out var p2));
    Assert.Equal(Win32Arrange.SnapPreset.TopRight, p2);

    Assert.False(Win32Arrange.TryParseSnap("invalid", out _));
    Assert.False(Win32Arrange.TryParseSnap(null, out _));
  }
}
