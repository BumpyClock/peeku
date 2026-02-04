namespace peeku;

internal static class CaptureImage
{
  internal static Task<CaptureImageResult> CaptureImageAsync(CaptureImageRequest req, CancellationToken ct)
  {
    var scope = Results.Start();

    try
    {
      ct.ThrowIfCancellationRequested();

      if (req is null)
      {
        return Task.FromResult(new CaptureImageResult(
          Ok: false,
          Meta: scope.Meta(),
          ImagePath: "",
          MimeType: "image/png",
          Width: 0,
          Height: 0,
          Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Request required.")));
      }

      if (req.Target is null)
      {
        return Task.FromResult(new CaptureImageResult(
          Ok: false,
          Meta: scope.Meta(),
          ImagePath: "",
          MimeType: "image/png",
          Width: 0,
          Height: 0,
          Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Target required.")));
      }

      var outPath = string.IsNullOrWhiteSpace(req.OutPath) ? "" : req.OutPath.Trim();
      var targetKind = req.Target switch
      {
        Target.Desktop => "Desktop",
        Target.FocusedWindow => "FocusedWindow",
        Target.Screen s => $"Screen({s.ScreenIndex})",
        Target.WindowByHwnd h => $"WindowByHwnd({h.HwndHex})",
        Target.WindowByQuery => "WindowByQuery",
        _ => req.Target.GetType().Name,
      };

      return Task.FromResult(new CaptureImageResult(
        Ok: false,
        Meta: scope.Meta(warning: "capture_image not implemented yet (WGC prototype pending)."),
        ImagePath: outPath,
        MimeType: "image/png",
        Width: 0,
        Height: 0,
        Base64Png: null,
        Error: PeekuErrors.Create(
          PeekuErrorCode.NotSupported,
          "Capture image not implemented yet. See src/peeku.Core/Capture/WGC_NOTES.md.",
          new
          {
            target = targetKind,
            includeBase64 = req.IncludeBase64,
            outPath = outPath.Length == 0 ? null : outPath,
            wgc = new
            {
              minWindowsBuild = 18362,
              minWindowsVersion = "Windows 10 1903",
              tfmHint = "net*-windows10.0.18362.0 (or higher) for WinRT projections",
              packagesLikelyNeeded = new[]
              {
                "Microsoft.WindowsAppSDK OR Microsoft.Windows.SDK.NET (WinRT projections)",
                "Windows.Win32 (CsWin32) for COM/PInvoke interop (optional but likely)",
                "Vortice.Windows (or similar) for D3D11 device creation (SharpDX deprecated)",
              },
              apis = new[]
              {
                "Windows.Graphics.Capture (GraphicsCaptureItem, Direct3D11CaptureFramePool, GraphicsCaptureSession)",
                "Windows.Graphics.DirectX.Direct3D11 (IDirect3DDevice/IDirect3DSurface)",
                "IGraphicsCaptureItemInterop (hwnd/monitor -> GraphicsCaptureItem)",
              },
            },
          })));
    }
    catch (OperationCanceledException)
    {
      return Task.FromResult(new CaptureImageResult(
        Ok: false,
        Meta: scope.Meta(),
        ImagePath: "",
        MimeType: "image/png",
        Width: 0,
        Height: 0,
        Error: PeekuErrors.Create(PeekuErrorCode.Canceled, "Operation cancelled.")));
    }
    catch (Exception ex)
    {
      return Task.FromResult(new CaptureImageResult(
        Ok: false,
        Meta: scope.Meta(),
        ImagePath: "",
        MimeType: "image/png",
        Width: 0,
        Height: 0,
        Error: PeekuErrors.Create(
          PeekuErrorCode.Internal,
          "Capture image failed.",
          new { exception = ex.GetType().FullName, ex.Message, ex.HResult })));
    }
  }
}

