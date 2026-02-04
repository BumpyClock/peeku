# Windows.Graphics.Capture (WGC) notes

Scope: `src/peeku.Core` `CaptureImageAsync` implementation path.

## Why placeholder now

- WGC desktop capture needs WinRT projection + D3D11 interop; non-trivial plumbing
- Still missing the actual capture plumbing (interop + readback + encoding); keep API surface stubbed until prototype lands

## Minimal moving parts (desktop .NET)

- OS: Windows 10 1903 (build 18362) or newer
- TFM: `net*-windows10.0.19041.0` + `TargetPlatformMinVersion=10.0.18362.0`
  - WinRT projections for `Windows.Graphics.Capture` come from the Windows-targeted TFM; no extra NuGet needed
- Capture item acquisition:
  - `GraphicsCapturePicker` (UI + dispatcher), or
  - `IGraphicsCaptureItemInterop` for hwnd/monitor -> `GraphicsCaptureItem` (Win32 interop)
- D3D11 interop + readback:
  - Create `ID3D11Device` (e.g., `Vortice.Direct3D11`), wrap to `Windows.Graphics.DirectX.Direct3D11.IDirect3DDevice`
  - `Direct3D11CaptureFramePool` -> `Direct3D11CaptureFrame` -> GPU texture -> CPU readback
- Encode PNG:
  - `Windows.Graphics.Imaging.BitmapEncoder` (WinRT) or managed encoder (tradeoffs)

## Prototype steps

1. Map `Target` to capture item (desktop / screen / hwnd / query -> hwnd)
2. Create D3D11 device + frame pool; capture single frame
3. Copy to CPU; encode PNG; write `OutPath`; optionally return `Base64Png`
