# .NET baseline (peeku)

- Date: 2026-02-04
- Goal: "latest .NET" for repo; pin SDK in `global.json`

## Findings

- .NET 10 SDK `10.0.102` appears current for early 2026 servicing (matches installed SDK here).
  - Track via .NET SDK release notes + GitHub `dotnet/sdk` tags.

## Repo decision

- Keep `global.json` pinned to installed `10.0.102`.
- TFM: `net10.0-windows10.0.19041.0` (WinRT projections needed for WGC/UIA stack).

