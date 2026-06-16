using peeku;
using Xunit;

namespace peeku.Core.Tests;

/// <summary>
/// Live-ish tests for AppLifecycle.ListAsync on the real desktop.
/// Tolerant: does not assert any specific app is running.
/// Requires a desktop session (not a headless CI agent with no windows).
/// </summary>
public sealed class AppListTests
{
  [Fact]
  public async Task ListAsync_ReturnsOk_WithNonEmptyApps()
  {
    var req = new AppListRequest();
    var result = await AppLifecycle.ListAsync(req, CancellationToken.None);

    Assert.True(result.Ok, $"Expected Ok=true but got error: {result.Error?.Message}");
    Assert.NotNull(result.Apps);
    Assert.NotEmpty(result.Apps);
  }

  [Fact]
  public async Task ListAsync_EachAppInfo_HasValidProcessIdAndProcessName()
  {
    var req = new AppListRequest();
    var result = await AppLifecycle.ListAsync(req, CancellationToken.None);

    Assert.True(result.Ok);
    foreach (var app in result.Apps)
    {
      Assert.True(app.ProcessId > 0, $"ProcessId must be > 0, got {app.ProcessId}");
      // ProcessName is non-null but may be "" if a pid-recycle race makes the name unresolvable
      // (Win32Windows swallows that and falls back to empty) — empty is valid, so assert non-null only.
      Assert.NotNull(app.ProcessName);
    }
  }

  [Fact]
  public async Task ListAsync_AtMostOneApp_IsActive()
  {
    var req = new AppListRequest();
    var result = await AppLifecycle.ListAsync(req, CancellationToken.None);

    Assert.True(result.Ok);
    var activeCount = result.Apps.Count(a => a.Active);
    Assert.True(activeCount <= 1, $"Expected at most 1 active app, found {activeCount}");
  }

  [Fact]
  public async Task ListAsync_Limit_CapsResults()
  {
    var req = new AppListRequest(Limit: 2);
    var result = await AppLifecycle.ListAsync(req, CancellationToken.None);

    Assert.True(result.Ok);
    Assert.True(result.Apps.Count <= 2, $"Expected at most 2 apps with Limit=2, got {result.Apps.Count}");
  }

  [Fact]
  public async Task ListAsync_EmptyApps_ReturnsOk()
  {
    // Limit=0 should return Ok with empty list (or the impl clamps — either is fine, just must be Ok).
    var req = new AppListRequest(Limit: 0);
    var result = await AppLifecycle.ListAsync(req, CancellationToken.None);

    Assert.True(result.Ok);
    Assert.NotNull(result.Apps);
  }
}
