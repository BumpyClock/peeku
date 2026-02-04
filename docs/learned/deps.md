# Deps decisions

Date: 2026-02-04

## Targets

- .NET: `net10.0-windows10.0.19041.0` (SDK pinned via `global.json` -> 10.0.102)
  - `peeku.Core`: `TargetPlatformMinVersion` = `10.0.18362.0` (WGC free-threaded frame pool)

## Packages (central pin)

- CLI:
  - `System.CommandLine` `2.0.2` (MIT)
  - `Serilog` `4.3.0` (Apache-2.0)
  - `Serilog.Sinks.Console` `6.1.1`
  - `Serilog.Sinks.File` `7.0.0`
  - `Serilog.Formatting.Compact` `2.0.0`
- Core (UI Automation):
  - `FlaUI.Core.Signed` `5.0.0` (MIT)
  - `FlaUI.UIA3.Signed` `5.0.0` (MIT)
- Core (Capture / D3D11):
  - `Vortice.Direct3D11` `3.8.2` (MIT)
  - `Vortice.DXGI` `3.8.2` (MIT)
- MCP:
  - `ModelContextProtocol` `0.7.0-preview.1` (MIT, preview)
  - `Microsoft.Extensions.Hosting` `10.0.2`
  - `Microsoft.Extensions.Logging.Console` `10.0.2`

## Notes

- WinRT / WGC: no NuGet needed; Windows-targeted TFM with explicit `windows10.0.x` platform version provides `Windows.Graphics.Capture` projections.
  - Tried `Microsoft.Windows.SDK.Contracts` (winmd) → `NETSDK1130` (WinMD refs unsupported in .NET 5+).
  - Tried `Microsoft.Windows.CsWinRT` → build required Windows SDK `Platform.xml` (not present here); avoid until we actually need projection generation.
- MCP SDK currently preview; expect churn; keep tool surface behind our own adapters (`ToolDescriptor` plan in PRD).
  - Verified latest stable package versions on NuGet (2026-02-04).

## Refs

- NuGet: System.CommandLine https://www.nuget.org/packages/System.CommandLine
- NuGet: Serilog https://www.nuget.org/packages/Serilog
- NuGet: Serilog.Sinks.File https://www.nuget.org/packages/Serilog.Sinks.File
- NuGet: FlaUI.Core.Signed https://www.nuget.org/packages/FlaUI.Core.Signed
- NuGet: ModelContextProtocol https://www.nuget.org/packages/ModelContextProtocol
- NuGet: Vortice.Direct3D11 https://www.nuget.org/packages/Vortice.Direct3D11
- NuGet: Vortice.DXGI https://www.nuget.org/packages/Vortice.DXGI
