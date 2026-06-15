using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using peeku;
using System.Text.Json;
using Xunit;

namespace peeku.Core.Tests;

/// <summary>
/// Tests that an <see cref="ActionSelectionResolver"/> miss on the snapshot path attaches
/// <c>candidates</c> to the <see cref="PeekuError.Details"/> object.
/// </summary>
public sealed class CandidateHintsResolverTests
{
  private static Func<Target, UIA3Automation, CancellationToken, (AutomationElement? Root, string? Warning)>
    NullRoot() => (_, _, _) => (null, "No foreground window.");

  [Fact]
  public async Task Snapshot_miss_attaches_candidates_to_error_details()
  {
    // Build a synthetic snapshot with two named elements — neither matches the selector.
    var snapshotId = "snap-test-1";
    var el1 = new UiaElement(new ElementRef("ref1", snapshotId), Name: "Cancel", ControlType: "Button",
      Rect: new Rect(0, 0, 50, 20));
    var el2 = new UiaElement(new ElementRef("ref2", snapshotId), Name: "Help", ControlType: "Button",
      Rect: new Rect(60, 0, 50, 20));

    // Tree: root -> button1, button2 (flat).
    var root = new UiaNode(
      Element: new ElementRef("root", snapshotId),
      ControlType: "Window",
      Children: new[]
      {
        new UiaNode(new ElementRef("ref1", snapshotId), Name: "Cancel", ControlType: "Button"),
        new UiaNode(new ElementRef("ref2", snapshotId), Name: "Help", ControlType: "Button"),
      });

    var snapshot = new UiaSnapshotResult(
      Ok: true,
      Meta: Results.Meta(),
      SnapshotId: snapshotId,
      Root: root,
      Elements: new[] { el1, el2 });

    Task<UiaSnapshotResult> SnapshotAsync(UiaSnapshotRequest _, CancellationToken __)
      => Task.FromResult(snapshot);

    // Selector that will NOT match anything (looking for "OK" button, only "Cancel"/"Help" exist).
    var selector = new Selector("button[name=\"OK\"]");

    var res = await ActionSelectionResolver.ResolveAsync(
      SnapshotAsync,
      NullRoot(),
      element: null,
      selector: selector,
      target: new Target.FocusedWindow(),
      CancellationToken.None);

    Assert.False(res.Ok);
    Assert.NotNull(res.Error);
    Assert.Equal(PeekuErrors.Code(PeekuErrorCode.ElementNotFound), res.Error!.Code);

    // Details must carry a candidates list.
    Assert.NotNull(res.Error.Details);

    // Serialize to JSON to inspect the candidates field (Details is object? — JSON roundtrip is safe).
    var json = JsonSerializer.Serialize(res.Error.Details);
    using var doc = JsonDocument.Parse(json);

    Assert.True(doc.RootElement.TryGetProperty("candidates", out var candidatesEl),
      "Expected 'candidates' key in error details.");
    Assert.Equal(JsonValueKind.Array, candidatesEl.ValueKind);
    Assert.True(candidatesEl.GetArrayLength() > 0, "Expected at least one candidate.");
  }

  [Fact]
  public async Task Snapshot_miss_selector_no_name_filter_still_returns_candidates()
  {
    // Selector has no name filter — candidates should still be returned (no-intent path).
    var snapshotId = "snap-test-2";
    var el1 = new UiaElement(new ElementRef("ref1", snapshotId), Name: "SomeButton", ControlType: "Button",
      Rect: new Rect(0, 0, 80, 20));

    var root = new UiaNode(
      Element: new ElementRef("root", snapshotId),
      ControlType: "Window",
      Children: new[]
      {
        new UiaNode(new ElementRef("ref1", snapshotId), Name: "SomeButton", ControlType: "Button"),
      });

    var snapshot = new UiaSnapshotResult(
      Ok: true,
      Meta: Results.Meta(),
      SnapshotId: snapshotId,
      Root: root,
      Elements: new[] { el1 });

    Task<UiaSnapshotResult> SnapshotAsync(UiaSnapshotRequest _, CancellationToken __)
      => Task.FromResult(snapshot);

    // Selector with control type only — no filter, so no match (window != button).
    var selector = new Selector("edit");

    var res = await ActionSelectionResolver.ResolveAsync(
      SnapshotAsync,
      NullRoot(),
      element: null,
      selector: selector,
      target: new Target.FocusedWindow(),
      CancellationToken.None);

    Assert.False(res.Ok);
    Assert.Equal(PeekuErrors.Code(PeekuErrorCode.ElementNotFound), res.Error!.Code);

    var json = JsonSerializer.Serialize(res.Error!.Details);
    using var doc = JsonDocument.Parse(json);

    Assert.True(doc.RootElement.TryGetProperty("candidates", out var candidatesEl));
    Assert.Equal(JsonValueKind.Array, candidatesEl.ValueKind);
    // The one named element "SomeButton" should appear even without intent.
    Assert.True(candidatesEl.GetArrayLength() >= 1);
  }
}
