# .NET 10 (LTS) notes

## Read when

- Picking target frameworks / SDK versions
- Working with `.slnx`

## Facts (as of 2026-02-05)

- Latest supported major: **.NET 10** (LTS). Original release **2025-11-11**.
- Latest patch listed: **10.0.2** (patch Tuesday **2026-01-13**).
- .NET 10 SDK behavior: `dotnet new sln` defaults to **`.slnx`**; use `--format sln` for legacy `.sln`.

## Repo alignment

- `src/peeku.Core` targets `net10.0-windows10.0.19041.0`.
- `global.json` pins SDK `10.0.102` (good for reproducible builds).

## Sources

- .NET support policy: https://dotnet.microsoft.com/platform/support/policy/dotnet-core
- .NET 10 announcement: https://devblogs.microsoft.com/dotnet/announcing-dotnet-10
- `.slnx` default breaking change: https://learn.microsoft.com/dotnet/core/compatibility/sdk/10.0/dotnet-new-sln-slnx-default

