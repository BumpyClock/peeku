# peeku

**Windows UI automation + capture tooling.**

The goal for peeku is to provide a simple CLI + MCP interface to interact with Windows OS and Apps. It provides a simple API and CLI interface that you can use in scripting or with AI agents to allow them to interact with Windows , and installed Apps. This is in active development so expect breaking changes. 

- Library-first: `src/peeku.Core`
- CLI: `src/peeku.Cli`
- MCP server (stdio): `src/peeku.Mcp`

## Prereqs

- Windows
- .NET SDK pinned in `global.json`

## Install

Publish a self-contained single-file binary and add it to PATH:

```powershell
dotnet publish src/peeku.Cli -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:PublishReadyToRun=true -o dist/
# Add dist\ to PATH, or copy dist\peeku.exe to a directory already on PATH.
# dist\ also contains peeku-daemon.exe and peeku-mcp.exe.
```

## Quickstart

```powershell
peeku --help
peeku --format json windows list --limit 10
peeku --format json capture image --includeBase64 false
peeku click "OK" --app notepad
peeku type "hello" --app notepad
```

## CLI quick reference

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
peeku --format json windows list --processName WindowsTerminal --limit 5
peeku --format json uia snapshot --titleContains PowerShell --depth 3 --maxNodes 500
peeku --format json click --hwnd 0x0000000000530CB6 --method uia --selector "window/*/*/tab/list/tabitem[name=\"PowerShell\"]"
```

## Contributing / dev

Run without publishing via the .NET SDK (pays MSBuild + cold JIT per call):

```powershell
dotnet test -c Release
dotnet run --project src/peeku.Cli -c Release -- --help
dotnet run --project src/peeku.Cli -c Release -- --format json windows list --limit 10
```

## Docs

- CLI: `docs/cli.md`
- MCP: `docs/mcp.md`
- Selector DSL: `docs/learned/selector.md`
- ElementRef stability: `docs/learned/elementref.md`
- WGC capture notes: `docs/learned/wgc.md`
