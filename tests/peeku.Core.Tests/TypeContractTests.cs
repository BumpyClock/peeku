using peeku;
using Xunit;

namespace peeku.Core.Tests;

/// <summary>
/// Contract tests for TypeRequest new fields (Method/Foreground) and the Input
/// route's clean-failure behaviour on an unresolvable target.
/// </summary>
public sealed class TypeContractTests
{
  // ── TypeRequest defaults ──────────────────────────────────────────────────

  [Fact]
  public void TypeRequest_Default_Method_IsAuto()
  {
    var req = new TypeRequest();
    Assert.Equal(ActionMethod.Auto, req.Method);
  }

  [Fact]
  public void TypeRequest_Default_Foreground_IsFalse()
  {
    var req = new TypeRequest();
    Assert.False(req.Foreground);
  }

  [Fact]
  public void TypeRequest_RoundTrip_Method_Input_And_Foreground_True()
  {
    var req = new TypeRequest(
      Text: "hello",
      Method: ActionMethod.Input,
      Foreground: true);

    Assert.Equal("hello", req.Text);
    Assert.Equal(ActionMethod.Input, req.Method);
    Assert.True(req.Foreground);
  }

  [Fact]
  public void TypeRequest_ExistingDefaults_Unchanged()
  {
    // Additive change — existing defaults must stay intact.
    var req = new TypeRequest();
    Assert.Null(req.Element);
    Assert.Null(req.Selector);
    Assert.Null(req.Target);
    Assert.Equal("", req.Text);
    Assert.True(req.Append);
    Assert.Null(req.DelayMs);
  }

  // ── Input route clean failure on unresolvable target ─────────────────────

  [Fact]
  public async Task TypeAsync_InputRoute_UnresolvableTarget_ReturnsNotFound()
  {
    // A window title that definitely does not exist → WindowNotFound or ElementNotFound.
    var result = await new WindowsClient().TypeAsync(
      new TypeRequest(
        Target: new Target.WindowByQuery(
          new WindowQuery(TitleContains: "definitely-no-such-window-xyz-12345")),
        Text: "x",
        Method: ActionMethod.Input),
      CancellationToken.None);

    Assert.False(result.Ok);
    Assert.NotNull(result.Error);

    // Accept either WindowNotFound or ElementNotFound — the Input route must fail
    // cleanly before any SendInput when the target cannot be resolved.
    var code = result.Error!.Code;
    var windowNotFound = PeekuErrors.Code(PeekuErrorCode.WindowNotFound);
    var elementNotFound = PeekuErrors.Code(PeekuErrorCode.ElementNotFound);
    Assert.True(
      code == windowNotFound || code == elementNotFound,
      $"Expected WindowNotFound or ElementNotFound, got: {code}");
  }
}
