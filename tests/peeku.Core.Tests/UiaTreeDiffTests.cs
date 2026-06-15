using peeku;
using Xunit;

namespace peeku.Core.Tests;

public sealed class UiaTreeDiffTests
{
  // ─── Helpers ──────────────────────────────────────────────────────────────

  private static UiaSnapshotResult MakeSnapshot(UiaNode root, string snapshotId = "s1")
  {
    var elements = new List<UiaElement>();
    CollectElements(root, elements);
    return new UiaSnapshotResult(
      Ok: true,
      Meta: new ResultMeta("t", DateTimeOffset.UnixEpoch, 1),
      SnapshotId: snapshotId,
      Root: root,
      Elements: elements);
  }

  private static UiaSnapshotResult MakeSnapshotTruncated(UiaNode root, string snapshotId = "s1")
  {
    var elements = new List<UiaElement>();
    CollectElements(root, elements);
    return new UiaSnapshotResult(
      Ok: true,
      Meta: new ResultMeta("t", DateTimeOffset.UnixEpoch, 1, Warning: "Snapshot truncated at MaxNodes=5000."),
      SnapshotId: snapshotId,
      Root: root,
      Elements: elements);
  }

  private static void CollectElements(UiaNode node, List<UiaElement> elements)
  {
    elements.Add(new UiaElement(Element: node.Element));
    if (node.Children is null) return;
    foreach (var child in node.Children)
    {
      CollectElements(child, elements);
    }
  }

  private static UiaNode Node(string refId, params UiaNode[] children)
    => new(
      Element: new ElementRef(refId),
      Children: children.Length == 0 ? null : children);

  // ─── Tests ────────────────────────────────────────────────────────────────

  [Fact]
  public void Diff_IdenticalSnapshots_EmptyDelta()
  {
    var root = Node("root", Node("child1"), Node("child2"));
    var before = MakeSnapshot(root, "s1");
    var after = MakeSnapshot(root, "s2");

    var delta = UiaTreeDiff.Diff(before, after);

    Assert.Empty(delta.Added);
    Assert.Empty(delta.Removed);
    Assert.False(delta.Truncated);
  }

  [Fact]
  public void Diff_NewNodeAdded_AppearsInAdded()
  {
    var before = MakeSnapshot(Node("root", Node("child1")), "s1");
    var after = MakeSnapshot(Node("root", Node("child1"), Node("child2")), "s2");

    var delta = UiaTreeDiff.Diff(before, after);

    Assert.Single(delta.Added);
    Assert.Empty(delta.Removed);
    Assert.Equal("child2", delta.Added[0].Subtree.Element.RefId);
    Assert.False(delta.Truncated);
  }

  [Fact]
  public void Diff_NodeRemoved_AppearsInRemoved()
  {
    var before = MakeSnapshot(Node("root", Node("child1"), Node("child2")), "s1");
    var after = MakeSnapshot(Node("root", Node("child1")), "s2");

    var delta = UiaTreeDiff.Diff(before, after);

    Assert.Empty(delta.Added);
    Assert.Single(delta.Removed);
    Assert.Equal("child2", delta.Removed[0].Subtree.Element.RefId);
    Assert.False(delta.Truncated);
  }

  [Fact]
  public void Diff_CollapseToRoots_ParentAndChildBothChanged_OnlyParentReturned()
  {
    // Before: root → [child1]
    // After: root → [child1, parent_new → [child_new]]
    // Both parent_new and child_new are new — should only emit parent_new (the topmost root)
    var before = MakeSnapshot(Node("root", Node("child1")), "s1");
    var after = MakeSnapshot(
      Node("root", Node("child1"), Node("parent_new", Node("child_new"))),
      "s2");

    var delta = UiaTreeDiff.Diff(before, after);

    Assert.Single(delta.Added);
    Assert.Equal("parent_new", delta.Added[0].Subtree.Element.RefId);
    Assert.Empty(delta.Removed);
  }

  [Fact]
  public void Diff_TruncatedFlag_SetWhenBeforeSnapshotTruncated()
  {
    var before = MakeSnapshotTruncated(Node("root", Node("child1")), "s1");
    var after = MakeSnapshot(Node("root", Node("child1")), "s2");

    var delta = UiaTreeDiff.Diff(before, after);

    Assert.True(delta.Truncated);
  }

  [Fact]
  public void Diff_TruncatedFlag_SetWhenAfterSnapshotTruncated()
  {
    var before = MakeSnapshot(Node("root", Node("child1")), "s1");
    var after = MakeSnapshotTruncated(Node("root", Node("child1")), "s2");

    var delta = UiaTreeDiff.Diff(before, after);

    Assert.True(delta.Truncated);
  }

  [Fact]
  public void Diff_TruncatedFlag_NotSetWhenNeitherTruncated()
  {
    var before = MakeSnapshot(Node("root"), "s1");
    var after = MakeSnapshot(Node("root"), "s2");

    var delta = UiaTreeDiff.Diff(before, after);

    Assert.False(delta.Truncated);
  }

  [Fact]
  public void Diff_AddedRoot_HasAncestorChain()
  {
    // root → parent → newChild
    // before: root → parent (no children)
    // after: root → parent → newChild
    var before = MakeSnapshot(Node("root", Node("parent")), "s1");
    var after = MakeSnapshot(Node("root", Node("parent", Node("newChild"))), "s2");

    var delta = UiaTreeDiff.Diff(before, after);

    Assert.Single(delta.Added);
    var added = delta.Added[0];
    Assert.Equal("newChild", added.Subtree.Element.RefId);
    // Ancestors: root, parent (root-first)
    Assert.Equal(2, added.Ancestors.Count);
    Assert.Equal("root", added.Ancestors[0].Element.RefId);
    Assert.Equal("parent", added.Ancestors[1].Element.RefId);
  }

  [Fact]
  public void Diff_MultipleIndependentAdded_AllReturned()
  {
    // before: root → [a, b]
    // after: root → [a, b, c, d]
    var before = MakeSnapshot(Node("root", Node("a"), Node("b")), "s1");
    var after = MakeSnapshot(Node("root", Node("a"), Node("b"), Node("c"), Node("d")), "s2");

    var delta = UiaTreeDiff.Diff(before, after);

    Assert.Equal(2, delta.Added.Count);
    var addedRefIds = delta.Added.Select(x => x.Subtree.Element.RefId).ToHashSet();
    Assert.Contains("c", addedRefIds);
    Assert.Contains("d", addedRefIds);
  }

  [Fact]
  public void Diff_NullArgs_ThrowsArgumentNullException()
  {
    var snap = MakeSnapshot(Node("root"), "s1");
    Assert.Throws<ArgumentNullException>(() => UiaTreeDiff.Diff(null!, snap));
    Assert.Throws<ArgumentNullException>(() => UiaTreeDiff.Diff(snap, null!));
  }

  [Fact]
  public void Diff_CollapseToRoots_ThreeLevelChainAllNew_OnlyRootReturned()
  {
    // a → b → c all new. Only 'a' should appear.
    var before = MakeSnapshot(Node("root"), "s1");
    var after = MakeSnapshot(Node("root", Node("a", Node("b", Node("c")))), "s2");

    var delta = UiaTreeDiff.Diff(before, after);

    Assert.Single(delta.Added);
    Assert.Equal("a", delta.Added[0].Subtree.Element.RefId);
  }
}
