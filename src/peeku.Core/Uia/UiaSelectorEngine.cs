namespace peeku;

internal static class UiaSelectorEngine
{
  internal static IReadOnlyList<SelectorSegment> Parse(string expr)
    => SelectorParser.Parse(expr);

  internal static IReadOnlyList<TNode> Select<TNode>(
    TNode root,
    IReadOnlyList<SelectorSegment> segments,
    int limit,
    Func<TNode, IReadOnlyList<TNode>> children,
    Func<TNode, string?> name,
    Func<TNode, string?> controlType,
    Func<TNode, string?> automationId,
    Func<TNode, string?> className,
    CancellationToken ct)
  {
    if (segments is null)
    {
      throw new ArgumentNullException(nameof(segments));
    }

    if (children is null)
    {
      throw new ArgumentNullException(nameof(children));
    }

    if (name is null)
    {
      throw new ArgumentNullException(nameof(name));
    }

    if (controlType is null)
    {
      throw new ArgumentNullException(nameof(controlType));
    }

    if (automationId is null)
    {
      throw new ArgumentNullException(nameof(automationId));
    }

    if (className is null)
    {
      throw new ArgumentNullException(nameof(className));
    }

    if (limit <= 0)
    {
      return Array.Empty<TNode>();
    }

    ct.ThrowIfCancellationRequested();

    const int descendantVisitCap = 10_000;

    var current = new List<TNode>(capacity: 1) { root };

    for (var i = 0; i < segments.Count; i++)
    {
      ct.ThrowIfCancellationRequested();

      var seg = segments[i];
      var next = new List<TNode>(capacity: current.Count * 4);

      if (seg.Descendant)
      {
        // descendant axis: DFS over each frontier node's subtree
        // Use IdentityComparer so records (UiaNode) de-dup by reference, not structural equality
        var visited = new HashSet<TNode>(IdentityComparer<TNode>.Instance);
        var visitCount = 0;

        for (var j = 0; j < current.Count; j++)
        {
          ct.ThrowIfCancellationRequested();

          var stack = new Stack<TNode>();
          var nodeChildren = children(current[j]);
          for (var k = nodeChildren.Count - 1; k >= 0; k--)
          {
            stack.Push(nodeChildren[k]);
          }

          while (stack.Count > 0)
          {
            ct.ThrowIfCancellationRequested();

            if (visitCount >= descendantVisitCap)
            {
              goto doneDescendant;
            }

            var cand = stack.Pop();
            if (!visited.Add(cand))
            {
              continue;
            }

            visitCount++;

            if (SegmentMatches(seg, cand, name, controlType, automationId, className))
            {
              next.Add(cand);
            }

            var candChildren = children(cand);
            for (var k = candChildren.Count - 1; k >= 0; k--)
            {
              stack.Push(candChildren[k]);
            }
          }
        }

        doneDescendant:;
      }
      else
      {
        // child axis (original behavior)
        for (var j = 0; j < current.Count; j++)
        {
          ct.ThrowIfCancellationRequested();

          var node = current[j];

          if (i == 0)
          {
            if (SegmentMatches(seg, node, name, controlType, automationId, className))
            {
              next.Add(node);
            }
          }

          var nodeChildren = children(node);
          for (var k = 0; k < nodeChildren.Count; k++)
          {
            ct.ThrowIfCancellationRequested();

            var cand = nodeChildren[k];
            if (!SegmentMatches(seg, cand, name, controlType, automationId, className))
            {
              continue;
            }

            next.Add(cand);
          }
        }
      }

      current = next;
      if (current.Count == 0)
      {
        return Array.Empty<TNode>();
      }
    }

    if (current.Count <= limit)
    {
      return current;
    }

    return current.Take(limit).ToArray();
  }

  private static bool SegmentMatches<TNode>(
    SelectorSegment seg,
    TNode node,
    Func<TNode, string?> name,
    Func<TNode, string?> controlType,
    Func<TNode, string?> automationId,
    Func<TNode, string?> className)
  {
    if (seg.ControlType is not null && seg.ControlType != "*")
    {
      var nodeType = Normalize(controlType(node));
      if (nodeType is null || !string.Equals(nodeType, seg.ControlType, StringComparison.OrdinalIgnoreCase))
      {
        return false;
      }
    }

    for (var i = 0; i < seg.Filters.Count; i++)
    {
      if (!FilterMatches(seg.Filters[i], node, name, controlType, automationId, className))
      {
        return false;
      }
    }

    return true;
  }

  private static bool FilterMatches<TNode>(
    SelectorFilter filter,
    TNode node,
    Func<TNode, string?> name,
    Func<TNode, string?> controlType,
    Func<TNode, string?> automationId,
    Func<TNode, string?> className)
  {
    var key = filter.Key.ToLowerInvariant();
    string? actual = key switch
    {
      "name" => name(node),
      "title" => name(node),
      "controltype" => controlType(node),
      "automationid" => automationId(node),
      "class" => className(node),
      "classname" => className(node),
      _ => null,
    };

    if (actual is null)
    {
      return false;
    }

    return filter.Op switch
    {
      SelectorOp.Equals => string.Equals(actual, filter.Value, StringComparison.OrdinalIgnoreCase),
      SelectorOp.Contains => actual.Contains(filter.Value, StringComparison.OrdinalIgnoreCase),
      _ => false,
    };
  }

  private static string? Normalize(string? s)
  {
    if (string.IsNullOrWhiteSpace(s))
    {
      return null;
    }

    return s.Trim().Replace(" ", "", StringComparison.Ordinal);
  }

  // Reference-equality comparer for any T; needed because record types use structural equality.
  private sealed class IdentityComparer<T> : IEqualityComparer<T>
  {
    internal static readonly IdentityComparer<T> Instance = new();

