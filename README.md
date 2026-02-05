# peeku

**Windows UI automation + capture tooling.**

The goal for peeku is to provide a simple CLI + MCP interface to interact with Windows OS and Apps. It provides a simple API and CLI interface that you can use in scripting or with AI agents to allow them to interact with Windows , and installed Apps. This is in active development so expect breaking changes. 

- Library-first: `src/peeku.Core`
- CLI: `src/peeku.Cli`
- MCP server (stdio): `src/peeku.Mcp`

## Prereqs

- Windows
- .NET SDK pinned in `global.json`

## Quickstart

```powershell
dotnet test -c Release
dotnet run --project src/peeku.Cli -c Release -- --help
dotnet run --project src/peeku.Cli -c Release -- --format json windows list --limit 10
dotnet run --project src/peeku.Cli -c Release -- --format json capture image --includeBase64 false
```

## CLI quick reference

Run:

```powershell
dotnet run --project src/peeku.Cli -c Release -- --help
```

Commands:

- `doctor` — environment self-checks
- `windows list` — enumerate windows (filters; `--includeMinimized`)
- `windows focused` — focused window info (HWND, pid, title)
- `capture image` — screenshot to PNG (`--hwnd`, `--out`, `--includeBase64`)
- `uia snapshot` — UIA snapshot tree (`--hwnd`/query; `--depth`, `--maxNodes`)
- `see` — capture + snapshot combined
- `find` — selector → matches
- `element get` — selector/ref → properties + patterns
- `click` / `invoke` / `set-value` / `type` / `scroll` / `hotkey` — actions
- `observe` — UIA events (v1: focus)
- `wait` — poll selector until match (uses `--timeout`)
- `batch` — run JSON ops array (ToolRegistry names)

Examples:

```powershell
dotnet run --project src/peeku.Cli -c Release -- --format json windows list --processName WindowsTerminal --limit 5
dotnet run --project src/peeku.Cli -c Release -- --format json uia snapshot --titleContains PowerShell --depth 3 --maxNodes 500
dotnet run --project src/peeku.Cli -c Release -- --format json click --hwnd 0x0000000000530CB6 --method uia --selector "window/*/*/tab/list/tabitem[name=\"PowerShell\"]"
```

## Docs

- CLI: `docs/cli.md`
- MCP: `docs/mcp.md`
- Selector DSL: `docs/learned/selector.md`
- ElementRef stability: `docs/learned/elementref.md`
- WGC capture notes: `docs/learned/wgc.md`

