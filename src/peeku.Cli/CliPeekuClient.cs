namespace peeku.Cli;

internal sealed class CliPeekuClient : global::peeku.IPeekuClient
{
  private readonly global::peeku.IPeekuClient _inner;

  private CliPeekuClient(global::peeku.IPeekuClient inner)
  {
    _inner = inner;
  }

  internal static global::peeku.IPeekuClient CreateDefault()
    => new CliPeekuClient(new global::peeku.WindowsClient());

  public Task<global::peeku.DoctorResult> DoctorAsync(global::peeku.DoctorRequest req, CancellationToken ct = default)
    => CliDoctor.DoctorAsync(_inner, req, ct);

  public Task<global::peeku.WindowListResult> WindowsListAsync(global::peeku.WindowsListRequest req, CancellationToken ct = default)
    => _inner.WindowsListAsync(req, ct);

  public Task<global::peeku.FocusedWindowResult> WindowsFocusedAsync(CancellationToken ct = default)
    => _inner.WindowsFocusedAsync(ct);

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

  public IAsyncEnumerable<global::peeku.ObservationEvent> ObserveAsync(global::peeku.ObserveRequest req, CancellationToken ct = default)
    => _inner.ObserveAsync(req, ct);

  public Task<global::peeku.WaitResult> WaitAsync(global::peeku.WaitRequest req, CancellationToken ct = default)
    => _inner.WaitAsync(req, ct);

  public Task<global::peeku.BatchResult> BatchAsync(global::peeku.BatchRequest req, CancellationToken ct = default)
    => _inner.BatchAsync(req, ct);
}

internal static class CliDoctor
{
  internal static async Task<global::peeku.DoctorResult> DoctorAsync(
    global::peeku.IPeekuClient client,
    global::peeku.DoctorRequest req,
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
      return new global::peeku.DoctorResult(
        Ok: ok,
        Meta: scope.Meta(),
        Checks: checks,
        Error: ok ? null : global::peeku.PeekuErrors.Create(global::peeku.PeekuErrorCode.Unavailable, "One or more checks failed"));
    }
    catch (OperationCanceledException)
    {
      return new global::peeku.DoctorResult(
        Ok: false,
        Meta: scope.Meta(),
        Checks: Array.Empty<global::peeku.DoctorCheck>(),
        Error: global::peeku.PeekuErrors.Create(global::peeku.PeekuErrorCode.Canceled, "Operation cancelled"));
    }
    catch (Exception ex)
    {
      return new global::peeku.DoctorResult(
        Ok: false,
        Meta: scope.Meta(),
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
