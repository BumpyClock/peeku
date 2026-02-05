# .NET 10 notes (peeku)

## Read when

- Picking target frameworks / SDK versions
- Working with `.slnx`

## What matters for this repo

- Use the SDK version pinned in `global.json` (repro builds).
- Solution format: `.slnx` is supported (this repo uses `peeku.slnx`).
  - If you need legacy `.sln`: `dotnet new sln --format sln`.

## Repo alignment

- `src/peeku.Core` targets `net10.0-windows10.0.19041.0`.
- `global.json` pins SDK `10.0.102` (good for reproducible builds).

## References

- `.slnx` default behavior (SDK 10): https://learn.microsoft.com/dotnet/core/compatibility/sdk/10.0/dotnet-new-sln-slnx-default
