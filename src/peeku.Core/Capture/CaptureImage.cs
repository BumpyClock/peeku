namespace peeku;

internal static class CaptureImage
{
  internal static async Task<CaptureImageResult> CaptureImageAsync(CaptureImageRequest req, CancellationToken ct)
  {
    var scope = Results.Start();

    try
    {
      ct.ThrowIfCancellationRequested();

      if (req is null)
      {
        return new CaptureImageResult(
          Ok: false,
          Meta: scope.Meta(),
          ImagePath: "",
          MimeType: "image/png",
          Width: 0,
          Height: 0,
          Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Request required."));
      }

      if (req.Target is null)
      {
        return new CaptureImageResult(
          Ok: false,
          Meta: scope.Meta(),
          ImagePath: "",
          MimeType: "image/png",
          Width: 0,
          Height: 0,
          Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Target required."));
      }

      if (!WgcCapture.IsSupported())
      {
        return new CaptureImageResult(
          Ok: false,
          Meta: scope.Meta(),
          ImagePath: "",
          MimeType: "image/png",
          Width: 0,
          Height: 0,
          Error: PeekuErrors.Create(
            PeekuErrorCode.Unavailable,
            "Windows.Graphics.Capture not supported on this OS."));
      }

      var outPath = string.IsNullOrWhiteSpace(req.OutPath)
        ? WgcCapture.DefaultOutPath(scope.TraceId)
        : Path.GetFullPath(req.OutPath.Trim());

      var dir = Path.GetDirectoryName(outPath);
      if (!string.IsNullOrWhiteSpace(dir))
      {
        Directory.CreateDirectory(dir);
      }

      if (!GraphicsCaptureItemFactory.TryCreate(req.Target, ct, out var item, out var itemError))
      {
        return new CaptureImageResult(
          Ok: false,
          Meta: scope.Meta(),
          ImagePath: outPath,
          MimeType: "image/png",
          Width: 0,
          Height: 0,
          Error: itemError ?? PeekuErrors.Create(PeekuErrorCode.Internal, "Failed to create capture item."));
      }

      if (item is null)
      {
        return new CaptureImageResult(
          Ok: false,
          Meta: scope.Meta(),
          ImagePath: outPath,
          MimeType: "image/png",
          Width: 0,
          Height: 0,
          Error: PeekuErrors.Create(PeekuErrorCode.Internal, "Capture item was null."));
      }

      var warning = req.IncludeBase64 ? "includeBase64 not implemented yet; returning Base64Png=null." : null;

      var capture = await WgcCapture.CapturePngAsync(item, ct).ConfigureAwait(false);
      if (!capture.Ok || capture.PngBytes is null)
      {
        return new CaptureImageResult(
          Ok: false,
          Meta: scope.Meta(warning: capture.Warning),
          ImagePath: outPath,
          MimeType: "image/png",
          Width: 0,
          Height: 0,
          Error: capture.Error ?? PeekuErrors.Create(PeekuErrorCode.Internal, "Capture failed."));
      }

      await File.WriteAllBytesAsync(outPath, capture.PngBytes, ct).ConfigureAwait(false);

      return new CaptureImageResult(
        Ok: true,
        Meta: scope.Meta(warning: CombineWarnings(capture.Warning, warning)),
        ImagePath: outPath,
        MimeType: "image/png",
        Width: capture.Width,
        Height: capture.Height,
        Base64Png: null);
    }
    catch (OperationCanceledException)
    {
      return new CaptureImageResult(
        Ok: false,
        Meta: scope.Meta(),
        ImagePath: "",
        MimeType: "image/png",
        Width: 0,
        Height: 0,
        Error: PeekuErrors.Create(PeekuErrorCode.Canceled, "Operation cancelled."));
    }
    catch (Exception ex)
    {
      return new CaptureImageResult(
        Ok: false,
        Meta: scope.Meta(),
        ImagePath: "",
        MimeType: "image/png",
        Width: 0,
        Height: 0,
        Error: PeekuErrors.Create(
          PeekuErrorCode.Internal,
          "Capture image failed.",
          new { exception = ex.GetType().FullName, ex.Message, ex.HResult }));
    }
  }

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
