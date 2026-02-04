using System.Runtime.Versioning;

using Vortice.Direct3D11;
using Vortice.DXGI;

using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace peeku;

[SupportedOSPlatform("windows10.0.17134.0")]
internal static class WgcStackProbe
{
  internal static void Touch()
  {
    _ = typeof(GraphicsCaptureItem);
    _ = typeof(Direct3D11CaptureFramePool);
    _ = typeof(Direct3D11CaptureFrame);
    _ = typeof(GraphicsCaptureSession);
    _ = typeof(GraphicsCapturePicker);
    _ = typeof(DirectXPixelFormat);
    _ = typeof(DirectXAlphaMode);
    _ = typeof(IDirect3DDevice);
    _ = typeof(IDirect3DSurface);

    _ = typeof(ID3D11Device);
    _ = typeof(ID3D11Texture2D);
    _ = typeof(IDXGIFactory);
  }
}

