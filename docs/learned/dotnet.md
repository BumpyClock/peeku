# .NET baseline (peeku)

- Date: 2026-02-05
- Goal: "latest .NET" for repo; pin SDK in `global.json`

## Findings

- SDK pinned via `global.json` (use that for builds/tests).
- CLI uses `System.CommandLine` (current API shape: `Command.SetAction(...)`, `Option<T>.DefaultValueFactory`, `Option.Validators`, `Option.Recursive=true`).

## Repo decision

- Keep `global.json` pinned to installed `10.0.102`.
- TFM: `net10.0-windows10.0.19041.0` (WinRT projections needed for WGC/UIA stack).

