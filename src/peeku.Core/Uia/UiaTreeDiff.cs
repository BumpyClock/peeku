namespace peeku;

/// <summary>
/// Computes an additive tree diff between two UiaSnapshotResults, keyed by ElementRef.RefId.
/// FlaUI-free and desktop-free — operates only on the already-captured snapshot data.
/// </summary>
/// <example>
/// <code>
/// var delta = UiaTreeDiff.Diff(before, after);
/// </code>
/// </example>
internal static class UiaTreeDiff
{
  internal static UiaTreeDelta Diff(UiaSnapshotResult before, UiaSnapshotResult after)
  {
    if (before is null) throw new ArgumentNullException(nameof(before));
    if (after is null) throw new ArgumentNullException(nameof(after));

    var truncated = before.Meta.Warning?.Contains("truncated", StringComparison.OrdinalIgnoreCase) == true
      || after.Meta.Warning?.Contains("truncated", StringComparison.OrdinalIgnoreCase) == true;

    var beforeKeys = CollectKeys(before.Root);
    var afterKeys = CollectKeys(after.Root);

    // parent maps: child refId → parent refId (null for the root itself)
    var beforeParents = BuildParentMap(before.Root);
    var afterParents = BuildParentMap(after.Root);

    // added = keys in after that are not in before
    var addedKeys = afterKeys.Except(beforeKeys, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
    // removed = keys in before that are not in after
    var removedKeys = beforeKeys.Except(afterKeys, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);

    var addedRoots = CollapseToRoots(addedKeys, afterParents);
    var removedRoots = CollapseToRoots(removedKeys, beforeParents);

    // Build element lookup maps for ancestor chain construction
    var afterElementMap = BuildElementMap(after.Elements);
    var beforeElementMap = BuildElementMap(before.Elements);

    // Build node lookup maps for subtree attachment
    var afterNodeMap = BuildNodeMap(after.Root);
    var beforeNodeMap = BuildNodeMap(before.Root);

    var added = addedRoots
      .Select(refId =>
      {
        var node = afterNodeMap.TryGetValue(refId, out var n) ? n : new UiaNode(new ElementRef(refId));
        var ancestors = BuildAncestorChain(refId, afterParents, afterElementMap);
        return new UiaDeltaRoot(Subtree: node, Ancestors: ancestors);
      })
      .ToArray();

    var removed = removedRoots
      .Select(refId =>
      {
        var node = beforeNodeMap.TryGetValue(refId, out var n) ? n : new UiaNode(new ElementRef(refId));
        var ancestors = BuildAncestorChain(refId, beforeParents, beforeElementMap);
        return new UiaDeltaRoot(Subtree: node, Ancestors: ancestors);
      })
      .ToArray();

    return new UiaTreeDelta(Added: added, Removed: removed, Truncated: truncated);
  }

  // Collect all refIds in DFS order from a tree root.
  private static HashSet<string> CollectKeys(UiaNode? root)
  {
    var keys = new HashSet<string>(StringComparer.Ordinal);
    if (root is null) return keys;

    var stack = new Stack<UiaNode>();
    stack.Push(root);

    while (stack.Count > 0)
    {
      var node = stack.Pop();
      var refId = node.Element?.RefId;
      if (!string.IsNullOrEmpty(refId))
      {
        keys.Add(refId);
      }

      if (node.Children is null) continue;
      foreach (var child in node.Children)
      {
        stack.Push(child);
      }
    }

    return keys;
  }

  // Build a child→parent refId map from the tree. Root node maps to null.
  private static Dictionary<string, string?> BuildParentMap(UiaNode? root)
  {
    var map = new Dictionary<string, string?>(StringComparer.Ordinal);
    if (root is null) return map;

    var rootRefId = root.Element?.RefId ?? "";
    if (!string.IsNullOrEmpty(rootRefId))
    {
      map[rootRefId] = null;
    }

    var stack = new Stack<(UiaNode Node, string ParentRefId)>();
    stack.Push((root, rootRefId));

    while (stack.Count > 0)
    {
      var (node, parentRefId) = stack.Pop();
      if (node.Children is null) continue;
      foreach (var child in node.Children)
      {
        var childRefId = child.Element?.RefId;
        if (!string.IsNullOrEmpty(childRefId))
        {
          map[childRefId] = parentRefId;
          stack.Push((child, childRefId));
        }
      }
    }

    return map;
  }

  // CollapseToRoots: for a set of changed keys and their parent map, return only the
  // topmost changed nodes (those whose parent is NOT also in the changed set).
  private static List<string> CollapseToRoots(HashSet<string> changedKeys, Dictionary<string, string?> parentMap)
  {
    var roots = new List<string>(capacity: changedKeys.Count);
    var visited = new HashSet<string>(StringComparer.Ordinal);

    foreach (var key in changedKeys)
    {
      if (visited.Contains(key)) continue;

      // Walk up to find topmost ancestor that's still in the changed set.
      var cursor = key;
      var topmost = key;
      while (parentMap.TryGetValue(cursor, out var parent) && parent is not null)
      {
        if (!changedKeys.Contains(parent)) break;
        topmost = parent;
        cursor = parent;
      }

      // Mark everything on the path from key up to topmost as visited
      // so we don't emit sub-roots when the parent was already added.
      cursor = key;
      while (!string.Equals(cursor, topmost, StringComparison.Ordinal))
      {
        visited.Add(cursor);
        if (!parentMap.TryGetValue(cursor, out var p) || p is null) break;
        cursor = p;
      }

      visited.Add(topmost);

      if (!roots.Contains(topmost))
      {
        roots.Add(topmost);
      }
    }

    return roots;
  }

  // Build a flat refId→UiaElement lookup from the snapshot's elements list.
  private static Dictionary<string, UiaElement> BuildElementMap(IReadOnlyList<UiaElement> elements)
  {
    var map = new Dictionary<string, UiaElement>(elements.Count, StringComparer.Ordinal);
    foreach (var el in elements)
    {
      var refId = el.Element?.RefId;
      if (!string.IsNullOrEmpty(refId))
      {
        map.TryAdd(refId, el);
      }
    }

    return map;
  }

  // Build a flat refId→UiaNode lookup from the tree (DFS).
  private static Dictionary<string, UiaNode> BuildNodeMap(UiaNode? root)
  {
    var map = new Dictionary<string, UiaNode>(StringComparer.Ordinal);
    if (root is null) return map;

    var stack = new Stack<UiaNode>();
    stack.Push(root);

    while (stack.Count > 0)
    {
      var node = stack.Pop();
      var refId = node.Element?.RefId;
      if (!string.IsNullOrEmpty(refId))
      {
        map.TryAdd(refId, node);
      }

      if (node.Children is null) continue;
      foreach (var child in node.Children)
      {
        stack.Push(child);
      }
    }

    return map;
  }

  // Build the ancestor chain for a given refId (root-first, excluding the node itself).
  private static IReadOnlyList<UiaElement> BuildAncestorChain(
    string refId,
    Dictionary<string, string?> parentMap,
    Dictionary<string, UiaElement> elementMap)
  {
    var chain = new List<UiaElement>(capacity: 8);
    var cursor = refId;

    while (parentMap.TryGetValue(cursor, out var parentRefId) && parentRefId is not null)
    {
      if (elementMap.TryGetValue(parentRefId, out var parentEl))
      {
        chain.Add(parentEl);
      }

      cursor = parentRefId;
    }

    chain.Reverse(); // root-first
    return chain;
  }
}
