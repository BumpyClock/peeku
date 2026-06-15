using peeku;
using Xunit;

namespace peeku.Core.Tests;

public sealed class CandidateHintsTests
{
  private static UiaElement El(string? name, string? automationId = null, string? controlType = null)
    => new(Element: new ElementRef(""), Name: name, AutomationId: automationId, ControlType: controlType);

  // ── Suggest: basic filtering ────────────────────────────────────────────

  [Fact]
  public void Suggest_excludes_elements_with_no_name_and_no_automationId()
  {
    var elements = new[]
    {
      El(null, null),
      El("", ""),
      El("OK"),
    };

    var result = CandidateHints.Suggest(elements, intent: null);

    Assert.Single(result);
    Assert.Equal("OK", result[0].Name);
  }

  [Fact]
  public void Suggest_returns_empty_when_elements_list_is_empty()
  {
    var result = CandidateHints.Suggest(Array.Empty<UiaElement>(), "OK");
    Assert.Empty(result);
  }

  [Fact]
  public void Suggest_respects_max_cap()
  {
    var elements = Enumerable.Range(1, 10)
      .Select(i => El($"Button{i}"))
      .ToArray();

    var result = CandidateHints.Suggest(elements, intent: null, max: 3);

    Assert.Equal(3, result.Count);
  }

  // ── Suggest: ranking with intent ────────────────────────────────────────

  [Fact]
  public void Suggest_ranks_substring_match_above_no_match()
  {
    var elements = new[]
    {
      El("ZzzUnrelated"),
      El("OK Button"),        // name matches "ok"
    };

    var result = CandidateHints.Suggest(elements, intent: "ok", max: 2);

    Assert.Equal(2, result.Count);
    Assert.Equal("OK Button", result[0].Name);
  }

  [Fact]
  public void Suggest_ranks_automationId_match_same_tier_as_name_match()
  {
    var elements = new[]
    {
      El("Unrelated"),
      El(null, automationId: "OkButton"),  // automationId matches "ok"
    };

    var result = CandidateHints.Suggest(elements, intent: "ok", max: 2);

    // The automationId-matching element should rank above the non-matching named element.
    Assert.Equal("OkButton", result[0].AutomationId);
  }

  [Fact]
  public void Suggest_match_is_case_insensitive()
  {
    var elements = new[]
    {
      El("SUBMIT"),
      El("CancelButton"),
    };

    var result = CandidateHints.Suggest(elements, intent: "submit", max: 2);

    Assert.Equal("SUBMIT", result[0].Name);
  }

  [Fact]
  public void Suggest_tiebreaks_by_shortest_name_when_scores_equal()
  {
    // Both match intent "btn", both named — tiebreak is shortest name.
    var elements = new[]
    {
      El("btn-long-name"),
      El("btn"),
    };

    var result = CandidateHints.Suggest(elements, intent: "btn", max: 2);

    Assert.Equal("btn", result[0].Name);
  }

  [Fact]
  public void Suggest_named_ranks_above_automationId_only_when_no_intent()
  {
    // No intent: named elements score 1, automationId-only score 0.
    var elements = new[]
    {
      El(null, automationId: "aid-only"),
      El("Named"),
    };

    var result = CandidateHints.Suggest(elements, intent: null, max: 2);

    Assert.Equal("Named", result[0].Name);
  }

  // ── IntentFromSelector ───────────────────────────────────────────────────

  [Fact]
  public void IntentFromSelector_returns_name_filter_value()
  {
    var intent = CandidateHints.IntentFromSelector("button[name=\"OK\"]");
    Assert.Equal("OK", intent);
  }

  [Fact]
  public void IntentFromSelector_returns_automationId_filter_value()
  {
    var intent = CandidateHints.IntentFromSelector("edit[automationId=\"SearchBox\"]");
    Assert.Equal("SearchBox", intent);
  }

  [Fact]
  public void IntentFromSelector_uses_last_segment()
  {
    var intent = CandidateHints.IntentFromSelector("pane/button[name=\"Submit\"]");
    Assert.Equal("Submit", intent);
  }

  [Fact]
  public void IntentFromSelector_returns_null_for_selector_without_name_filter()
  {
    var intent = CandidateHints.IntentFromSelector("button");
    Assert.Null(intent);
  }

  [Fact]
  public void IntentFromSelector_returns_null_for_null_input()
  {
    var intent = CandidateHints.IntentFromSelector(null);
    Assert.Null(intent);
  }

  [Fact]
  public void IntentFromSelector_returns_null_for_whitespace_input()
  {
    var intent = CandidateHints.IntentFromSelector("   ");
    Assert.Null(intent);
  }

  [Fact]
  public void IntentFromSelector_handles_title_filter_as_name_alias()
  {
    var intent = CandidateHints.IntentFromSelector("window[title=\"Notepad\"]");
    Assert.Equal("Notepad", intent);
  }

  // ── Integration: Suggest + IntentFromSelector round-trip ────────────────

  [Fact]
  public void Suggest_with_IntentFromSelector_ranks_matching_candidate_first()
  {
    var elements = new[]
    {
      El("Cancel"),
      El("OK"),
      El("Help"),
    };

    var intent = CandidateHints.IntentFromSelector("button[name=\"OK\"]");
    var result = CandidateHints.Suggest(elements, intent, max: 3);

    Assert.Equal("OK", result[0].Name);
  }
}
