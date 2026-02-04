# Windows.Graphics.Capture (WGC) notes

- Date: 2026-02-04
- Scope: `src/peeku.Core/Capture/*`

## Working minimal stack (single-frame PNG)

- `GraphicsCaptureItem` acquisition: Win32 interop (`IGraphicsCaptureItemInterop`) for hwnd/monitor.
- D3D device: `Vortice.Direct3D11` `D3D11CreateDevice` with `BgraSupport`.
- Bridge DXGI -> WinRT `IDirect3DDevice`: P/Invoke `CreateDirect3D11DeviceFromDXGIDevice` (d3d11.dll).
- Capture: `Direct3D11CaptureFramePool.CreateFreeThreaded` + `GraphicsCaptureSession.StartCapture()`.
- Readback: `SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface)`.
- Encode: `BitmapEncoder` PNG to `InMemoryRandomAccessStream`, then read bytes.

## Repo implementation

- `src/peeku.Core/Capture/WgcCapture.cs`: above pipeline.
- `src/peeku.Core/Capture/CaptureImage.cs`: chooses `OutPath` (temp default), writes PNG, returns metadata; `IncludeBase64` not yet implemented.

