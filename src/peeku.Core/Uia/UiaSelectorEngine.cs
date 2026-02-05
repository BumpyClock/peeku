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

    var current = new List<TNode>(capacity: 1) { root };

    for (var i = 0; i < segments.Count; i++)
    {
      ct.ThrowIfCancellationRequested();

      var seg = segments[i];
      var next = new List<TNode>(capacity: current.Count * 4);

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

  internal enum SelectorOp
  {
    Equals = 0,
    Contains = 1,
  }

  internal sealed record SelectorFilter(string Key, SelectorOp Op, string Value);

  internal sealed record SelectorSegment(
    string? ControlType,
    IReadOnlyList<SelectorFilter> Filters);

  private static class SelectorParser
  {
    public static IReadOnlyList<SelectorSegment> Parse(string expr)
    {
      if (string.IsNullOrWhiteSpace(expr))
      {
        throw new ArgumentException("Selector expr required.", nameof(expr));
      }

      var rawSegments = expr.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
      if (rawSegments.Length == 0)
      {
        throw new ArgumentException("Selector expr required.", nameof(expr));
      }

      var segments = new List<SelectorSegment>(capacity: rawSegments.Length);
      foreach (var raw in rawSegments)
      {
        segments.Add(ParseSegment(raw));
      }

      return segments;
    }

    private static SelectorSegment ParseSegment(string raw)
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
      return new SelectorSegment(ct, filters);
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

