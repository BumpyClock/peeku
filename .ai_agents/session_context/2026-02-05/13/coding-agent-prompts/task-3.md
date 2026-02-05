# Task 3: Session manager + strict UIA actor thread

Repo: `C:\Users\adityasharma\Projects\peeku`

Goal: add per-connection session with a strict single-threaded execution queue so all UIA work runs on one actor thread. Use existing daemon JSON-RPC stack.

Constraints:
- No new external deps.
- All new classes `sealed`.
- Every class has XML doc comment with purpose + usage example.
- No comments inside method bodies.
- Keep files <~500 LOC; split if needed.
- Follow existing repo style.

Implementation requirements:
- Each JSON-RPC connection gets its own session/actor.
- All dispatch work (including `peeku.batch`) must execute on the actor thread.
- Use a queue (Channel or BlockingCollection) to serialize work; actor loop runs on a dedicated background thread.
- Provide a clean shutdown/dispose path when connection closes.

Suggested structure:
- New `DaemonSession` class in `src/peeku.Daemon/`:
  - Holds `IPeekuClient` instance (use `new WindowsClient()` per session).
  - `ExecuteAsync<T>(Func<IPeekuClient, CancellationToken, Task<T>> work, CancellationToken ct)` enqueues and awaits result.
  - Dedicated thread/task reads queue and runs work sequentially.
- Modify `JsonRpcConnection` and/or `JsonRpcDispatcher`:
  - `JsonRpcConnection.ProcessAsync` creates `DaemonSession` and passes it to dispatcher.
  - `JsonRpcDispatcher.DispatchAsync` uses session.ExecuteAsync for all methods, including `server.ping` and `server.capabilities` (ok to run on actor thread).

Tests (TDD required):
- Add tests in `tests/peeku.Daemon.Tests`:
  - Ensure queued work executes sequentially (e.g., enqueue two actions that append to a shared list; verify order).
  - Ensure session Dispose stops actor thread without hanging.

Do NOT edit CLI code.

After implementation:
- Write report to `.ai_agents/session_context/2026-02-05/13/coding-agent-reports/task-3-report.md` with summary + files changed + tests run.
