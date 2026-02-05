# peeku MCP server

## Run (stdio)

```powershell
dotnet run --project src/peeku.Mcp -c Release
```

Notes:
- MCP protocol over stdin/stdout; logs on stderr.
- Tools surfaced from `peeku.ToolRegistry` (name/title/description + schemas).

## Example client config (command)

```json
{
  "mcpServers": {
    "peeku": {
      "command": "dotnet",
      "args": ["run", "--project", "src/peeku.Mcp", "-c", "Release", "--no-build"]
    }
  }
}
```
