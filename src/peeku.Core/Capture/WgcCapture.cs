using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Runtime.InteropServices.WindowsRuntime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Foundation;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace peeku;

[SupportedOSPlatform("windows10.0.18362.0")]
internal static class WgcCapture
{
  internal sealed record CaptureResult(
    bool Ok,
    byte[]? PngBytes = null,
    int Width = 0,
    int Height = 0,
    string? Warning = null,
    PeekuError? Error = null);

  internal static bool IsSupported()
  {
    try
    {
      return GraphicsCaptureSession.IsSupported();
    }
    catch
    {
      return false;
    }
  }

  internal static string DefaultOutPath(string traceId)
  {
    var file = $"peeku_capture_{Results.TraceId(traceId)}.png";
    return Path.Combine(Path.GetTempPath(), "peeku", file);
  }

  internal static async Task<CaptureResult> CapturePngAsync(GraphicsCaptureItem item, CancellationToken ct)
  {
    if (item is null)
    {
      return new CaptureResult(
        Ok: false,
        Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Capture item is required."));
    }

    try
    {
      ct.ThrowIfCancellationRequested();

      using var d3dDevice = CreateD3DDevice(out var dxgiDevice);
      using (dxgiDevice)
      {
        var direct3DDevice = CreateDirect3DDevice(dxgiDevice);

        var size = item.Size;
        using var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
          direct3DDevice,
          DirectXPixelFormat.B8G8R8A8UIntNormalized,
          numberOfBuffers: 2,
          size: size);

        using var session = framePool.CreateCaptureSession(item);

        var tcs = new TaskCompletionSource<Direct3D11CaptureFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        TypedEventHandler<Direct3D11CaptureFramePool, object> handler = (pool, _) =>
        {
          try
          {
            var frame = pool.TryGetNextFrame();
            if (frame is null)
            {
              return;
            }

            if (!tcs.TrySetResult(frame))
            {
              frame.Dispose();
            }
          }
          catch (Exception ex)
          {
            tcs.TrySetException(ex);
          }
        };

        framePool.FrameArrived += handler;

        try
        {
          session.StartCapture();

          using var frame = await tcs.Task.WaitAsync(ct).ConfigureAwait(false);
          var contentSize = frame.ContentSize;

          using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface).AsTask(ct).ConfigureAwait(false);
          var pngBytes = await EncodePngAsync(bitmap, ct).ConfigureAwait(false);

          return new CaptureResult(
            Ok: true,
            PngBytes: pngBytes,
            Width: contentSize.Width,
            Height: contentSize.Height);
        }
        finally
        {
          framePool.FrameArrived -= handler;
        }
      }
    }
    catch (OperationCanceledException)
    {
      return new CaptureResult(
        Ok: false,
        Error: PeekuErrors.Create(PeekuErrorCode.Canceled, "Operation cancelled."));
    }
    catch (Exception ex)
    {
      return new CaptureResult(
        Ok: false,
        Error: PeekuErrors.Create(
          PeekuErrorCode.Internal,
          "WGC capture failed.",
          new { exception = ex.GetType().FullName, ex.Message, ex.HResult }));
    }
  }

  private static ID3D11Device CreateD3DDevice(out IDXGIDevice dxgiDevice)
  {
    var flags = DeviceCreationFlags.BgraSupport;

    var featureLevels = new[]
    {
      FeatureLevel.Level_11_1,
      FeatureLevel.Level_11_0,
      FeatureLevel.Level_10_1,
      FeatureLevel.Level_10_0,
    };

    var result = D3D11.D3D11CreateDevice(
      adapter: null,
      driverType: DriverType.Hardware,
      flags: flags,
      featureLevels: featureLevels,
      out var device);

    if (result.Failure)
    {
      throw new InvalidOperationException($"D3D11CreateDevice failed: {result.Code}");
    }

    if (device is null)
    {
      throw new InvalidOperationException("D3D11CreateDevice returned null device.");
    }

    dxgiDevice = device.QueryInterface<IDXGIDevice>();
    return device;
  }

  private static IDirect3DDevice CreateDirect3DDevice(IDXGIDevice dxgiDevice)
  {
    var dxgiPtr = dxgiDevice.NativePointer;
    if (dxgiPtr == 0)
    {
      throw new InvalidOperationException("DXGI device pointer was null.");
    }

    var direct3DPtr = default(nint);
    try
    {
      var hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiPtr, out direct3DPtr);
      Marshal.ThrowExceptionForHR(hr);

      return global::WinRT.MarshalInspectable<IDirect3DDevice>.FromAbi(direct3DPtr);
    }
    finally
    {
      if (direct3DPtr != 0)
      {
        global::WinRT.MarshalInspectable<IDirect3DDevice>.DisposeAbi(direct3DPtr);
      }
    }
  }

  private static async Task<byte[]> EncodePngAsync(SoftwareBitmap bitmap, CancellationToken ct)
  {
    using var stream = new InMemoryRandomAccessStream();
    var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream).AsTask(ct).ConfigureAwait(false);
    encoder.SetSoftwareBitmap(bitmap);
    await encoder.FlushAsync().AsTask(ct).ConfigureAwait(false);

    var length = stream.Size;
    if (length <= 0)
    {
      return Array.Empty<byte>();
    }

    if (length > int.MaxValue)
    {
      throw new InvalidOperationException("Encoded PNG too large.");
    }

    using var reader = new DataReader(stream.GetInputStreamAt(0));
    _ = await reader.LoadAsync((uint)length).AsTask(ct).ConfigureAwait(false);

    var bytes = new byte[(int)length];
    reader.ReadBytes(bytes);
    return bytes;
  }

  [DllImport("d3d11.dll", PreserveSig = true)]
  private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);
}
