using peeku;
using Xunit;

namespace peeku.Core.Tests;

/// <summary>
/// Tests for the synthetic-keypress path. Validation and named-key resolution all run BEFORE any
/// SendInput / window focus, so these cases are safe to exercise headless (no desktop required).
/// </summary>
public sealed class KeyPressTests
{
  // ── Named-key VK resolution (reuses the HotkeyInputInjector VK table) ──────────────────────────

  [Theory]
  [InlineData("enter", 0x0D)]
  [InlineData("ENTER", 0x0D)]
  [InlineData("return", 0x0D)]
  [InlineData("tab", 0x09)]
  [InlineData("esc", 0x1B)]
  [InlineData("escape", 0x1B)]
  [InlineData("up", 0x26)]
  [InlineData("down", 0x28)]
  [InlineData("left", 0x25)]
  [InlineData("right", 0x27)]
  [InlineData("f1", 0x70)]
  [InlineData("f12", 0x7B)]
  [InlineData("a", (int)'A')]
  [InlineData("Z", (int)'Z')]
  [InlineData("0", (int)'0')]
  [InlineData("9", (int)'9')]
  [InlineData("space", 0x20)]
  [InlineData("pageup", 0x21)]
  public void TryResolveKey_ResolvesNamedKey_ToExpectedVk(string name, int expectedVk)
  {
    Assert.True(HotkeyInputInjector.TryResolveKey(name, out var key, out var error), error);
    Assert.Null(error);
    Assert.Equal((ushort)expectedVk, key.Vk);
  }

  [Theory]
  [InlineData("up")]
  [InlineData("down")]
  [InlineData("delete")]
  [InlineData("home")]
  public void TryResolveKey_ArrowAndNav_AreExtended(string name)
  {
    Assert.True(HotkeyInputInjector.TryResolveKey(name, out var key, out _));
    Assert.True(key.Extended);
  }

  [Theory]
  [InlineData("banana")]
  [InlineData("ctrlx")]
  [InlineData("")]
  [InlineData("   ")]
  public void TryResolveKey_Unknown_ReturnsError(string name)
  {
    Assert.False(HotkeyInputInjector.TryResolveKey(name, out _, out var error));
    Assert.False(string.IsNullOrWhiteSpace(error));
  }

  [Theory]
  [InlineData("ctrl")]
  [InlineData("shift")]
  [InlineData("alt")]
  [InlineData("win")]
  public void TryResolveKey_Modifier_IsRejected(string name)
  {
    // press takes discrete keys; a bare modifier should route the user to hotkey instead.
    Assert.False(HotkeyInputInjector.TryResolveKey(name, out _, out var error));
    Assert.Contains("modifier", error, System.StringComparison.OrdinalIgnoreCase);
  }

  // ── PressRequest validation (must fail before any keystroke) ───────────────────────────────────

  [Fact]
  public async Task PressAsync_EmptyKeys_ReturnsInvalidArgument()
  {
    var res = await new WindowsClient().PressAsync(new PressRequest(Keys: System.Array.Empty<string>()));
    AssertInvalidArgument(res);
  }

  [Fact]
  public async Task PressAsync_NullKeys_ReturnsInvalidArgument()
  {
    var res = await new WindowsClient().PressAsync(new PressRequest(Keys: null!));
    AssertInvalidArgument(res);
  }

  [Fact]
  public async Task PressAsync_UnknownKey_ReturnsInvalidArgument_NamingTheKey()
  {
    var res = await new WindowsClient().PressAsync(new PressRequest(Keys: new[] { "enter", "banana" }));
    AssertInvalidArgument(res);
    Assert.Contains("banana", res.Error!.Message, System.StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task PressAsync_CountBelowOne_ReturnsInvalidArgument()
  {
    var res = await new WindowsClient().PressAsync(new PressRequest(Keys: new[] { "enter" }, Count: 0));
    AssertInvalidArgument(res);
  }

  [Fact]
  public async Task PressAsync_NegativeDelay_ReturnsInvalidArgument()
  {
    var res = await new WindowsClient().PressAsync(new PressRequest(Keys: new[] { "enter" }, DelayMs: -1));
    AssertInvalidArgument(res);
  }

  [Fact]
  public async Task PressAsync_NegativeHold_ReturnsInvalidArgument()
  {
    var res = await new WindowsClient().PressAsync(new PressRequest(Keys: new[] { "enter" }, HoldMs: -5));
    AssertInvalidArgument(res);
  }

  private static void AssertInvalidArgument(ActionResult res)
  {
    Assert.False(res.Ok);
    Assert.NotNull(res.Error);
    Assert.Equal(PeekuErrors.Code(PeekuErrorCode.InvalidArgument), res.Error!.Code);
  }
}
