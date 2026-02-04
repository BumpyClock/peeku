namespace peeku;

public sealed class WindowsClient : IPeekuClient
{
  public Task<WindowListResult> WindowsListAsync(WindowsListRequest req, CancellationToken ct = default)
  {
    var scope = Results.Start();
    try
    {
      var windows = Win32Windows.ListWindows(req, ct);
      return Task.FromResult(new WindowListResult(
        Ok: true,
        Meta: scope.Meta(),
        Windows: windows));
    }
    catch (OperationCanceledException)
    {
      return Task.FromResult(new WindowListResult(
        Ok: false,
        Meta: scope.Meta(),
        Windows: Array.Empty<WindowInfo>(),
        Error: PeekuErrors.Create(PeekuErrorCode.Canceled, "Operation cancelled.")));
    }
    catch (Exception ex)
    {
      return Task.FromResult(new WindowListResult(
        Ok: false,
        Meta: scope.Meta(),
        Windows: Array.Empty<WindowInfo>(),
        Error: PeekuErrors.Create(
          PeekuErrorCode.Internal,
          "Windows list failed.",
          new { exception = ex.GetType().FullName, ex.Message, ex.HResult })));
    }
  }

  public Task<FocusedWindowResult> WindowsFocusedAsync(CancellationToken ct = default)
  {
    var scope = Results.Start();
    try
    {
      ct.ThrowIfCancellationRequested();

      var window = Win32Windows.GetFocusedWindow();
      return Task.FromResult(new FocusedWindowResult(
        Ok: true,
        Meta: scope.Meta(warning: window is null ? "No foreground window." : null),
        Window: window));
    }
    catch (OperationCanceledException)
    {
      return Task.FromResult(new FocusedWindowResult(
        Ok: false,
        Meta: scope.Meta(),
        Window: null,
        Error: PeekuErrors.Create(PeekuErrorCode.Canceled, "Operation cancelled.")));
    }
    catch (Exception ex)
    {
      return Task.FromResult(new FocusedWindowResult(
        Ok: false,
        Meta: scope.Meta(),
        Window: null,
        Error: PeekuErrors.Create(
          PeekuErrorCode.Internal,
          "Windows focused failed.",
          new { exception = ex.GetType().FullName, ex.Message, ex.HResult })));
    }
  }

  public Task<DoctorResult> DoctorAsync(DoctorRequest req, CancellationToken ct = default)
    => throw new NotImplementedException();

  public Task<CaptureImageResult> CaptureImageAsync(CaptureImageRequest req, CancellationToken ct = default)
    => CaptureImage.CaptureImageAsync(req, ct);

  public Task<UiaSnapshotResult> UiaSnapshotAsync(UiaSnapshotRequest req, CancellationToken ct = default)
    => throw new NotImplementedException();

