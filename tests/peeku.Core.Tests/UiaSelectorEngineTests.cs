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
}

