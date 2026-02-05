# peeku

Windows UI automation + capture tooling.

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

