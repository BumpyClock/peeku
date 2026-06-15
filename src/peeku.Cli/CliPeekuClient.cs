namespace peeku.Cli;

internal sealed class CliPeekuClient : global::peeku.IPeekuClient
{
  private readonly global::peeku.IPeekuClient _inner;
  private readonly string? _warning;

  private CliPeekuClient(global::peeku.IPeekuClient inner, string warning)
  {
    _inner = inner;
    _warning = string.IsNullOrWhiteSpace(warning) ? null : warning.Trim();
  }

  // Short budget for the connect/ping probe so a stale marker no longer costs a full
  // command --timeout (plan §6). PID-liveness already filters dead/mismatched markers.
  private static readonly TimeSpan ProbeBudget = TimeSpan.FromMilliseconds(300);

  internal static global::peeku.IPeekuClient CreateDefault()
  {
    if (!DaemonMarker.TryLoad(out var marker))
    {
      return new CliPeekuClient(new global::peeku.WindowsClient(), "");
    }

    // PID-liveness before the pipe ping: if the daemon process is dead or its PID was
    // recycled to an unrelated process, drop the stale marker and skip the round-trip.
    if (!marker.IsAlive())
    {
      DaemonMarker.TryDeleteStale();
      return new CliPeekuClient(new global::peeku.WindowsClient(), "");
    }

    var rpc = new DaemonJsonRpcClient(marker.PipeName, ProbeBudget);
    using var cts = new CancellationTokenSource(ProbeBudget);
    var pingOk = rpc.TryPingAsync(cts.Token).GetAwaiter().GetResult();
    if (pingOk)
    {
      return new CliPeekuClient(new DaemonPeekuClient(rpc), "");
    }

    var warning = "Daemon unreachable; fell back to in-proc";
    var fallback = new WarningPeekuClient(new global::peeku.WindowsClient(), warning);
    return new CliPeekuClient(fallback, warning);
  }

  public Task<global::peeku.DoctorResult> DoctorAsync(global::peeku.DoctorRequest req, CancellationToken ct = default)
    => CliDoctor.DoctorAsync(_inner, req, _warning ?? "", ct);

  public Task<global::peeku.WindowListResult> WindowsListAsync(global::peeku.WindowsListRequest req, CancellationToken ct = default)
    => _inner.WindowsListAsync(req, ct);

  public Task<global::peeku.FocusedWindowResult> WindowsFocusedAsync(CancellationToken ct = default)
    => _inner.WindowsFocusedAsync(ct);

  public Task<global::peeku.FocusedWindowResult> WindowFocusAsync(global::peeku.WindowFocusRequest req, CancellationToken ct = default)
    => _inner.WindowFocusAsync(req, ct);

  public Task<global::peeku.CaptureImageResult> CaptureImageAsync(global::peeku.CaptureImageRequest req, CancellationToken ct = default)
    => _inner.CaptureImageAsync(req, ct);

  public Task<global::peeku.UiaSnapshotResult> UiaSnapshotAsync(global::peeku.UiaSnapshotRequest req, CancellationToken ct = default)
    => _inner.UiaSnapshotAsync(req, ct);

  public Task<global::peeku.SeeResult> SeeAsync(global::peeku.SeeRequest req, CancellationToken ct = default)
    => _inner.SeeAsync(req, ct);

  public Task<global::peeku.FindResult> FindAsync(global::peeku.FindRequest req, CancellationToken ct = default)
    => _inner.FindAsync(req, ct);

  public Task<global::peeku.ElementGetResult> ElementGetAsync(global::peeku.ElementGetRequest req, CancellationToken ct = default)
    => _inner.ElementGetAsync(req, ct);

  public Task<global::peeku.ActionResult> ClickAsync(global::peeku.ClickRequest req, CancellationToken ct = default)
    => _inner.ClickAsync(req, ct);

  public Task<global::peeku.ActionResult> InvokeAsync(global::peeku.InvokeRequest req, CancellationToken ct = default)
    => _inner.InvokeAsync(req, ct);

  public Task<global::peeku.ActionResult> SetValueAsync(global::peeku.SetValueRequest req, CancellationToken ct = default)
    => _inner.SetValueAsync(req, ct);

  public Task<global::peeku.ActionResult> TypeAsync(global::peeku.TypeRequest req, CancellationToken ct = default)
    => _inner.TypeAsync(req, ct);

  public Task<global::peeku.ActionResult> ScrollAsync(global::peeku.ScrollRequest req, CancellationToken ct = default)
    => _inner.ScrollAsync(req, ct);

  public Task<global::peeku.ActionResult> HotkeyAsync(global::peeku.HotkeyRequest req, CancellationToken ct = default)
    => _inner.HotkeyAsync(req, ct);

  public Task<global::peeku.ActionResult> PressAsync(global::peeku.PressRequest req, CancellationToken ct = default)
    => _inner.PressAsync(req, ct);

  public IAsyncEnumerable<global::peeku.ObservationEvent> ObserveAsync(global::peeku.ObserveRequest req, CancellationToken ct = default)
    => _inner.ObserveAsync(req, ct);

