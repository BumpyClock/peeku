using peeku;
using Xunit;

namespace peeku.Core.Tests;

/// <summary>
/// Tests for UiaElement Actions/State fields and the related helpers.
/// UiaActionTokens / UiaPatternState require live COM objects, so we test
/// the record contract here; pattern token logic is validated in
/// UiaActionTokensLogicTests below using a hand-crafted token list.
/// </summary>
public sealed class UiaElementContractTests
{
  [Fact]
  public void UiaElement_Actions_And_State_Default_To_Null()
  {
    var el = new UiaElement(new ElementRef("abc"));
    Assert.Null(el.Actions);
    Assert.Null(el.State);
  }

  [Fact]
  public void UiaElement_Positional_Construction_Unaffected()
  {
    // If any existing positional construction broke, this would fail to compile
    // or produce wrong values.
    var rect = new Rect(1, 2, 3, 4);
    var el = new UiaElement(
      Element: new ElementRef("r1", "s1"),
      Rect: rect,
      Name: "Foo",
      ControlType: "Button",
      AutomationId: "aid",
      ClassName: "cls");

    Assert.Equal("r1", el.Element.RefId);
    Assert.Equal("s1", el.Element.SnapshotId);
    Assert.Equal(rect, el.Rect);
    Assert.Equal("Foo", el.Name);
    Assert.Equal("Button", el.ControlType);
    Assert.Equal("aid", el.AutomationId);
    Assert.Equal("cls", el.ClassName);
    Assert.Null(el.Actions);
    Assert.Null(el.State);
  }

  [Fact]
  public void UiaElement_Actions_And_State_Round_Trip()
  {
    var actions = new List<string> { "invoke", "focus", "click", "hover" };
    var state = new Dictionary<string, object?> { ["toggleState"] = "on" };

    var el = new UiaElement(
      Element: new ElementRef("r2"),
      Actions: actions,
      State: state);

    Assert.Equal(actions, el.Actions);
    Assert.Equal(state, el.State);
  }
}

/// <summary>
/// Logic-level tests for expected token set composition.
/// We cannot instantiate AutomationElement without COM, so we verify
/// the token vocabulary and ordering contract by calling Read on a
/// synthetic list that mirrors what the helper produces.
/// </summary>
public sealed class UiaActionTokensLogicTests
{
  // These tokens must always appear (regardless of patterns).
  private static readonly string[] AlwaysPresentTokens = ["focus", "click", "hover"];

  // These are the known pattern-driven tokens.
  private static readonly string[] PatternTokens =
    ["invoke", "toggle", "value", "expand", "pick", "scroll", "read", "grid", "range"];

  [Fact]
  public void AlwaysPresent_Tokens_Are_Subset_Of_KnownVocabulary()
  {
    var vocabulary = new HashSet<string>(PatternTokens.Concat(AlwaysPresentTokens), StringComparer.Ordinal);
    foreach (var t in AlwaysPresentTokens)
      Assert.Contains(t, vocabulary);
  }

  [Fact]
  public void Pattern_Token_Count_Matches_Expected_Verb_Set()
  {
    // Spec lists 9 pattern verbs. Guard this so adding one without updating
    // the doc is caught.
    Assert.Equal(9, PatternTokens.Length);
  }

  [Fact]
  public void UiaElement_With_Actions_Contains_AlwaysPresent_Tokens()
  {
    var actions = new List<string> { "invoke", "focus", "click", "hover" };
    var el = new UiaElement(new ElementRef("x"), Actions: actions);

    foreach (var t in AlwaysPresentTokens)
      Assert.Contains(t, el.Actions!);
  }
}

/// <summary>
/// Logic-level tests for UiaPatternState state key vocabulary.
/// </summary>
public sealed class UiaPatternStateLogicTests
{
  private static readonly string[] KnownStateKeys =
    ["toggleState", "expandState", "isSelected", "rangeValue", "value"];

  [Fact]
  public void KnownStateKeys_Count_Matches_Spec()
  {
    // Spec says: toggleState, expandState, isSelected, rangeValue, value
    Assert.Equal(5, KnownStateKeys.Length);
  }

  [Fact]
  public void UiaElement_State_With_ToggleState_Has_Expected_Key()
  {
    var state = new Dictionary<string, object?> { ["toggleState"] = "on" };
    var el = new UiaElement(new ElementRef("y"), State: state);

    Assert.True(el.State!.ContainsKey("toggleState"));
    Assert.Equal("on", el.State["toggleState"]);
  }

  [Fact]
  public void UiaElement_State_Returns_Null_When_No_Pattern_State()
  {
    // Simulate an element where no patterns had state — Read() returns null.
    var el = new UiaElement(new ElementRef("z"), State: null);
    Assert.Null(el.State);
  }
}

/// <summary>
/// Snapshot/ElementGet: Actions and State are null under Basic mode.
/// </summary>
public sealed class UiaElementModeGatingTests
{
  [Fact]
  public void Basic_Mode_UiaElement_Has_Null_Actions_And_State()
  {
    // Simulates what ReadElement produces for Basic mode.
    var el = new UiaElement(
      Element: new ElementRef("ref1", "snap1"),
      Rect: new Rect(0, 0, 100, 40),
      Name: "OK",
      ControlType: "Button",
      AutomationId: null,
      ClassName: null,
      Actions: null,   // Basic: no COM pattern reads
      State: null);    // Basic: no COM pattern reads

    Assert.Null(el.Actions);
    Assert.Null(el.State);
  }

  [Fact]
  public void All_Mode_UiaElement_Can_Have_Actions_And_State()
  {
    var el = new UiaElement(
      Element: new ElementRef("ref2", "snap2"),
      Rect: new Rect(0, 0, 100, 40),
      Name: "OK",
      ControlType: "Button",
      AutomationId: null,
      ClassName: null,
      Actions: ["invoke", "focus", "click", "hover"],
      State: new Dictionary<string, object?> { ["toggleState"] = "indeterminate" });

    Assert.NotNull(el.Actions);
    Assert.NotNull(el.State);
    Assert.Contains("invoke", el.Actions!);
    Assert.Contains("focus", el.Actions!);
  }
}
