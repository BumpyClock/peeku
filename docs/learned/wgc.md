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
- `src/peeku.Core/Capture/CaptureImage.cs`: chooses `OutPath` (temp default), writes PNG, returns metadata; `IncludeBase64` supported.
- `src/peeku.Core/Capture/GraphicsCaptureItemFactory.cs`:
  - uses `IGraphicsCaptureItemInterop` vtable call (no RCW cast) + correct `IGraphicsCaptureItem` IID (`79C3F95B-31F7-4EC2-A464-632EF5D30760`).
  - wraps returned WinRT ABI pointers with `WinRT.MarshalInspectable<T>.FromAbi(...)`.
- `src/peeku.Core/Capture/WgcCapture.cs`:
  - wraps `CreateDirect3D11DeviceFromDXGIDevice` result via `WinRT.MarshalInspectable<IDirect3DDevice>.FromAbi(...)` (avoid `System.__ComObject` CCW issues).