  public Task<global::peeku.WaitResult> WaitAsync(global::peeku.WaitRequest req, CancellationToken ct = default)
    => _inner.WaitAsync(req, ct);

  public Task<global::peeku.BatchResult> BatchAsync(global::peeku.BatchRequest req, CancellationToken ct = default)
    => _inner.BatchAsync(req, ct);

  public Task<global::peeku.DiffResult> DiffAsync(global::peeku.DiffRequest req, CancellationToken ct = default)
    => _inner.DiffAsync(req, ct);
  public Task<global::peeku.ElementAtPointResult> ElementAtPointAsync(global::peeku.ElementAtPointRequest req, CancellationToken ct = default)
    => _inner.ElementAtPointAsync(req, ct);
}

internal static class CliDoctor
{
  internal static async Task<global::peeku.DoctorResult> DoctorAsync(
    global::peeku.IPeekuClient client,
    global::peeku.DoctorRequest req,
    string warning,
    CancellationToken ct)
  {
    var ctx = CliContextAccessor.Current;
    var scope = global::peeku.Results.Start(ctx.TraceId);

    try
    {
      ct.ThrowIfCancellationRequested();

      var checks = new List<global::peeku.DoctorCheck>(capacity: req.Deep ? 8 : 4);

      checks.Add(new global::peeku.DoctorCheck(
        Name: "os.windows",
        Ok: OperatingSystem.IsWindows(),
        Details: Environment.OSVersion.VersionString));

      var (wgcOk, wgcDetails) = CheckWgcSupport();
      checks.Add(new global::peeku.DoctorCheck(
        Name: "capture.wgcSupported",
        Ok: wgcOk,
        Details: wgcDetails));

      if (req.Deep)
      {
        var focused = await client.WindowsFocusedAsync(ct).ConfigureAwait(false);
        checks.Add(new global::peeku.DoctorCheck(
          Name: "windows.focused",
          Ok: focused.Ok && focused.Window is not null,
          Details: focused.Window is null ? focused.Meta.Warning : $"{focused.Window.ProcessName ?? "?"} {focused.Window.Title ?? "?"} ({focused.Window.HwndHex})"));

        if (focused.Ok && focused.Window is not null)
        {
          var img = await client.CaptureImageAsync(new global::peeku.CaptureImageRequest(
            Target: global::peeku.Target.Focused(),
            OutPath: null,
            IncludeBase64: false), ct).ConfigureAwait(false);

          checks.Add(new global::peeku.DoctorCheck(
            Name: "capture.imageFocused",
            Ok: img.Ok,
            Details: img.Ok ? img.ImagePath : img.Error?.Message));

          var snap = await client.UiaSnapshotAsync(new global::peeku.UiaSnapshotRequest(
            Target: global::peeku.Target.Focused(),
            Depth: 1,
            MaxNodes: 200,
            IncludeProperties: global::peeku.UiaPropertiesMode.Basic), ct).ConfigureAwait(false);

          checks.Add(new global::peeku.DoctorCheck(
            Name: "uia.snapshotFocused",
            Ok: snap.Ok,
            Details: snap.Ok ? snap.SnapshotId : snap.Error?.Message));
        }
        else
        {
          checks.Add(new global::peeku.DoctorCheck(
            Name: "capture.imageFocused",
            Ok: false,
            Details: "No focused window."));

          checks.Add(new global::peeku.DoctorCheck(
            Name: "uia.snapshotFocused",
            Ok: false,
            Details: "No focused window."));
        }
      }

      var ok = checks.All(c => c.Ok);
      var meta = warning.Length == 0 ? scope.Meta() : scope.Meta(warning);
      return new global::peeku.DoctorResult(
        Ok: ok,
        Meta: meta,
        Checks: checks,
        Error: ok ? null : global::peeku.PeekuErrors.Create(global::peeku.PeekuErrorCode.Unavailable, "One or more checks failed"));
    }
    catch (OperationCanceledException)
    {
      var meta = warning.Length == 0 ? scope.Meta() : scope.Meta(warning);
      return new global::peeku.DoctorResult(
        Ok: false,
        Meta: meta,
        Checks: Array.Empty<global::peeku.DoctorCheck>(),
        Error: global::peeku.PeekuErrors.Create(global::peeku.PeekuErrorCode.Canceled, "Operation cancelled"));
    }
    catch (Exception ex)
    {
      var meta = warning.Length == 0 ? scope.Meta() : scope.Meta(warning);
      return new global::peeku.DoctorResult(
        Ok: false,
        Meta: meta,
        Checks: Array.Empty<global::peeku.DoctorCheck>(),
        Error: global::peeku.PeekuErrors.Create(
          global::peeku.PeekuErrorCode.Internal,
          "Doctor failed",
          new { exception = ex.GetType().FullName, ex.Message, ex.HResult }));
    }
  }

  private static (bool Ok, string? Details) CheckWgcSupport()
  {
    try
    {
      var supported = Windows.Graphics.Capture.GraphicsCaptureSession.IsSupported();
      return (supported, supported ? null : "GraphicsCaptureSession.IsSupported() returned false");
    }
    catch (Exception ex)
    {
      return (false, $"{ex.GetType().Name}: {ex.Message}");
    }
  }
}
