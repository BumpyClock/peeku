using peeku;
using Xunit;

namespace peeku.Core.Tests;

public sealed class UiaSelectorEngineTests
{
  private sealed record Node(
    string? Name = null,
    string? ControlType = null,
    string? AutomationId = null,
    string? ClassName = null,
    IReadOnlyList<Node>? Children = null);

  [Fact]
  public void Select_returns_a_child_match_when_the_first_segment_matches_a_child()
  {
    var root = new Node(
      Name: "Main",
      ControlType: "Window",
      Children: new[]
      {
        new Node(Name: "Input", ControlType: "Edit", AutomationId: "Input"),
      });

    var segments = UiaSelectorEngine.Parse("edit");
    var matches = UiaSelectorEngine.Select(
      root,
      segments,
      limit: 20,
      children: static n => n.Children ?? Array.Empty<Node>(),
      name: static n => n.Name,
      controlType: static n => n.ControlType,
      automationId: static n => n.AutomationId,
      className: static n => n.ClassName,
      ct: CancellationToken.None);

    Assert.Single(matches);
    Assert.Equal("Edit", matches[0].ControlType);
  }

  [Fact]
  public void Select_does_not_search_grandchildren_without_a_path_segment()
  {
    var root = new Node(
      ControlType: "Window",
      Children: new[]
      {
        new Node(
          ControlType: "Pane",
          Children: new[]
          {
            new Node(ControlType: "Edit"),
          }),
      });

    var segments = UiaSelectorEngine.Parse("window/edit");
    var matches = UiaSelectorEngine.Select(
      root,
      segments,
      limit: 20,
      children: static n => n.Children ?? Array.Empty<Node>(),
      name: static n => n.Name,
      controlType: static n => n.ControlType,
      automationId: static n => n.AutomationId,
      className: static n => n.ClassName,
      ct: CancellationToken.None);

    Assert.Empty(matches);
  }

  [Fact]
  public void Select_matches_a_deep_path_when_all_segments_match()
  {
    var target = new Node(Name: "Input", ControlType: "Edit", AutomationId: "Input");

    var root = new Node(
      ControlType: "Window",
      Children: new[]
      {
        new Node(
          ControlType: "Pane",
          Children: new[] { target }),
      });

    var segments = UiaSelectorEngine.Parse("window/pane/edit[automationId=\"Input\"]");
    var matches = UiaSelectorEngine.Select(
      root,
      segments,
      limit: 20,
      children: static n => n.Children ?? Array.Empty<Node>(),
      name: static n => n.Name,
      controlType: static n => n.ControlType,
      automationId: static n => n.AutomationId,
      className: static n => n.ClassName,
      ct: CancellationToken.None);

    Assert.Single(matches);
    Assert.Equal(target, matches[0]);
  }

  [Fact]
  public void Select_normalizes_segment_control_type_by_removing_spaces()
  {
    var root = new Node(
      ControlType: "Window",
      Children: new[]
      {
        new Node(ControlType: "Tab Item"),
      });

    var segments = UiaSelectorEngine.Parse("tabitem");
    var matches = UiaSelectorEngine.Select(
      root,
      segments,
      limit: 20,
      children: static n => n.Children ?? Array.Empty<Node>(),
      name: static n => n.Name,
      controlType: static n => n.ControlType,
      automationId: static n => n.AutomationId,
      className: static n => n.ClassName,
      ct: CancellationToken.None);

    Assert.Single(matches);
    Assert.Equal("Tab Item", matches[0].ControlType);
  }

  [Fact]
  public void Parse_throws_for_an_unclosed_filter_bracket()
  {
    var ex = Assert.Throws<ArgumentException>(() => UiaSelectorEngine.Parse("window[name=\"Main\""));
    Assert.Contains("Unclosed filter bracket", ex.Message, StringComparison.OrdinalIgnoreCase);
  }

  // ── descendant axis (//) tests ────────────────────────────────────────────

  [Fact]
  public void Parse_leading_double_slash_marks_first_segment_as_descendant()
  {
    var segments = UiaSelectorEngine.Parse("//button");
    Assert.Single(segments);
    Assert.True(segments[0].Descendant);
    Assert.Equal("button", segments[0].ControlType);
  }

  [Fact]
  public void Parse_mid_double_slash_marks_second_segment_as_descendant()
  {
    var segments = UiaSelectorEngine.Parse("window//button");
    Assert.Equal(2, segments.Count);
    Assert.False(segments[0].Descendant);
    Assert.True(segments[1].Descendant);
  }

  [Fact]
  public void Parse_single_slash_segments_are_not_descendant()
  {
    var segments = UiaSelectorEngine.Parse("window/pane/edit");
    Assert.Equal(3, segments.Count);
    Assert.All(segments, s => Assert.False(s.Descendant));
  }

