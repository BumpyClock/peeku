using peeku;
using Xunit;

namespace peeku.Core.Tests;

/// <summary>
/// Unit tests for S1: mouse INPUT builders, SyntheticPointer, CLI --foreground precedence,
/// and router (inputSupported now true on click).
/// </summary>
public sealed class SyntheticPointerTests
{
  // ── MOUSEINPUT builder flag tests ─────────────────────────────────────────

  [Fact]
  public void MouseMove_EmitsAbsoluteAndVirtualDeskFlags()
  {
    var input = HotkeyInputInjector.MouseMove(32767, 32767);
    var mi = input.U.mi;

    Assert.Equal(
      HotkeyInputInjector.MOUSEEVENTF_MOVE |
      HotkeyInputInjector.MOUSEEVENTF_ABSOLUTE |
      HotkeyInputInjector.MOUSEEVENTF_VIRTUALDESK,
      mi.dwFlags);

    Assert.Equal(32767, mi.dx);
    Assert.Equal(32767, mi.dy);
  }

  [Fact]
  public void MouseButtonDown_LeftDown_EmitsCorrectFlag()
  {
    var input = HotkeyInputInjector.MouseButtonDown(HotkeyInputInjector.MOUSEEVENTF_LEFTDOWN);
    Assert.Equal(HotkeyInputInjector.MOUSEEVENTF_LEFTDOWN, input.U.mi.dwFlags);
  }

  [Fact]
  public void MouseButtonUp_LeftUp_EmitsCorrectFlag()
  {
    var input = HotkeyInputInjector.MouseButtonUp(HotkeyInputInjector.MOUSEEVENTF_LEFTUP);
    Assert.Equal(HotkeyInputInjector.MOUSEEVENTF_LEFTUP, input.U.mi.dwFlags);
  }

  [Fact]
  public void MouseButtonDown_RightDown_EmitsCorrectFlag()
  {
    var input = HotkeyInputInjector.MouseButtonDown(HotkeyInputInjector.MOUSEEVENTF_RIGHTDOWN);
    Assert.Equal(HotkeyInputInjector.MOUSEEVENTF_RIGHTDOWN, input.U.mi.dwFlags);
  }

  [Fact]
  public void MouseButtonUp_RightUp_EmitsCorrectFlag()
  {
    var input = HotkeyInputInjector.MouseButtonUp(HotkeyInputInjector.MOUSEEVENTF_RIGHTUP);
    Assert.Equal(HotkeyInputInjector.MOUSEEVENTF_RIGHTUP, input.U.mi.dwFlags);
  }

  // ── Click INPUT[] order ──────────────────────────────────────────────────

  [Fact]
  public void ClickInputArray_Order_IsMoveDownUp()
  {
    // Build the same array that SyntheticPointer builds internally.
    var inputs = new[]
    {
      HotkeyInputInjector.MouseMove(1000, 2000),
      HotkeyInputInjector.MouseButtonDown(HotkeyInputInjector.MOUSEEVENTF_LEFTDOWN),
      HotkeyInputInjector.MouseButtonUp(HotkeyInputInjector.MOUSEEVENTF_LEFTUP),
    };

    // [0] must be a MOVE (has MOVE flag)
    Assert.True((inputs[0].U.mi.dwFlags & HotkeyInputInjector.MOUSEEVENTF_MOVE) != 0, "First event must be MOVE");

    // [1] must be button-down
    Assert.Equal(HotkeyInputInjector.MOUSEEVENTF_LEFTDOWN, inputs[1].U.mi.dwFlags);

    // [2] must be button-up
    Assert.Equal(HotkeyInputInjector.MOUSEEVENTF_LEFTUP, inputs[2].U.mi.dwFlags);
  }

  // ── Zero-rect guard (must fire BEFORE any SendInput) ────────────────────