    public bool Equals(T? x, T? y) => ReferenceEquals(x, y);

    public int GetHashCode(T obj) =>
      obj is null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
  }

  internal enum SelectorOp
  {
    Equals = 0,
    Contains = 1,
  }

  internal sealed record SelectorFilter(string Key, SelectorOp Op, string Value);

  internal sealed record SelectorSegment(
    string? ControlType,
    IReadOnlyList<SelectorFilter> Filters,
    bool Descendant = false);

  private static class SelectorParser
  {
    public static IReadOnlyList<SelectorSegment> Parse(string expr)
    {
      if (string.IsNullOrWhiteSpace(expr))
      {
        throw new ArgumentException("Selector expr required.", nameof(expr));
      }

      // Hand-scan tokenizer. Splits on '/' while tracking bracket depth so
      // that '/' inside a quoted filter value like [name="a/b"] is literal.
      // Two consecutive out-of-bracket slashes mark the following segment as Descendant.
      // Three or more consecutive slashes → error.
      // Leading single '/' → trimmed (existing behaviour). Leading '//' → first segment is Descendant.

      var tokens = new List<(string Raw, bool Descendant)>();
      var buf = new System.Text.StringBuilder();
      var depth = 0;
      var pos = 0;
      var pendingDescendant = false;

      // strip single leading '/' (not '//')
      if (expr.Length > 0 && expr[0] == '/' && (expr.Length < 2 || expr[1] != '/'))
      {
        pos = 1;
      }

      while (pos < expr.Length)
      {
        var ch = expr[pos];

        if (ch == '[')
        {
          depth++;
          buf.Append(ch);
          pos++;
          continue;
        }

        if (ch == ']')
        {
          if (depth > 0) depth--;
          buf.Append(ch);
          pos++;
          continue;
        }

        if (ch == '/' && depth == 0)
        {
          // count run of slashes
          var slashStart = pos;
          while (pos < expr.Length && expr[pos] == '/') pos++;
          var slashCount = pos - slashStart;

          if (slashCount > 2)
          {
            throw new ArgumentException("Invalid selector: '///' is not a valid axis.", nameof(expr));
          }

          var raw = buf.ToString().Trim();
          buf.Clear();

          if (raw.Length > 0)
          {
            tokens.Add((raw, pendingDescendant));
          }
          else if (pendingDescendant)
          {
            // e.g. "a///b" would hit slashCount>2 above; "a//" mid at end → trailing error below
            throw new ArgumentException("Descendant axis requires a following segment.", nameof(expr));
          }

          // the NEXT token inherits descendant flag from this separator
          pendingDescendant = slashCount == 2;
          continue;
        }

        buf.Append(ch);
        pos++;
      }

      // flush last token
      var lastRaw = buf.ToString().Trim();
      if (lastRaw.Length > 0)
      {
        tokens.Add((lastRaw, pendingDescendant));
      }
      else if (pendingDescendant)
      {
        // trailing '//' with no following segment
        throw new ArgumentException("Descendant axis requires a following segment.", nameof(expr));
      }

      if (tokens.Count == 0)
      {
        throw new ArgumentException("Selector expr required.", nameof(expr));
      }

      var segments = new List<SelectorSegment>(capacity: tokens.Count);
      foreach (var (raw, descendant) in tokens)
      {
        segments.Add(ParseSegment(raw, descendant));
      }

      return segments;
    }

    private static SelectorSegment ParseSegment(string raw, bool descendant = false)
    {
      var typePart = raw;
      var filters = new List<SelectorFilter>();

      var bracketStart = raw.IndexOf('[', StringComparison.Ordinal);
      if (bracketStart >= 0)
      {
        typePart = raw[..bracketStart].Trim();
        var rest = raw[bracketStart..];

        while (rest.Length > 0 && rest[0] == '[')
        {
          var end = rest.IndexOf(']');
          if (end < 0)
          {
            throw new ArgumentException($"Unclosed filter bracket in selector segment: '{raw}'.");
          }

          var inner = rest[1..end].Trim();
          if (inner.Length > 0)
          {
            filters.Add(ParseFilter(inner));
          }

          rest = rest[(end + 1)..].TrimStart();
        }
      }

      var ct = string.IsNullOrWhiteSpace(typePart) ? "*" : Normalize(typePart);
      return new SelectorSegment(ct, filters, descendant);
    }

    private static SelectorFilter ParseFilter(string inner)
    {
      var opIdx = inner.IndexOf("~=", StringComparison.Ordinal);
      var op = SelectorOp.Contains;

      if (opIdx < 0)
      {
        opIdx = inner.IndexOf('=', StringComparison.Ordinal);
        op = SelectorOp.Equals;
      }

      if (opIdx < 0)
      {
        throw new ArgumentException($"Invalid filter: '{inner}'. Expected key=value or key~=\"value\".");
      }

      var key = inner[..opIdx].Trim();
      var valueRaw = op == SelectorOp.Contains
        ? inner[(opIdx + 2)..].Trim()
        : inner[(opIdx + 1)..].Trim();

      var value = Unquote(valueRaw);
      if (string.IsNullOrWhiteSpace(key) || value is null)
      {
        throw new ArgumentException($"Invalid filter: '{inner}'.");
      }

      return new SelectorFilter(key, op, value);
    }

    private static string? Unquote(string s)
    {
      if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
      {
        return s[1..^1];
      }

      if (s.Length >= 2 && s[0] == '\'' && s[^1] == '\'')
      {
        return s[1..^1];
      }

      return string.IsNullOrWhiteSpace(s) ? null : s;
    }
  }
}