  [Fact]
  public void Parse_throws_for_trailing_double_slash()
  {
    var ex = Assert.Throws<ArgumentException>(() => UiaSelectorEngine.Parse("window//"));
    Assert.Contains("following segment", ex.Message, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void Parse_throws_for_triple_slash()
  {
    var ex = Assert.Throws<ArgumentException>(() => UiaSelectorEngine.Parse("window///button"));
    Assert.Contains("///", ex.Message, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void Parse_double_slash_inside_filter_value_is_literal_not_axis()
  {
    // [name="a/b"] — the '/' inside brackets must not be tokenized as an axis
    var segments = UiaSelectorEngine.Parse("window[name=\"a/b\"]");
    Assert.Single(segments);
    Assert.False(segments[0].Descendant);
    Assert.Equal("window", segments[0].ControlType);
    Assert.Single(segments[0].Filters);
    Assert.Equal("a/b", segments[0].Filters[0].Value);
  }

  [Fact]
  public void Select_descendant_leading_finds_deeply_nested_node()
  {
    var target = new Node(Name: "Save", ControlType: "Button");
    var root = new Node(
      ControlType: "Window",
      Children: new[]
      {
        new Node(
          ControlType: "Pane",
          Children: new[]
          {
            new Node(
              ControlType: "Group",
              Children: new[] { target }),
          }),
      });

    var segments = UiaSelectorEngine.Parse("//button[name=\"Save\"]");
    var matches = UiaSelectorEngine.Select(
      root,
      segments,
      limit: 20,
      children: static n => n.Children ?? Array.Empty<Node>(),
      name: static n => n.Name,
      controlType: static n => n.ControlType,
      automationId: static n => n.AutomationId,
      className: static n => n.ClassName,
      ct: CancellationToken.None);

    Assert.Single(matches);
    Assert.Equal(target, matches[0]);
  }

  [Fact]
  public void Select_descendant_mid_finds_deep_child_after_child_axis()
  {
    // "window//button": find Window by child axis, then find Button anywhere under it
    var button = new Node(Name: "OK", ControlType: "Button");
    var root = new Node(
      ControlType: "Window",
      Children: new[]
      {
        new Node(
          ControlType: "Pane",
          Children: new[]
          {
            new Node(
              ControlType: "Group",
              Children: new[] { button }),
          }),
      });

    var segments = UiaSelectorEngine.Parse("window//button");
    var matches = UiaSelectorEngine.Select(
      root,
      segments,
      limit: 20,
      children: static n => n.Children ?? Array.Empty<Node>(),
      name: static n => n.Name,
      controlType: static n => n.ControlType,
      automationId: static n => n.AutomationId,
      className: static n => n.ClassName,
      ct: CancellationToken.None);

    Assert.Single(matches);
    Assert.Equal(button, matches[0]);
  }

  [Fact]
  public void Select_descendant_returns_multiple_matches_in_dfs_preorder()
  {
    var b1 = new Node(Name: "First", ControlType: "Button");
    var b2 = new Node(Name: "Second", ControlType: "Button");
    var root = new Node(
      ControlType: "Window",
      Children: new[]
      {
        new Node(ControlType: "Pane", Children: new[] { b1 }),
        new Node(ControlType: "Pane", Children: new[] { b2 }),
      });

    var segments = UiaSelectorEngine.Parse("//button");
    var matches = UiaSelectorEngine.Select(
      root,
      segments,
      limit: 20,
      children: static n => n.Children ?? Array.Empty<Node>(),
      name: static n => n.Name,
      controlType: static n => n.ControlType,
      automationId: static n => n.AutomationId,
      className: static n => n.ClassName,
      ct: CancellationToken.None);

    Assert.Equal(2, matches.Count);
    Assert.Equal(b1, matches[0]);
    Assert.Equal(b2, matches[1]);
  }

  [Fact]
  public void Select_descendant_does_not_match_root_itself()
  {
    // Leading // should DFS the children of root, not root itself
    var root = new Node(ControlType: "Window");

    var segments = UiaSelectorEngine.Parse("//window");
    var matches = UiaSelectorEngine.Select(
      root,
      segments,
      limit: 20,
      children: static n => n.Children ?? Array.Empty<Node>(),
      name: static n => n.Name,
      controlType: static n => n.ControlType,
      automationId: static n => n.AutomationId,
      className: static n => n.ClassName,
      ct: CancellationToken.None);

    Assert.Empty(matches);
  }

  [Fact]
  public void Select_child_axis_unchanged_after_descendant_implementation()
  {
    // Regression: existing child-axis behaviour must be byte-identical
    var target = new Node(ControlType: "Edit");
    var root = new Node(
      ControlType: "Window",
      Children: new[]
      {
        new Node(
          ControlType: "Pane",
          Children: new[] { target }),
      });

    // window/edit should NOT match (Edit is grandchild, not child of Window)
    var segments = UiaSelectorEngine.Parse("window/edit");
    var matches = UiaSelectorEngine.Select(
      root,
      segments,
      limit: 20,
      children: static n => n.Children ?? Array.Empty<Node>(),
      name: static n => n.Name,
      controlType: static n => n.ControlType,
      automationId: static n => n.AutomationId,
      className: static n => n.ClassName,
      ct: CancellationToken.None);

    Assert.Empty(matches);
  }
}

