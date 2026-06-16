using peeku;
using Xunit;

namespace peeku.Core.Tests;

/// <summary>
/// Unit tests for WaitPredicates and the wait-routing logic.
/// These tests cover the pure/string conditions without live UIA (no AutomationElement needed)
/// and the backward-compat guarantee that a default WaitRequest uses Exists.
/// </summary>
public sealed class WaitPredicatesTests
{
  // ──────────────────────────────────────────────────────────
  // Backward-compat: default WaitRequest stays on Exists path
  // ──────────────────────────────────────────────────────────

  [Fact]
  public void WaitRequest_Default_Condition_Is_Exists()
  {
    var req = new WaitRequest(new Selector("button"), new Target.Desktop(), TimeSpan.FromSeconds(5));
    Assert.Equal(WaitCondition.Exists, req.Condition);
    Assert.Null(req.ExpectedValue);
  }

  [Fact]
  public void WaitRequest_Default_Does_Not_Route_To_Live_Path()
  {
    var req = new WaitRequest(new Selector("button"), new Target.Desktop(), TimeSpan.FromSeconds(5));
    // Exists and NotExists are the snapshot-path conditions.
    Assert.False(UiaLiveWait.RequiresLivePath(req.Condition));
  }

  [Fact]
  public void WaitRequest_NotExists_Routes_To_Live_Path()
  {
    var req = new WaitRequest(new Selector("button"), new Target.Desktop(), TimeSpan.FromSeconds(5), WaitCondition.NotExists);
    Assert.True(UiaLiveWait.RequiresLivePath(req.Condition));
  }

  // ──────────────────────────────────────────────────────────
  // RequiresLivePath: all conditions except Exists/NotExists
  // ──────────────────────────────────────────────────────────

  [Theory]
  [InlineData(WaitCondition.Enabled)]
  [InlineData(WaitCondition.Disabled)]
  [InlineData(WaitCondition.Visible)]
  [InlineData(WaitCondition.Hidden)]
  [InlineData(WaitCondition.Focused)]
  [InlineData(WaitCondition.ToggleOn)]
  [InlineData(WaitCondition.ToggleOff)]
  [InlineData(WaitCondition.Expanded)]
  [InlineData(WaitCondition.Collapsed)]
  [InlineData(WaitCondition.Selected)]
  [InlineData(WaitCondition.NotSelected)]
  [InlineData(WaitCondition.ValueEquals)]
  [InlineData(WaitCondition.ValueContains)]
  [InlineData(WaitCondition.NameEquals)]
  [InlineData(WaitCondition.NameContains)]
  public void RequiresLivePath_True_For_State_Conditions(WaitCondition condition)
  {
    Assert.True(UiaLiveWait.RequiresLivePath(condition));
  }

  [Fact]
  public void RequiresLivePath_False_Only_For_Exists()
  {
    // Only Exists uses the cheaper snapshot-poll path.
    Assert.False(UiaLiveWait.RequiresLivePath(WaitCondition.Exists));
    // NotExists goes live so the loop can invert absence.
    Assert.True(UiaLiveWait.RequiresLivePath(WaitCondition.NotExists));
  }

  // ──────────────────────────────────────────────────────────
  // String comparison logic (pure, no AutomationElement)
  // Test via BatchArgs.ReadWaitCondition (parsing round-trip)
  // ──────────────────────────────────────────────────────────

  [Theory]
  [InlineData("exists",        WaitCondition.Exists)]
  [InlineData("notExists",     WaitCondition.NotExists)]
  [InlineData("NOTEXISTS",     WaitCondition.NotExists)]
  [InlineData("enabled",       WaitCondition.Enabled)]
  [InlineData("disabled",      WaitCondition.Disabled)]
  [InlineData("visible",       WaitCondition.Visible)]
  [InlineData("hidden",        WaitCondition.Hidden)]
  [InlineData("focused",       WaitCondition.Focused)]
  [InlineData("toggleOn",      WaitCondition.ToggleOn)]
  [InlineData("toggleOff",     WaitCondition.ToggleOff)]
  [InlineData("expanded",      WaitCondition.Expanded)]
  [InlineData("collapsed",     WaitCondition.Collapsed)]
  [InlineData("selected",      WaitCondition.Selected)]
  [InlineData("notSelected",   WaitCondition.NotSelected)]
  [InlineData("valueEquals",   WaitCondition.ValueEquals)]
  [InlineData("valueContains", WaitCondition.ValueContains)]
  [InlineData("nameEquals",    WaitCondition.NameEquals)]
  [InlineData("nameContains",  WaitCondition.NameContains)]
  public void BatchArgs_ReadWaitCondition_Parses_All_Values(string raw, WaitCondition expected)
  {
    var json = System.Text.Json.JsonSerializer.SerializeToElement(
      new System.Collections.Generic.Dictionary<string, string> { ["condition"] = raw });
    var result = BatchArgs.ReadWaitCondition(json, "condition");
    Assert.Equal(expected, result);
  }

