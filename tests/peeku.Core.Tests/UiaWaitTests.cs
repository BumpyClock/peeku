using peeku;
using Xunit;

namespace peeku.Core.Tests;

public sealed class UiaWaitTests
{
  [Fact]
  public async Task WaitAsync_Finds_Element_When_It_Appears()
  {
    var selector = new Selector("window[name=\"Main\"]/edit[automationId=\"Input\"]");
    var req = new WaitRequest(selector, new Target.Desktop(), TimeSpan.FromMilliseconds(50));

    var s1 = Snapshot("s1", includeEdit: false);
    var s2 = Snapshot("s2", includeEdit: true);

    var queue = new Queue<UiaSnapshotResult>(new[] { s1, s2 });
    var calls = 0;

    Task<UiaSnapshotResult> SnapshotAsync(UiaSnapshotRequest _, CancellationToken __)
    {
      calls++;
      return Task.FromResult(queue.Count > 0 ? queue.Dequeue() : s2);
    }

    var res = await UiaWait.WaitAsync(
      req,
      SnapshotAsync,
      pollInterval: TimeSpan.FromMilliseconds(50),
      delayAsync: static (_, _) => Task.CompletedTask,
      CancellationToken.None);

    Assert.True(res.Ok);
    Assert.True(res.Found);
    Assert.NotNull(res.Element);
    Assert.Equal("edit-s2", res.Element!.RefId);
    Assert.Equal("s2", res.Element.SnapshotId);
    Assert.Equal(2, calls);
  }

  [Fact]
  public async Task WaitAsync_Returns_NotFound_After_Timeout()
  {
    var selector = new Selector("window[name=\"Main\"]/edit[automationId=\"Input\"]");
    var req = new WaitRequest(selector, new Target.Desktop(), TimeSpan.FromMilliseconds(120));

    var snapshot = Snapshot("s1", includeEdit: false);
    var calls = 0;

    Task<UiaSnapshotResult> SnapshotAsync(UiaSnapshotRequest _, CancellationToken __)
    {
      calls++;
      return Task.FromResult(snapshot);
    }

    var res = await UiaWait.WaitAsync(
      req,
      SnapshotAsync,
      pollInterval: TimeSpan.FromMilliseconds(50),
      delayAsync: static (_, _) => Task.CompletedTask,
      CancellationToken.None);

    Assert.True(res.Ok);
    Assert.False(res.Found);
    Assert.Null(res.Element);
    Assert.Equal(3, calls);
  }

  private static UiaSnapshotResult Snapshot(string snapshotId, bool includeEdit)
  {
    var rootRef = new ElementRef($"root-{snapshotId}", snapshotId);
    var rootEl = new UiaElement(rootRef, Name: "Main", ControlType: "Window");

    var elements = new List<UiaElement>(capacity: 4) { rootEl };

    var children = default(IReadOnlyList<UiaNode>);
    if (includeEdit)
    {
      var editRef = new ElementRef($"edit-{snapshotId}", snapshotId);
      var editNode = new UiaNode(editRef, Name: "Input", ControlType: "Edit");
      children = new[] { editNode };

      elements.Add(new UiaElement(
        editRef,
        Name: "Input",
        ControlType: "Edit",
        AutomationId: "Input"));
    }

    var rootNode = new UiaNode(
      rootRef,
      Name: "Main",
      ControlType: "Window",
      Children: children);

    return new UiaSnapshotResult(
      Ok: true,
      Meta: Results.Meta("test"),
      SnapshotId: snapshotId,
      Root: rootNode,
      Elements: elements);
  }
}

