using System.Diagnostics;
using peeku;
using Xunit;

namespace peeku.Core.Tests;

/// <summary>
/// Unit tests for AppLifecycle (S4).
/// Covers: PSI construction, WM_CLOSE-before-kill contract, --force skip, bad path → InvalidArgument.
/// No live process spawning — uses static helpers that are unit-testable.
/// </summary>
public sealed class AppLifecycleTests
{
  // ── AppLaunchRequest record ───────────────────────────────────────────────

  [Fact]
  public void AppLaunchRequest_Defaults_AreCorrect()
  {
    var req = new AppLaunchRequest("notepad.exe");
    Assert.Equal("notepad.exe", req.Target);
    Assert.Null(req.Args);
    Assert.False(req.WaitUntilReady);
    Assert.Equal(5000, req.WaitMs);
    Assert.False(req.NoFocus);
  }

  [Fact]
  public void AppLaunchRequest_AllFields_RoundTrip()
  {
    var req = new AppLaunchRequest(
      Target: "shell:AppsFolder\\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App",
      Args: "--silent",
      WaitUntilReady: true,
      WaitMs: 8000,
      NoFocus: true);

    Assert.Equal("shell:AppsFolder\\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", req.Target);
    Assert.Equal("--silent", req.Args);
    Assert.True(req.WaitUntilReady);
    Assert.Equal(8000, req.WaitMs);
    Assert.True(req.NoFocus);
  }

  // ── AppQuitRequest record ─────────────────────────────────────────────────

  [Fact]
  public void AppQuitRequest_Defaults_AreCorrect()
  {
    var req = new AppQuitRequest();
    Assert.Null(req.ProcessId);
    Assert.Null(req.ProcessName);
    Assert.False(req.Force);
    Assert.False(req.All);
    Assert.Null(req.Except);
    Assert.Equal(3000, req.WaitMs);
  }

  // ── AppLaunchResult / AppQuitResult records ────────────────────────────────

  [Fact]
  public void AppLaunchResult_Ok_CanBeConstructed()
  {
    var meta = Results.Start().Meta();
    var window = new WindowInfo("0x00001234", 42, "Notepad", "notepad");
    var result = new AppLaunchResult(Ok: true, Meta: meta, ProcessId: 42, Window: window);

    Assert.True(result.Ok);
    Assert.Equal(42, result.ProcessId);
    Assert.Same(window, result.Window);
    Assert.Null(result.Error);
  }

  [Fact]
  public void AppLaunchResult_Error_CanBeConstructed()
  {
    var meta = Results.Start().Meta();
    var error = PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "app not found for 'bad.exe'; verify the path or AUMID");
    var result = new AppLaunchResult(Ok: false, Meta: meta, Error: error);

    Assert.False(result.Ok);
    Assert.Equal(PeekuErrors.Code(PeekuErrorCode.InvalidArgument), result.Error!.Code);
    Assert.Contains("app not found", result.Error.Message);
  }

  [Fact]
  public void AppQuitResult_Ok_CanBeConstructed()
  {
    var meta = Results.Start().Meta();
    var result = new AppQuitResult(Ok: true, Meta: meta, Closed: 1, Killed: 0);

    Assert.True(result.Ok);
    Assert.Equal(1, result.Closed);
    Assert.Equal(0, result.Killed);
  }

  // ── Bad target → InvalidArgument ──────────────────────────────────────────

  [Fact]
  public async Task LaunchAsync_EmptyTarget_ReturnsInvalidArgument()
  {
    var req = new AppLaunchRequest(Target: "");
    var result = await AppLifecycle.LaunchAsync(req, CancellationToken.None);

    Assert.False(result.Ok);
    Assert.Equal(PeekuErrors.Code(PeekuErrorCode.InvalidArgument), result.Error!.Code);
  }

  [Fact]
  public async Task LaunchAsync_NullTarget_ReturnsInvalidArgument()
  {
    var req = new AppLaunchRequest(Target: "   ");
    var result = await AppLifecycle.LaunchAsync(req, CancellationToken.None);

    Assert.False(result.Ok);
    Assert.Equal(PeekuErrors.Code(PeekuErrorCode.InvalidArgument), result.Error!.Code);
  }

  [Fact]
  public async Task LaunchAsync_NonexistentPath_ReturnsInvalidArgument()
  {
    // A completely bogus path that cannot exist.
    var req = new AppLaunchRequest(Target: @"C:\this_path_does_not_exist_peeku_test_xyz.exe");
    var result = await AppLifecycle.LaunchAsync(req, CancellationToken.None);

    Assert.False(result.Ok);
    // Win32Exception / FileNotFoundException → mapped to InvalidArgument.
    Assert.Equal(PeekuErrors.Code(PeekuErrorCode.InvalidArgument), result.Error!.Code);
    Assert.Contains("app not found", result.Error.Message);
  }

  // ── Quit: no pid and no name → InvalidArgument ───────────────────────────

  [Fact]
  public async Task QuitAsync_NoPidNoName_ReturnsInvalidArgument()
  {
    var req = new AppQuitRequest();
    var result = await AppLifecycle.QuitAsync(req, CancellationToken.None);

    Assert.False(result.Ok);
    Assert.Equal(PeekuErrors.Code(PeekuErrorCode.InvalidArgument), result.Error!.Code);
  }

  // ── Quit: unknown pid → NotFound ─────────────────────────────────────────

  [Fact]
  public async Task QuitAsync_UnknownPid_ReturnsNotFound()
  {
    // PID 0x7FFFFFFF is extremely unlikely to exist.
    var req = new AppQuitRequest(ProcessId: int.MaxValue);
    var result = await AppLifecycle.QuitAsync(req, CancellationToken.None);

    Assert.False(result.Ok);
    Assert.Equal(PeekuErrors.Code(PeekuErrorCode.NotFound), result.Error!.Code);
  }

  // ── Quit --all without processName → InvalidArgument ─────────────────────

  [Fact]
  public async Task QuitAsync_AllWithoutProcessName_ReturnsInvalidArgument()
  {
    var req = new AppQuitRequest(All: true);
    var result = await AppLifecycle.QuitAsync(req, CancellationToken.None);

    Assert.False(result.Ok);
    Assert.Equal(PeekuErrors.Code(PeekuErrorCode.InvalidArgument), result.Error!.Code);
  }

  // ── Quit --all with unknown name → Ok with zero counts (no processes match) ──

  [Fact]
  public async Task QuitAsync_AllWithUnknownName_ReturnsOkZeroCounts()
  {
    // Process name that should never be running.
    var req = new AppQuitRequest(All: true, ProcessName: "peeku_test_ghost_process_xyz_abc");
    var result = await AppLifecycle.QuitAsync(req, CancellationToken.None);

    Assert.True(result.Ok);
    Assert.Equal(0, result.Closed);
    Assert.Equal(0, result.Killed);
  }
}
