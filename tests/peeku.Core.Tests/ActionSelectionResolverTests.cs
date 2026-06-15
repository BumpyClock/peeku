using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using peeku;
using Xunit;

namespace peeku.Core.Tests;

/// <summary>
/// Unit tests for <see cref="ActionSelectionResolver"/>'s guard/branch selection. These exercise the
/// contract that "neither element nor selector" no longer fails as <c>InvalidArgument</c> and instead
/// routes to the focused-element path. The focused path constructs a real <see cref="UIA3Automation"/>
/// internally, so we drive it deterministically by injecting a <c>resolveRoot</c> fake that returns a
/// null root (producing WindowNotFound / NotSupported) — proving the branch is reached without needing
/// a live desktop or a real focused element.
/// </summary>
public sealed class ActionSelectionResolverTests
{
  // snapshotAsync must be non-null but is never invoked on the focused/null-root path.
  private static Task<UiaSnapshotResult> UnusedSnapshot(UiaSnapshotRequest _, CancellationToken __)
    => throw new InvalidOperationException("snapshotAsync should not be called for the focused path.");

  private static Func<Target, UIA3Automation, CancellationToken, (AutomationElement? Root, string? Warning)>
    RootReturning(AutomationElement? root, string? warning)
    => (_, _, _) => (root, warning);

  [Fact]
  public async Task ResolveAsync_Neither_Element_Nor_Selector_Does_Not_Reject_As_InvalidArgument()
  {
    var resolveRoot = RootReturning(root: null, warning: "No window matched query.");

    var res = await ActionSelectionResolver.ResolveAsync(
      UnusedSnapshot,
      resolveRoot,
      element: null,
      selector: null,
      target: new Target.WindowByQuery(new WindowQuery(ProcessName: "notepad")),
      CancellationToken.None);

    // The old contract rejected "neither" as InvalidArgument; the new contract routes to the focused
    // path, which (with a null root from the fake) deterministically yields WindowNotFound.
    Assert.False(res.Ok);
    Assert.NotNull(res.Error);
    Assert.NotEqual(PeekuErrors.Code(PeekuErrorCode.InvalidArgument), res.Error!.Code);
    Assert.Equal(PeekuErrors.Code(PeekuErrorCode.WindowNotFound), res.Error.Code);
  }

  [Fact]
  public async Task ResolveAsync_Neither_With_NotSupported_Root_Maps_To_NotSupported()
  {
    var resolveRoot = RootReturning(root: null, warning: "Screen target not supported for UIA snapshot.");

    var res = await ActionSelectionResolver.ResolveAsync(
      UnusedSnapshot,
      resolveRoot,
      element: null,
      selector: null,
      target: new Target.Screen(0),
      CancellationToken.None);

    Assert.False(res.Ok);
    Assert.NotNull(res.Error);
    Assert.Equal(PeekuErrors.Code(PeekuErrorCode.NotSupported), res.Error!.Code);
    Assert.Equal("Screen target not supported for UIA snapshot.", res.Warning);
  }

  [Fact]
  public async Task ResolveAsync_Neither_Defaults_Target_To_Focused_Window()
  {
    Target? captured = null;
    Func<Target, UIA3Automation, CancellationToken, (AutomationElement? Root, string? Warning)> resolveRoot =
      (t, _, _) => { captured = t; return (null, "No foreground window."); };

    var res = await ActionSelectionResolver.ResolveAsync(
      UnusedSnapshot,
      resolveRoot,
      element: null,
      selector: null,
      target: null,
      CancellationToken.None);

    Assert.False(res.Ok);
    Assert.IsType<Target.FocusedWindow>(captured);
  }

  [Fact]
  public async Task ResolveAsync_Both_Element_And_Selector_Still_Rejected()
  {
    var res = await ActionSelectionResolver.ResolveAsync(
      UnusedSnapshot,
      RootReturning(root: null, warning: null),
      element: new ElementRef("uia:1:abc"),
      selector: new Selector("button[name=\"OK\"]"),
      target: null,
      CancellationToken.None);

    Assert.False(res.Ok);
    Assert.NotNull(res.Error);
    Assert.Equal(PeekuErrors.Code(PeekuErrorCode.InvalidArgument), res.Error!.Code);
    Assert.Contains("only one", res.Error.Message, StringComparison.OrdinalIgnoreCase);
  }
}