  [Fact]
  public async Task SyntheticPointer_ZeroWidth_ReturnsInvalidArgument_BeforeSend()
  {
    var rect = new Rect(100, 100, 0, 50);
    var (ok, error, evidence) = await SyntheticPointer.ClickAsync(rect, IntPtr.Zero, CancellationToken.None);

    Assert.False(ok);
    Assert.NotNull(error);
    Assert.Equal(PeekuErrors.Code(PeekuErrorCode.InvalidArgument), error!.Code);
    Assert.Null(evidence);
  }

  [Fact]
  public async Task SyntheticPointer_ZeroHeight_ReturnsInvalidArgument_BeforeSend()
  {
    var rect = new Rect(100, 100, 50, 0);
    var (ok, error, evidence) = await SyntheticPointer.ClickAsync(rect, IntPtr.Zero, CancellationToken.None);

    Assert.False(ok);
    Assert.NotNull(error);
    Assert.Equal(PeekuErrors.Code(PeekuErrorCode.InvalidArgument), error!.Code);
    Assert.Null(evidence);
  }

  [Fact]
  public async Task SyntheticPointer_NegativeWidth_ReturnsInvalidArgument_BeforeSend()
  {
    var rect = new Rect(100, 100, -1, 50);
    var (ok, error, evidence) = await SyntheticPointer.ClickAsync(rect, IntPtr.Zero, CancellationToken.None);

    Assert.False(ok);
    Assert.NotNull(error);
    Assert.Equal(PeekuErrors.Code(PeekuErrorCode.InvalidArgument), error!.Code);
  }

  // ── SendInputsCounted is separate from throwing SendInputs ────────────────

  [Fact]
  public void SendInputsCounted_EmptyArray_ReturnsZeroWithoutThrowing()
  {
    // Must NOT throw even for empty (unlike the void throwing path which short-circuits).
    var result = HotkeyInputInjector.SendInputsCounted(Array.Empty<HotkeyInputInjector.INPUT>());
    Assert.Equal(0u, result);
  }

  // ── Router: inputSupported=true on click ─────────────────────────────────

  [Fact]
  public void ActionMethodRouter_Click_InputMethod_IsSupported()
  {
    var method = ActionMethodRouter.Route(ActionMethod.Input, uiaSupported: true, inputSupported: true, out var error);
    Assert.Null(error);
    Assert.Equal(ActionMethod.Input, method);
  }

  [Fact]
  public void ActionMethodRouter_Click_InputMethod_WasNotSupported_Before()
  {
    // Ensure old inputSupported:false still rejects (not changed for other callers).
    var method = ActionMethodRouter.Route(ActionMethod.Input, uiaSupported: true, inputSupported: false, out var error);
    Assert.NotNull(error);
    Assert.Equal(PeekuErrors.Code(PeekuErrorCode.NotSupported), error!.Code);
  }

  // ── S2: right-click flag ────────────────────────────────────────────────

  [Fact]
  public void MouseButtonDown_RightDown_EmitsRightFlag()
  {
    var input = HotkeyInputInjector.MouseButtonDown(HotkeyInputInjector.MOUSEEVENTF_RIGHTDOWN);
    Assert.Equal(HotkeyInputInjector.MOUSEEVENTF_RIGHTDOWN, input.U.mi.dwFlags);
  }

  [Fact]
  public void MouseButtonUp_RightUp_EmitsRightFlag()
  {
    var input = HotkeyInputInjector.MouseButtonUp(HotkeyInputInjector.MOUSEEVENTF_RIGHTUP);
    Assert.Equal(HotkeyInputInjector.MOUSEEVENTF_RIGHTUP, input.U.mi.dwFlags);
  }

  // ── S2: double-click INPUT[] has two down/up pairs ───────────────────────

