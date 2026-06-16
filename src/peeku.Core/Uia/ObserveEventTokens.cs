namespace peeku;

/// <summary>
/// Single source of truth for the observe event-set wire vocabulary.
/// <see cref="Encode"/> and <see cref="Decode"/> are exact inverses:
/// <c>Decode(Encode(set)) == set</c> for every set.
/// The CLI daemon client (enum → tokens) and the batch arg parser (tokens → enum)
/// both route through here so the two halves can never drift — this is the fix for
/// the historical Property → Structure collapse. Wire tokens: structure | property | focus.
/// </summary>
internal static class ObserveEventTokens
{
  /// <summary>Decompose a (possibly combined) event set into its wire tokens, in canonical order.</summary>
  public static IReadOnlyList<string> Encode(ObserveEventSet set)
  {
    var tokens = new List<string>(capacity: 3);
    if ((set & ObserveEventSet.Structure) != 0) tokens.Add("structure");
    if ((set & ObserveEventSet.Property) != 0) tokens.Add("property");
    if ((set & ObserveEventSet.Focus) != 0) tokens.Add("focus");
    return tokens;
  }

  /// <summary>Combine wire tokens back into an event set. Unknown tokens are ignored; empty → None.</summary>
  public static ObserveEventSet Decode(IEnumerable<string> tokens)
  {
    var set = ObserveEventSet.None;
    foreach (var raw in tokens)
    {
      switch ((raw ?? "").Trim().ToLowerInvariant())
      {
        case "structure": set |= ObserveEventSet.Structure; break;
        case "property": set |= ObserveEventSet.Property; break;
        case "focus": set |= ObserveEventSet.Focus; break;
      }
    }

    return set;
  }
}
