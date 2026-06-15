using System.Text.Json;
using peeku;
using Xunit;

namespace peeku.Core.Tests;

/// <summary>
/// Construction/validation tests for windows-focus. Actually bringing a window to the foreground
/// needs a live desktop, so the success path is covered by live verification; here we assert the
/// request shape and the batch arg-reading wire up correctly.
/// </summary>
public sealed class WindowFocusTests
{
  [Fact]
  public void WindowFocusRequest_CarriesTarget()
  {
    var target = new Target.WindowByHwnd("0x0000000000123456");
    var req = new WindowFocusRequest(target);
    Assert.Same(target, req.Target);
  }

  [Fact]
  public void BatchArgs_WindowsFocus_ReadsTarget()
  {
    using var doc = JsonDocument.Parse("""{"target":{"kind":"hwnd","hwndHex":"0x0000000000123456"}}""");
    var ok = BatchArgs.TryReadTarget(doc.RootElement, out var target, out var error);

    Assert.True(ok, error);
    var hwnd = Assert.IsType<Target.WindowByHwnd>(target);
    Assert.Equal("0x0000000000123456", hwnd.HwndHex);
  }

  [Fact]
  public void BatchArgs_Press_ReadsKeysArray()
  {
    using var doc = JsonDocument.Parse("""{"keys":["tab","tab","enter"],"count":3}""");

    var keys = BatchArgs.ReadStringArray(doc.RootElement, "keys");
    var count = BatchArgs.ReadInt(doc.RootElement, "count");

    Assert.NotNull(keys);
    Assert.Equal(new[] { "tab", "tab", "enter" }, keys);
    Assert.Equal(3, count);
  }

  [Fact]
  public void BatchArgs_Press_AcceptsBareStringAsSingleKey()
  {
    using var doc = JsonDocument.Parse("""{"keys":"enter"}""");

    var keys = BatchArgs.ReadStringArray(doc.RootElement, "keys");

    Assert.NotNull(keys);
    Assert.Equal(new[] { "enter" }, keys);
  }
}
