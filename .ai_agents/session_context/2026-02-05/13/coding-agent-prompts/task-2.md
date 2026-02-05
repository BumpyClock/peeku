# Task 2: Daemon client + CLI routing

Repo: `C:\Users\adityasharma\Projects\peeku`

Goal: add daemon-aware CLI flow:
- Connect-by-default when marker exists
- Flags: `--server` (foreground), `--daemon` (spawn), `--daemon --stop` (shutdown)
- JSON-RPC client over NamedPipe (JSONL framing) matching Task 1 protocol.

Constraints:
- No new external deps.
- All new classes `sealed`.
- Every class has XML doc comment with purpose + usage example.
- No comments inside method bodies.
- Keep files <~500 LOC; split if needed.
- Follow existing code style.

Do NOT edit `src/peeku.Daemon/**` (Task 1 owns).

Implementation details:
- JSON-RPC framing: one JSON object per line (same as Task 1).
- Implement `DaemonJsonRpcClient` (or similarly named) in `src/peeku.Cli/`:
  - Connect to named pipe (name from marker file).
  - `CallAsync<T>` -> send request, read responses until matching id, deserialize result.
  - Sequential calls only (lock/sem).
- Implement `DaemonPeekuClient` in `src/peeku.Cli/` implementing `IPeekuClient`:
  - Each method calls `peeku.batch` with single op (tool name + args).
  - Deserialize `BatchResult` then `BatchStepResult.Result` into correct result type.
  - On failure or parse error, return a result with `Ok:false` and `PeekuErrors.Create(PeekuErrorCode.Internal, "...")`.

Marker file:
- Path: `%LOCALAPPDATA%\\peeku\\daemon.json`
- JSON shape: `{ pipeName, pid, startedAt, protocolVersion, buildVersion }`
- Implement `DaemonMarker.TryLoad()` + `Save()` in CLI; no null args.

CLI changes:
- `Program.cs` add global options: `--server`, `--daemon`, `--stop`.
- If `--server` true: start server in foreground (invoke daemon server class from `peeku.Daemon` assembly), then return exit code when stopped.
- If `--daemon` true:
  - If `--stop` true: connect to daemon (marker -> pipe) and call `server.shutdown`; remove marker file.
  - Else: spawn background daemon process (`peeku-daemon` if found next to CLI exe, else `dotnet run --project src/peeku.Daemon -c Release -- --pipeName <name>`); write marker file; return 0.
- If neither: use connect-by-default:
  - If marker exists and daemon reachable (ping), use `DaemonPeekuClient`.
  - If unreachable: fallback to `WindowsClient` and add `Meta.Warning` on results: "Daemon unreachable; fell back to in-proc".

Warning wrapper:
- Implement wrapper `WarningPeekuClient` that merges warning into `ResultMeta.Warning` for all result types.

Tests (TDD required):
- Unit tests for `DaemonMarker` save/load (temp dir).
- Unit test for `DaemonPeekuClient` mapping using fake JSON-RPC client (inject interface or test helper).
- If tests too heavy, call out explicitly in report.

After implementation:
- Write report to `.ai_agents/session_context/2026-02-05/13/coding-agent-reports/task-2-report.md` with summary + files changed + tests run.
