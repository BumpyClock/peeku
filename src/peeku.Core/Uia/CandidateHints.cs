namespace peeku;

/// <summary>
/// Generates ranked "did you mean" candidate hints for <see cref="PeekuErrorCode.ElementNotFound"/>
/// errors so that agents can self-correct on the next step.
/// </summary>
internal static class CandidateHints
{
  /// <summary>
  /// A lightweight projection of a <see cref="UiaElement"/> used in error details.
  /// </summary>
  internal sealed record Candidate(
    string? ControlType,
    string? Name,
    string? AutomationId,
    Rect? Rect);

  /// <summary>
  /// Returns up to <paramref name="max"/> ranked candidate elements from <paramref name="elements"/>
  /// based on how well they match <paramref name="intent"/>.
  /// Ranking: substring match of intent in name/automationId > named-over-nameless > shortest-name
  /// tiebreak. Elements without name and automationId are excluded.
  /// </summary>
  internal static IReadOnlyList<Candidate> Suggest(
    IReadOnlyList<UiaElement> elements,
    string? intent,
    int max = 3)
  {
    if (elements is null || elements.Count == 0 || max <= 0)
    {
      return Array.Empty<Candidate>();
    }

    var intentNorm = string.IsNullOrWhiteSpace(intent)
      ? null
      : intent.Trim().ToLowerInvariant();

    // Pre-score; exclude elements with no name and no automationId.
    var scored = new List<(int Score, int NameLen, UiaElement Element)>(capacity: Math.Min(elements.Count, 64));
    for (var i = 0; i < elements.Count; i++)
    {
      var el = elements[i];
      var hasName = !string.IsNullOrWhiteSpace(el.Name);
      var hasId = !string.IsNullOrWhiteSpace(el.AutomationId);
      if (!hasName && !hasId)
      {
        continue;
      }

      var score = Score(el, intentNorm);
      var nameLen = hasName ? el.Name!.Length : int.MaxValue;
      scored.Add((score, nameLen, el));
    }

    // Sort descending score, ascending nameLen tiebreak.
    scored.Sort(static (a, b) =>
    {
      var sc = b.Score.CompareTo(a.Score);
      return sc != 0 ? sc : a.NameLen.CompareTo(b.NameLen);
    });

    var take = Math.Min(max, scored.Count);
    var result = new Candidate[take];
    for (var i = 0; i < take; i++)
    {
      var el = scored[i].Element;
      result[i] = new Candidate(el.ControlType, el.Name, el.AutomationId, el.Rect);
    }

    return result;
  }

  /// <summary>
  /// Extracts the intent string (name or automationId filter value) from the last segment of a
  /// selector expression. Returns null when the selector has no name/automationId filter.
  /// </summary>
  internal static string? IntentFromSelector(string? selectorExpr)
  {
    if (string.IsNullOrWhiteSpace(selectorExpr))
    {
      return null;
    }

    IReadOnlyList<UiaSelectorEngine.SelectorSegment> segments;
    try
    {
      segments = UiaSelectorEngine.Parse(selectorExpr);
    }
    catch
    {
      return null;
    }

    if (segments.Count == 0)
    {
      return null;
    }

    var last = segments[segments.Count - 1];
    for (var i = 0; i < last.Filters.Count; i++)
    {
      var f = last.Filters[i];
      var key = f.Key.ToLowerInvariant();
      if (key is "name" or "title" or "automationid")
      {
        return f.Value;
      }
    }

    return null;
  }

  private static int Score(UiaElement el, string? intentNorm)
  {
    if (intentNorm is null)
    {
      // No intent: named > unnamed, no bonus.
      return string.IsNullOrWhiteSpace(el.Name) ? 0 : 1;
    }

    var nameMatch = !string.IsNullOrWhiteSpace(el.Name) &&
                    el.Name!.Contains(intentNorm, StringComparison.OrdinalIgnoreCase);
    var idMatch = !string.IsNullOrWhiteSpace(el.AutomationId) &&
                  el.AutomationId!.Contains(intentNorm, StringComparison.OrdinalIgnoreCase);

    if (nameMatch || idMatch)
    {
      return 3;
    }

    if (!string.IsNullOrWhiteSpace(el.Name))
    {
      return 1;
    }

    return 0;
  }
}
