# MCP (Model Context Protocol) notes (peeku)

- Date: 2026-02-05
- Package: `ModelContextProtocol` `0.7.0-preview.1`

## Findings

- Tools responses: populate `structuredContent` (when schema provided) and also add a `"type":"text"` content item with the JSON string (compat).
- Capture/see: can add `"type":"image"` blocks (base64 png).
- C# SDK content blocks: `TextContentBlock { Text = ... }`, `ImageContentBlock { Data = <base64>, MimeType = "image/png" }`.
- DI API (preview.1): `services.AddMcpServer(_ => { }).WithStdioServerTransport().WithListToolsHandler(...).WithCallToolHandler(...)`.
- Sources: MCP spec (tools, 2025-06-18) + NuGet Gallery + GitHub (modelcontextprotocol/csharp-sdk).