  public async Task<SeeResult> SeeAsync(SeeRequest req, CancellationToken ct = default)
  {
    var scope = Results.Start();

    try
    {
      ct.ThrowIfCancellationRequested();

      if (req is null)
      {
        return new SeeResult(
          Ok: false,
          Meta: scope.Meta(),
          Image: new CaptureImageResult(
            Ok: false,
            Meta: scope.Meta(),
            ImagePath: "",
            MimeType: "image/png",
            Width: 0,
            Height: 0,
            Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Request is required.")),
          SnapshotId: "",
          Elements: Array.Empty<UiaElement>(),
          Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Request is required."));
      }

      var captureReq = new CaptureImageRequest(req.Target, OutPath: null, IncludeBase64: req.IncludeBase64);
      var image = await CaptureImageAsync(captureReq, ct).ConfigureAwait(false);
      if (!image.Ok)
      {
        return new SeeResult(
          Ok: false,
          Meta: scope.Meta(warning: image.Meta.Warning),
          Image: image,
          SnapshotId: "",
          Elements: Array.Empty<UiaElement>(),
          Error: image.Error ?? PeekuErrors.Create(PeekuErrorCode.Internal, "Capture failed."));
      }

      var uia = new UiaClient();
      var snapshot = await uia.UiaSnapshotAsync(
        new UiaSnapshotRequest(
          Target: req.Target,
          Depth: req.Depth,
          MaxNodes: req.MaxNodes,
          IncludeProperties: req.IncludeProperties),
        ct).ConfigureAwait(false);

      if (!snapshot.Ok)
      {
        return new SeeResult(
          Ok: false,
          Meta: scope.Meta(warning: CombineWarnings(image.Meta.Warning, snapshot.Meta.Warning)),
          Image: image,
          SnapshotId: snapshot.SnapshotId,
          Elements: snapshot.Elements,
          Error: snapshot.Error ?? PeekuErrors.Create(PeekuErrorCode.Internal, "UIA snapshot failed."));
      }

      return new SeeResult(
        Ok: true,
        Meta: scope.Meta(warning: CombineWarnings(image.Meta.Warning, snapshot.Meta.Warning)),
        Image: image,
        SnapshotId: snapshot.SnapshotId,
        Elements: snapshot.Elements);
    }
    catch (OperationCanceledException)
    {
      return new SeeResult(
        Ok: false,
        Meta: scope.Meta(),
        Image: new CaptureImageResult(
          Ok: false,
          Meta: scope.Meta(),
          ImagePath: "",
          MimeType: "image/png",
          Width: 0,
          Height: 0,
          Error: PeekuErrors.Create(PeekuErrorCode.Canceled, "Operation cancelled.")),
        SnapshotId: "",
        Elements: Array.Empty<UiaElement>(),
        Error: PeekuErrors.Create(PeekuErrorCode.Canceled, "Operation cancelled."));
    }
    catch (Exception ex)
    {
      return new SeeResult(
        Ok: false,
        Meta: scope.Meta(),
        Image: new CaptureImageResult(
          Ok: false,
          Meta: scope.Meta(),
          ImagePath: "",
          MimeType: "image/png",
          Width: 0,
          Height: 0,
          Error: PeekuErrors.Create(PeekuErrorCode.Internal, "See failed.", new { exception = ex.GetType().FullName, ex.Message, ex.HResult })),
        SnapshotId: "",
        Elements: Array.Empty<UiaElement>(),
        Error: PeekuErrors.Create(
          PeekuErrorCode.Internal,
          "See failed.",
          new { exception = ex.GetType().FullName, ex.Message, ex.HResult }));
    }
  }

  public Task<FindResult> FindAsync(FindRequest req, CancellationToken ct = default)
    => throw new NotImplementedException();

  public Task<ElementGetResult> ElementGetAsync(ElementGetRequest req, CancellationToken ct = default)
    => throw new NotImplementedException();

  public Task<ActionResult> ClickAsync(ClickRequest req, CancellationToken ct = default)
    => throw new NotImplementedException();

  public Task<ActionResult> InvokeAsync(InvokeRequest req, CancellationToken ct = default)
    => throw new NotImplementedException();

  public Task<ActionResult> SetValueAsync(SetValueRequest req, CancellationToken ct = default)
    => throw new NotImplementedException();

  public Task<ActionResult> TypeAsync(TypeRequest req, CancellationToken ct = default)
    => throw new NotImplementedException();

  public Task<ActionResult> ScrollAsync(ScrollRequest req, CancellationToken ct = default)
    => throw new NotImplementedException();

  public Task<ActionResult> HotkeyAsync(HotkeyRequest req, CancellationToken ct = default)
    => throw new NotImplementedException();

  public IAsyncEnumerable<ObservationEvent> ObserveAsync(ObserveRequest req, CancellationToken ct = default)
    => throw new NotImplementedException();

  public Task<WaitResult> WaitAsync(WaitRequest req, CancellationToken ct = default)
    => throw new NotImplementedException();

  public Task<BatchResult> BatchAsync(BatchRequest req, CancellationToken ct = default)
    => throw new NotImplementedException();

  private static string? CombineWarnings(string? a, string? b)
  {
    if (string.IsNullOrWhiteSpace(a))
    {
      return string.IsNullOrWhiteSpace(b) ? null : b;
    }

    if (string.IsNullOrWhiteSpace(b))
    {
      return a;
    }

    return $"{a} {b}";
  }
}
