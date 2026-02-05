# .NET baseline (peeku)

- Date: 2026-02-05
- Goal: "latest .NET" for repo; pin SDK in `global.json`

## Findings

- .NET 10.0.2: SDK `10.0.102`, release date 2026-01-13 (matches installed SDK here).
  - Source: dotnet.microsoft.com (.NET 10 downloads) + dotnet blog (January 2026 servicing).
- System.CommandLine `2.0.2` updated 2026-01-13.
  - API differs from older `beta4` docs; use `RootCommand.SetAction(...)`, `Option<T>(name, aliases)` + `DefaultValueFactory`/`Validators`.
  - Source: NuGet Gallery (System.CommandLine).

## Repo decision

- Keep `global.json` pinned to installed `10.0.102`.
- TFM: `net10.0-windows10.0.19041.0` (WinRT projections needed for WGC/UIA stack).

