# Deps decisions

Date: 2026-02-04

## Targets

- .NET: `net10.0-windows` (SDK pinned via `global.json`)

## Packages (central pin)

- CLI:
  - `System.CommandLine` `2.0.2` (MIT)
  - `Serilog` `4.3.0` (Apache-2.0)
  - `Serilog.Sinks.Console` `6.1.1`
  - `Serilog.Sinks.File` `7.0.0`
  - `Serilog.Formatting.Compact` `3.0.0`
- Core (UI Automation):
  - `FlaUI.Core.Signed` `5.0.0` (MIT)
  - `FlaUI.UIA3.Signed` `5.0.0` (MIT)
- MCP:
  - `ModelContextProtocol` `0.7.0-preview.1` (MIT, preview)
  - `Microsoft.Extensions.Hosting` `10.0.2`
  - `Microsoft.Extensions.Logging.Console` `10.0.2`

## Notes

- WinRT / WGC: rely on Windows-targeted TFM + SDK-provided WinRT projections; add explicit Windows SDK ref package only if we hit missing API surface.
- MCP SDK currently preview; expect churn; keep tool surface behind our own adapters (`ToolDescriptor` plan in PRD).

## Refs

- NuGet: System.CommandLine https://www.nuget.org/packages/System.CommandLine
- NuGet: Serilog https://www.nuget.org/packages/Serilog
- NuGet: Serilog.Sinks.File https://www.nuget.org/packages/Serilog.Sinks.File
- NuGet: FlaUI.Core.Signed https://www.nuget.org/packages/FlaUI.Core.Signed
- NuGet: ModelContextProtocol https://www.nuget.org/packages/ModelContextProtocol
