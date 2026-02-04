namespace peeku;

public static class UiaSelectors
{
  public static IReadOnlyList<ElementRef> Select(UiaSnapshotResult snapshot, Selector selector, int limit = 20)
  {
    if (snapshot is null)
    {
      throw new ArgumentNullException(nameof(snapshot));
    }

    if (selector is null)
    {
      throw new ArgumentNullException(nameof(selector));
    }

    if (limit <= 0)
    {
      return Array.Empty<ElementRef>();
    }

    var segments = SelectorParser.Parse(selector.Expr);

    var byRefId = snapshot.Elements.ToDictionary(e => e.Element.RefId, StringComparer.Ordinal);
    var results = new List<ElementRef>(capacity: Math.Clamp(limit, 0, 256));

    var current = new List<UiaNode>(capacity: 1) { snapshot.Root };

    for (var i = 0; i < segments.Count; i++)
    {
      var seg = segments[i];
      var next = new List<UiaNode>(capacity: current.Count * 4);

      foreach (var node in current)
      {
        IEnumerable<UiaNode> nodesToCheck = i == 0
          ? EnumerateSelfThenChildren(node)
          : EnumerateChildren(node);

        foreach (var cand in nodesToCheck)
        {
          if (!seg.Matches(cand, byRefId))
          {
            continue;
          }

          next.Add(cand);
        }
      }

      current = next;

      if (current.Count == 0)
      {
        return Array.Empty<ElementRef>();
      }
    }

    foreach (var node in current)
    {
      results.Add(node.Element);
      if (results.Count >= limit)
      {
        break;
      }
    }

    return results;
  }

  private static IEnumerable<UiaNode> EnumerateSelfThenChildren(UiaNode node)
  {
    yield return node;

    if (node.Children is null)
    {
      yield break;
    }

    foreach (var child in node.Children)
    {
      yield return child;
    }
  }

  private static IEnumerable<UiaNode> EnumerateChildren(UiaNode node)
  {
    if (node.Children is null)
    {
      yield break;
    }

    foreach (var child in node.Children)
    {
      yield return child;
    }
  }

  private sealed record SelectorSegment(
    string? ControlType,
    IReadOnlyList<SelectorFilter> Filters)
  {
    public bool Matches(UiaNode node, IReadOnlyDictionary<string, UiaElement> byRefId)
    {
      if (ControlType is not null && ControlType != "*")
      {
        var nodeType = Normalize(node.ControlType);
        if (nodeType is null || !string.Equals(nodeType, ControlType, StringComparison.OrdinalIgnoreCase))
        {
          return false;
        }
      }

      if (Filters.Count == 0)
      {
        return true;
      }

      _ = byRefId.TryGetValue(node.Element.RefId, out var element);
      foreach (var f in Filters)
      {
        if (!f.Matches(node, element))
        {
          return false;
        }
      }

      return true;
    }
  }

  private enum SelectorOp
  {
    Equals = 0,
    Contains = 1,
  }

  private sealed record SelectorFilter(string Key, SelectorOp Op, string Value)
  {
    public bool Matches(UiaNode node, UiaElement? element)
    {
      var key = Key.ToLowerInvariant();
      string? actual = key switch
      {
        "name" => node.Name,
        "title" => node.Name,
        "controltype" => node.ControlType,
        "automationid" => element?.AutomationId,
        "class" => element?.ClassName,
        "classname" => element?.ClassName,
        _ => null,
      };

      if (actual is null)
      {
        return false;
      }

      return Op switch
      {
        SelectorOp.Equals => string.Equals(actual, Value, StringComparison.OrdinalIgnoreCase),
        SelectorOp.Contains => actual.Contains(Value, StringComparison.OrdinalIgnoreCase),
        _ => false,
      };
    }
  }

  private static string? Normalize(string? s)
  {
    if (string.IsNullOrWhiteSpace(s))
    {
      return null;
    }

    return s.Trim().Replace(" ", "", StringComparison.Ordinal);
  }

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

