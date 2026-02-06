# peeku MCP server

## Run (stdio)

```powershell
dotnet run --project src/peeku.Mcp -c Release
```

Notes:
- MCP protocol over stdin/stdout; logs on stderr.
- Tools surfaced from `peeku.ToolRegistry` (name/title/description + schemas).

## Tools

Names:

- `peeku_doctor`
- `peeku_windows_list`
- `peeku_windows_focused`
- `peeku_capture_image`
- `peeku_uia_snapshot`
- `peeku_see`
- `peeku_find`
- `peeku_element_get`
- `peeku_click`
- `peeku_invoke`
- `peeku_set_value`
- `peeku_type`
- `peeku_scroll`
- `peeku_hotkey`
- `peeku_observe`
- `peeku_wait`
- `peeku_batch`

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

## Example tool calls (arguments)

These are `arguments` passed to MCP `tools/call` for the named tool.

### `peeku_uia_snapshot`

```json
{
  "target": { "kind": "focused_window" },
  "depth": 2,
  "maxNodes": 200,
  "includeProperties": "basic"
}
```

### `peeku_see`

```json
{
  "target": { "kind": "window_query", "query": { "titleContains": "PowerShell", "processName": "WindowsTerminal" } },
  "depth": 2,
  "maxNodes": 500,
  "includeProperties": "basic",
  "includeBase64": false
}
```

### `peeku_find`

```json
{
  "selector": { "expr": "window[name~=\"PowerShell\"]/tab" },
  "target": { "kind": "window_hwnd", "hwndHex": "0x0000000000530CB6" },
  "limit": 5
}
```

Notes:
- `selector.preferCachedSnapshot` (optional, default `true`): set `false` for live UIA evaluation (no snapshot)

### `peeku_click` (activate tabs via UIA)

```json
{
  "selector": { "expr": "window[name~=\"PowerShell\"]/*/*/tab/list/tabitem[name=\"PowerShell\"]" },
  "target": { "kind": "window_hwnd", "hwndHex": "0x0000000000530CB6" },
  "method": "uia"
}
```

## Response shape (v1)

- `structuredContent`: always present; JSON object (tool result).
- `content[0]`: `"type":"text"` JSON string (compat).
- capture/see: if base64 included, `content` also includes `"type":"image"` with `{ data, mimeType }`.
- action tools `peeku_set_value` and `peeku_type` include `evidence` in result payload:
  - `operation`, `status`, `valuePatternSupported`, `expectedValue`, `actualValue`, `verificationPerformed`, `verificationMatched`