  [Fact]
  public void BatchArgs_ReadWaitCondition_Returns_Null_For_Unknown()
  {
    var json = System.Text.Json.JsonSerializer.SerializeToElement(
      new System.Collections.Generic.Dictionary<string, string> { ["condition"] = "bogus" });
    var result = BatchArgs.ReadWaitCondition(json, "condition");
    Assert.Null(result);
  }

  [Fact]
  public void BatchArgs_ReadWaitCondition_Returns_Null_When_Absent()
  {
    var json = System.Text.Json.JsonSerializer.SerializeToElement(
      new System.Collections.Generic.Dictionary<string, string>());
    var result = BatchArgs.ReadWaitCondition(json, "condition");
    Assert.Null(result);
  }

  // ──────────────────────────────────────────────────────────
  // WaitCondition enum has exactly the expected members
  // (regression guard — parity test catches schema drift)
  // ──────────────────────────────────────────────────────────

  [Fact]
  public void WaitCondition_Has_Expected_Members()
  {
    var names = Enum.GetNames<WaitCondition>();
    Assert.Equal(17, names.Length);
    Assert.Contains("Exists",        names);
    Assert.Contains("NotExists",     names);
    Assert.Contains("Enabled",       names);
    Assert.Contains("Disabled",      names);
    Assert.Contains("Visible",       names);
    Assert.Contains("Hidden",        names);
    Assert.Contains("Focused",       names);
    Assert.Contains("ToggleOn",      names);
    Assert.Contains("ToggleOff",     names);
    Assert.Contains("Expanded",      names);
    Assert.Contains("Collapsed",     names);
    Assert.Contains("Selected",      names);
    Assert.Contains("NotSelected",   names);
    Assert.Contains("ValueEquals",   names);
    Assert.Contains("ValueContains", names);
    Assert.Contains("NameEquals",    names);
    Assert.Contains("NameContains",  names);
  }

  // ──────────────────────────────────────────────────────────
  // UiaWait (snapshot path): Exists default — backward-compat
  // ──────────────────────────────────────────────────────────

  [Fact]
  public async Task UiaWait_Exists_Default_Returns_NotFound_When_Selector_Misses_And_No_Timeout()
  {
    // Verify the Exists (default) path still works correctly — no regression.
    var req = new WaitRequest(
      new Selector("does-not-exist"),
      new Target.Desktop(),
      TimeSpan.Zero);

    Assert.Equal(WaitCondition.Exists, req.Condition);
    Assert.False(UiaLiveWait.RequiresLivePath(req.Condition));

    var snapshot = EmptySnapshot("s1");
    var res = await UiaWait.WaitAsync(
      req,
      (_, _) => Task.FromResult(snapshot),
      pollInterval: TimeSpan.FromMilliseconds(1),
      delayAsync: static (_, _) => Task.CompletedTask,
      CancellationToken.None);

    Assert.True(res.Ok);
    Assert.False(res.Found); // selector misses, zero timeout → not found
  }

  // ──────────────────────────────────────────────────────────
  // Regression: a bare --timeout like "2000" parses as 2000 *days*, which overflows
  // CancellationTokenSource.CancelAfter (rejects > int.MaxValue ms). The live wait must
  // clamp the timeout instead of throwing ArgumentOutOfRangeException(delay).
  // ──────────────────────────────────────────────────────────

  [Fact]
  public async Task UiaLiveWait_Oversized_Timeout_Does_Not_Throw_ArgumentOutOfRange()
  {
    // 2000 days — far beyond int.MaxValue ms (~24.8 days), the exact crash trigger.
    var req = new WaitRequest(
      new Selector("//button"),
      new Target.Desktop(),
      TimeSpan.FromDays(2000),
      WaitCondition.Enabled);

    // resolveRoot returns null → loop exits immediately with WindowNotFound, but only AFTER
    // the timeout-clamp + CancelAfter setup runs. Pre-fix, CancelAfter threw before this point.
    var res = await UiaLiveWait.WaitAsync(
      req,
      (_, _, _) => new UiaLiveWait.RootResolution(null, "no root"),
      CancellationToken.None);

    // Must be a clean WindowNotFound, NOT an Internal "Wait failed" from ArgumentOutOfRangeException.
    Assert.False(res.Ok);
    Assert.NotNull(res.Error);
    Assert.Equal(PeekuErrors.Code(PeekuErrorCode.WindowNotFound), res.Error!.Code);
  }

  private static UiaSnapshotResult EmptySnapshot(string snapshotId)
  {
    var rootRef = new ElementRef($"root-{snapshotId}", snapshotId);
    var rootNode = new UiaNode(rootRef, Name: "Desktop", ControlType: "Pane");
    return new UiaSnapshotResult(
      Ok: true,
      Meta: Results.Meta("test"),
      SnapshotId: snapshotId,
      Root: rootNode,
      Elements: new[] { new UiaElement(rootRef, Name: "Desktop", ControlType: "Pane") });
  }
}
