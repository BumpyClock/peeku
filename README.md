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

## Docs

- CLI: `docs/cli.md`
- MCP: `docs/mcp.md`
- Selector DSL: `docs/learned/selector.md`
- ElementRef stability: `docs/learned/elementref.md`
- WGC capture notes: `docs/learned/wgc.md`

