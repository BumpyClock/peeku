using System.Text.Json;
using peeku;
using Xunit;

namespace peeku.Mcp.Tests;

public sealed class PeekuMcpToolDispatcherTests
{
  [Fact]
  public async Task DispatchAsync_Doctor_SkipsBatch()
  {
    var client = new FakeClient
    {
      DoctorHandler = req => new DoctorResult(
        Ok: true,
        Meta: new ResultMeta("t", DateTimeOffset.UnixEpoch, 1),
        Checks: Array.Empty<DoctorCheck>()),
    };

    var args = JsonSerializer.SerializeToElement(new { deep = true });
    var (ok, _, payload, error) = await PeekuMcpToolDispatcher.DispatchAsync(client, "peeku_doctor", args, CancellationToken.None);

    Assert.True(ok);
    Assert.Null(error);
    Assert.IsType<DoctorResult>(payload);
    Assert.Equal(1, client.DoctorCalls);
    Assert.Equal(0, client.BatchCalls);
    Assert.True(client.LastDoctorReq?.Deep);
  }

  [Fact]
  public async Task DispatchAsync_WindowsList_RoutesThroughBatch()
  {
    var client = new FakeClient
    {
      BatchHandler = req =>
      {
        Assert.Single(req.Ops);
        Assert.Equal("peeku_windows_list", req.Ops[0].Tool);
        Assert.True(req.StopOnError);

        var res = new WindowListResult(
          Ok: true,
          Meta: new ResultMeta("t", DateTimeOffset.UnixEpoch, 1),
          Windows: Array.Empty<WindowInfo>());

        return new BatchResult(
          Ok: true,
          Meta: new ResultMeta("bt", DateTimeOffset.UnixEpoch, 1),
          Results: new[] { new BatchStepResult("peeku_windows_list", true, 1, res) });
      },
    };

    var args = JsonSerializer.SerializeToElement(new { limit = 1 });
    var (ok, _, payload, error) = await PeekuMcpToolDispatcher.DispatchAsync(client, "peeku_windows_list", args, CancellationToken.None);

    Assert.True(ok);
    Assert.Null(error);
    Assert.IsType<WindowListResult>(payload);
    Assert.Equal(0, client.DoctorCalls);
    Assert.Equal(1, client.BatchCalls);
  }

  private sealed class FakeClient : IPeekuClient
  {
    public int DoctorCalls { get; private set; }
    public DoctorRequest? LastDoctorReq { get; private set; }
    public Func<DoctorRequest, DoctorResult>? DoctorHandler { get; init; }

    public int BatchCalls { get; private set; }
    public BatchRequest? LastBatchReq { get; private set; }
    public Func<BatchRequest, BatchResult>? BatchHandler { get; init; }

    public Task<DoctorResult> DoctorAsync(DoctorRequest req, CancellationToken ct = default)
    {
      DoctorCalls++;
      LastDoctorReq = req;
      return Task.FromResult((DoctorHandler ?? throw new InvalidOperationException("DoctorHandler required."))(req));
    }

    public Task<BatchResult> BatchAsync(BatchRequest req, CancellationToken ct = default)
    {
      BatchCalls++;
      LastBatchReq = req;
      return Task.FromResult((BatchHandler ?? throw new InvalidOperationException("BatchHandler required."))(req));
    }

    public Task<WindowListResult> WindowsListAsync(WindowsListRequest req, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<FocusedWindowResult> WindowsFocusedAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<FocusedWindowResult> WindowFocusAsync(WindowFocusRequest req, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<CaptureImageResult> CaptureImageAsync(CaptureImageRequest req, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<UiaSnapshotResult> UiaSnapshotAsync(UiaSnapshotRequest req, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<SeeResult> SeeAsync(SeeRequest req, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<FindResult> FindAsync(FindRequest req, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<ElementGetResult> ElementGetAsync(ElementGetRequest req, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<ActionResult> ClickAsync(ClickRequest req, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<ActionResult> InvokeAsync(InvokeRequest req, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<ActionResult> SetValueAsync(SetValueRequest req, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<ActionResult> TypeAsync(TypeRequest req, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<ActionResult> ScrollAsync(ScrollRequest req, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<ActionResult> HotkeyAsync(HotkeyRequest req, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<ActionResult> PressAsync(PressRequest req, CancellationToken ct = default) => throw new NotImplementedException();
    public IAsyncEnumerable<ObservationEvent> ObserveAsync(ObserveRequest req, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<WaitResult> WaitAsync(WaitRequest req, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<DiffResult> DiffAsync(DiffRequest req, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<ElementAtPointResult> ElementAtPointAsync(ElementAtPointRequest req, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<WindowActionResult> WindowMoveAsync(WindowMoveRequest req, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<WindowActionResult> WindowResizeAsync(WindowResizeRequest req, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<WindowActionResult> WindowSetBoundsAsync(WindowBoundsRequest req, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<WindowActionResult> WindowMinimizeAsync(WindowStateRequest req, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<WindowActionResult> WindowMaximizeAsync(WindowStateRequest req, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<WindowActionResult> WindowRestoreAsync(WindowStateRequest req, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<WindowActionResult> WindowCloseAsync(WindowCloseRequest req, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<AppLaunchResult> AppLaunchAsync(AppLaunchRequest req, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<AppQuitResult> AppQuitAsync(AppQuitRequest req, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<AppRelaunchResult> AppRelaunchAsync(AppRelaunchRequest req, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<AppListResult> AppListAsync(AppListRequest req, CancellationToken ct = default) => throw new NotImplementedException();
  }
}

