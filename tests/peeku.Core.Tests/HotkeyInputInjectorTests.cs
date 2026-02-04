using peeku;
using Xunit;

namespace peeku.Core.Tests;

public sealed class HotkeyInputInjectorTests
{
  private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
  private const uint KEYEVENTF_KEYUP = 0x0002;

  [Theory]
  [InlineData("CTRL+SHIFT+S", 2, 1)]
  [InlineData("ALT+F4", 1, 1)]
  [InlineData("CTRL+ALT+DEL", 2, 1)]
  public void TryParse_Valid_Chords(string keys, int expectedMods, int expectedKeys)
  {
    Assert.True(HotkeyInputInjector.TryParse(keys, out var chord, out var error), error);
    Assert.Null(error);
    Assert.Equal(expectedMods, chord.Modifiers.Count);
    Assert.Equal(expectedKeys, chord.Keys.Count);
  }

  [Fact]
  public void BuildInputs_Order_IsExpected()
  {
    Assert.True(HotkeyInputInjector.TryParse("CTRL+SHIFT+S", out var chord, out var error), error);

    var inputs = HotkeyInputInjector.BuildInputs(chord);
    Assert.Equal(6, inputs.Length);

    Assert.Equal((ushort)'S', inputs[2].U.ki.wVk);
    Assert.Equal(0u, inputs[2].U.ki.dwFlags);

    Assert.Equal((ushort)'S', inputs[3].U.ki.wVk);
    Assert.True((inputs[3].U.ki.dwFlags & KEYEVENTF_KEYUP) != 0);

    Assert.True((inputs[0].U.ki.dwFlags & KEYEVENTF_KEYUP) == 0);
    Assert.True((inputs[1].U.ki.dwFlags & KEYEVENTF_KEYUP) == 0);
    Assert.True((inputs[4].U.ki.dwFlags & KEYEVENTF_KEYUP) != 0);
    Assert.True((inputs[5].U.ki.dwFlags & KEYEVENTF_KEYUP) != 0);
  }

  [Fact]
  public void TryParse_RCtrl_MarksExtended()
  {
    Assert.True(HotkeyInputInjector.TryParse("RCTRL+S", out var chord, out var error), error);

    var inputs = HotkeyInputInjector.BuildInputs(chord);
    Assert.True((inputs[0].U.ki.dwFlags & KEYEVENTF_EXTENDEDKEY) != 0);
  }

  [Theory]
  [InlineData("CTRL+SHIFT")]
  [InlineData("BANANA")]
  public void TryParse_Invalid_ReturnsError(string keys)
  {
    Assert.False(HotkeyInputInjector.TryParse(keys, out _, out var error));
    Assert.False(string.IsNullOrWhiteSpace(error));
  }
}
