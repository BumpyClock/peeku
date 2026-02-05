# Task 1: Daemon server + RPC base

Repo: `C:\Users\adityasharma\Projects\peeku`

Goal: add new `peeku.Daemon` project (exe) with NamedPipe JSON-RPC server (JSONL framing). Implement methods: `server.ping`, `server.capabilities`, `server.shutdown`, `peeku.batch`.

Constraints:
- C# only; no new external deps.
- All new classes `sealed`.
- Every class has XML doc comment with purpose + usage example.
- No comments inside method bodies.
- Keep files <~500 LOC; split if needed.
- Follow existing repo style (nullable annotations etc).

Protocol:
- JSON-RPC 2.0 object per line (JSONL).
- Request: `{ "jsonrpc":"2.0", "id":"<string|number>", "method":"...", "params":{...} }`
- Notification: same but no `id`.
- Response: `{ "jsonrpc":"2.0", "id":..., "result":<object> }` or `{ "jsonrpc":"2.0", "id":..., "error":{ "code":-32603, "message":"...", "data":{...} } }`
- Use error codes: -32600 invalid request, -32601 method not found, -32602 invalid params, -32603 internal error.

Server behavior:
- Listens on named pipe (default: `peeku.<username>.v1` unless `--pipeName` arg provided).
- For each connection, process lines sequentially.
- `server.ping` -> result `{ ok:true, meta: ResultMeta }` (use `Results.Start()` to create meta).
- `server.capabilities` -> `{ protocolVersion:"1", buildVersion:"<assembly version>", supportsHandles:false, supportsStreaming:false }`
- `server.shutdown` -> respond ok, then signal server stop.
- `peeku.batch` -> params are `BatchRequest`; call `IPeekuClient.BatchAsync` (use `new WindowsClient()`); return `BatchResult`.

Files to create/update:
- `src/peeku.Daemon/peeku.Daemon.csproj` (OutputType Exe; target `net10.0-windows10.0.19041.0`; ref `peeku.Core`)
- `src/peeku.Daemon/Program.cs`
- `src/peeku.Daemon/DaemonServer.cs` (or split into `DaemonServer.cs` + `JsonRpc*.cs`)
- Update `peeku.slnx` to include new project under `/src/`

Tests (TDD required):
- Add `tests/peeku.Daemon.Tests/peeku.Daemon.Tests.csproj` referencing `peeku.Daemon`
- Unit tests for JSON-RPC parsing/dispatch using in-memory reader/writer (no named pipe required)
- Example: send `server.ping` request line; assert response `ok:true` and `jsonrpc:"2.0"`

Do NOT edit CLI code in this task.

After implementation:
- Write report to `.ai_agents/session_context/2026-02-05/13/coding-agent-reports/task-1-report.md` with summary + files changed + tests run.
