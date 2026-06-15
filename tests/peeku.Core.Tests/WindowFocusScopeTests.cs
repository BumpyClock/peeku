using peeku;
using Xunit;

namespace peeku.Core.Tests;

/// <summary>
/// Unit tests for <see cref="WindowFocusScope"/> using injected seam delegates so the tests
/// run without a real desktop.
/// </summary>
public sealed class WindowFocusScopeTests
{
  [Fact]
  public void Dispose_restores_captured_hwnd()
  {
    var captured = IntPtr.Zero;
    var fakeHwnd = new IntPtr(0xBEEF);

    var scope = new WindowFocusScope(
      getForeground: () => fakeHwnd,
      restore: hwnd => captured = hwnd,
      isWindow: _ => true);

    scope.Dispose();

    Assert.Equal(fakeHwnd, captured);
  }

  [Fact]
  public void Dispose_does_not_call_restore_when_prior_is_zero()
  {
    var restoreCalled = false;

    var scope = new WindowFocusScope(
      getForeground: () => IntPtr.Zero,
      restore: _ => restoreCalled = true,
      isWindow: _ => true);

    scope.Dispose();

    Assert.False(restoreCalled);
  }

  [Fact]
  public void Dispose_does_not_call_restore_when_window_no_longer_valid()
  {
    var restoreCalled = false;
    var fakeHwnd = new IntPtr(0xDEAD);

    var scope = new WindowFocusScope(
      getForeground: () => fakeHwnd,
      restore: _ => restoreCalled = true,
      isWindow: _ => false);  // window destroyed

    scope.Dispose();

    Assert.False(restoreCalled);
  }

  [Fact]
  public void Using_block_restores_on_exit()
  {
    var restoreCount = 0;
    var fakeHwnd = new IntPtr(0x1234);

    using (new WindowFocusScope(
      getForeground: () => fakeHwnd,
      restore: _ => restoreCount++,
      isWindow: _ => true))
    {
      // Scope body — no-op in test.
    }

    Assert.Equal(1, restoreCount);
  }

  [Fact]
  public void Dispose_swallows_restore_exception()
  {
    var fakeHwnd = new IntPtr(0xCAFE);

    var scope = new WindowFocusScope(
      getForeground: () => fakeHwnd,
      restore: _ => throw new InvalidOperationException("simulated restore failure"),
      isWindow: _ => true);

    // Must not throw.
    var ex = Record.Exception(() => scope.Dispose());
    Assert.Null(ex);
  }
}
