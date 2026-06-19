using peeku;
using Xunit;

namespace peeku.Core.Tests;

/// <summary>
/// Headless unit tests for SyntheticText + HotkeyInputInjector.BuildUnicodeChar.
/// No SendInput calls and no desktop dependency — safe in any CI environment.
/// </summary>
public sealed class SyntheticTextTests
{
  private const uint KEYEVENTF_UNICODE = 0x0004;
  private const uint KEYEVENTF_KEYUP   = 0x0002;

  // ── BuildUnicodeChar: INPUT[] shape ──────────────────────────────────────

  [Fact]
  public void BuildUnicodeChar_Returns_TwoInputs()
  {
    var inputs = HotkeyInputInjector.BuildUnicodeChar((ushort)'A');
    Assert.Equal(2, inputs.Length);
  }

  [Fact]
  public void BuildUnicodeChar_Down_HasCorrectFlags_And_Zero_Vk()
  {
    var inputs = HotkeyInputInjector.BuildUnicodeChar((ushort)'A');
    var down = inputs[0].U.ki;

    Assert.Equal(0, down.wVk);
    Assert.Equal((ushort)'A', down.wScan);
    Assert.Equal(KEYEVENTF_UNICODE, down.dwFlags);
  }

  [Fact]
  public void BuildUnicodeChar_Up_HasCorrectFlags_And_Zero_Vk()
  {
    var inputs = HotkeyInputInjector.BuildUnicodeChar((ushort)'A');
    var up = inputs[1].U.ki;

    Assert.Equal(0, up.wVk);
    Assert.Equal((ushort)'A', up.wScan);
    Assert.Equal(KEYEVENTF_UNICODE | KEYEVENTF_KEYUP, up.dwFlags);
  }

  [Theory]
  [InlineData((ushort)0x0041)]  // 'A'
  [InlineData((ushort)0x00E9)]  // 'é'
  [InlineData((ushort)0xD83D)]  // high surrogate of U+1F600
  [InlineData((ushort)0xDE00)]  // low surrogate of U+1F600
  public void BuildUnicodeChar_ScanMatchesCodeUnit(ushort codeUnit)
  {
    var inputs = HotkeyInputInjector.BuildUnicodeChar(codeUnit);

    Assert.Equal(codeUnit, inputs[0].U.ki.wScan);
    Assert.Equal(codeUnit, inputs[1].U.ki.wScan);
  }

  // ── Surrogate pairs: astral chars yield 2 UTF-16 code units ──────────────

  [Fact]
  public void AstralChar_Emoji_HasTwoUtf16CodeUnits()
  {
    // U+1F600 GRINNING FACE encodes as a surrogate pair in UTF-16.
    const string emoji = "\U0001F600";

    Assert.Equal(2, emoji.Length);
    Assert.True(char.IsSurrogatePair(emoji[0], emoji[1]));
  }

  [Fact]
  public void BuildUnicodeChar_Works_For_Each_SurrogatePairHalf()
  {
    const string emoji = "\U0001F600";
    var high = (ushort)emoji[0];
    var low  = (ushort)emoji[1];

    var hiInputs = HotkeyInputInjector.BuildUnicodeChar(high);
    var loInputs = HotkeyInputInjector.BuildUnicodeChar(low);

    Assert.Equal(2, hiInputs.Length);
    Assert.Equal(2, loInputs.Length);

    // Down events carry the surrogate code unit in wScan.
    Assert.Equal(high, hiInputs[0].U.ki.wScan);
    Assert.Equal(low,  loInputs[0].U.ki.wScan);

    // Both halves use KEYEVENTF_UNICODE for down, UNICODE|KEYUP for up.
    Assert.Equal(KEYEVENTF_UNICODE, hiInputs[0].U.ki.dwFlags);
    Assert.Equal(KEYEVENTF_UNICODE | KEYEVENTF_KEYUP, hiInputs[1].U.ki.dwFlags);
    Assert.Equal(KEYEVENTF_UNICODE, loInputs[0].U.ki.dwFlags);
    Assert.Equal(KEYEVENTF_UNICODE | KEYEVENTF_KEYUP, loInputs[1].U.ki.dwFlags);
  }

  // ── Empty-text guard: returns InvalidArgument before any SendInput ────────

  [Fact]
  public async Task TypeUnicodeAsync_EmptyText_ReturnsInvalidArgument()
  {
    var (ok, error, evidence) = await SyntheticText.TypeUnicodeAsync(
      IntPtr.Zero, null, "", 0, CancellationToken.None);

    Assert.False(ok);
    Assert.NotNull(error);
    Assert.Equal(PeekuErrors.Code(PeekuErrorCode.InvalidArgument), error!.Code);
    Assert.Null(evidence);
  }

  // Note: whitespace-only text (e.g. "   ") is VALID input — typing spaces is legitimate, so the
  // guard is IsNullOrEmpty (length 0), NOT IsNullOrWhiteSpace. No whitespace-rejection test here.
}