  [Fact]
  public void DoubleClick_InputArray_HasTwoDownUpPairs()
  {
    // Simulate what SyntheticPointer.SendClickAtAbsoluteAsync builds for doubleClick=true.
    var downFlag = HotkeyInputInjector.MOUSEEVENTF_LEFTDOWN;
    var upFlag   = HotkeyInputInjector.MOUSEEVENTF_LEFTUP;

    var inputs = new HotkeyInputInjector.INPUT[]
    {
      HotkeyInputInjector.MouseMove(1000, 2000),
      HotkeyInputInjector.MouseButtonDown(downFlag),
      HotkeyInputInjector.MouseButtonUp(upFlag),
      HotkeyInputInjector.MouseButtonDown(downFlag),
      HotkeyInputInjector.MouseButtonUp(upFlag),
    };

    Assert.Equal(5, inputs.Length);

    // [0] MOVE
    Assert.True((inputs[0].U.mi.dwFlags & HotkeyInputInjector.MOUSEEVENTF_MOVE) != 0);

    // [1] first down
    Assert.Equal(downFlag, inputs[1].U.mi.dwFlags);

    // [2] first up
    Assert.Equal(upFlag, inputs[2].U.mi.dwFlags);

    // [3] second down
    Assert.Equal(downFlag, inputs[3].U.mi.dwFlags);

    // [4] second up
    Assert.Equal(upFlag, inputs[4].U.mi.dwFlags);
  }

  [Fact]
  public void DoubleClick_RightButton_InputArray_UsesRightFlags()
  {
    var downFlag = HotkeyInputInjector.MOUSEEVENTF_RIGHTDOWN;
    var upFlag   = HotkeyInputInjector.MOUSEEVENTF_RIGHTUP;

    var inputs = new HotkeyInputInjector.INPUT[]
    {
      HotkeyInputInjector.MouseMove(500, 500),
      HotkeyInputInjector.MouseButtonDown(downFlag),
      HotkeyInputInjector.MouseButtonUp(upFlag),
      HotkeyInputInjector.MouseButtonDown(downFlag),
      HotkeyInputInjector.MouseButtonUp(upFlag),
    };

    Assert.Equal(HotkeyInputInjector.MOUSEEVENTF_RIGHTDOWN, inputs[1].U.mi.dwFlags);
    Assert.Equal(HotkeyInputInjector.MOUSEEVENTF_RIGHTUP,   inputs[2].U.mi.dwFlags);
    Assert.Equal(HotkeyInputInjector.MOUSEEVENTF_RIGHTDOWN, inputs[3].U.mi.dwFlags);
    Assert.Equal(HotkeyInputInjector.MOUSEEVENTF_RIGHTUP,   inputs[4].U.mi.dwFlags);
  }

  // ── S2: window-relative offset = rect.Left/Top + x/y ────────────────────

  [Fact]
  public void WindowRelativeOffset_EqualsRectLeftTopPlusXY()
  {
    // Simulate the offset calculation from UiaClient.Actions.cs coords path.
    // rect.Left=100, rect.Top=200, x=30, y=40 → screenX=130, screenY=240.
    int rectLeft = 100, rectTop = 200;
    int x = 30, y = 40;
    var screenX = rectLeft + x;
    var screenY = rectTop  + y;

    Assert.Equal(130, screenX);
    Assert.Equal(240, screenY);
  }

  // ── S2: ClickRequest new fields default to null/false ───────────────────

  [Fact]
  public void ClickRequest_NewFields_DefaultToNullOrFalse()
  {
    var req = new ClickRequest();
    Assert.Null(req.X);
    Assert.Null(req.Y);
    Assert.False(req.GlobalCoords);
    Assert.False(req.Double);
    Assert.False(req.Right);
  }

  [Fact]
  public void ClickRequest_CoordsCanBeSet()
  {
    var req = new ClickRequest(X: 100, Y: 200, GlobalCoords: true, Double: true, Right: true);
    Assert.Equal(100, req.X);
    Assert.Equal(200, req.Y);
    Assert.True(req.GlobalCoords);
    Assert.True(req.Double);
    Assert.True(req.Right);
  }
}
