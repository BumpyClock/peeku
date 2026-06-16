# peeku CLI

## Install

Build a self-contained single-file binary and put it on PATH:

```powershell
dotnet publish src/peeku.Cli -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:PublishReadyToRun=true -o dist/
# Add dist\ to your PATH, or copy dist\peeku.exe to a directory already on PATH.
# dist\ also contains peeku-daemon.exe and peeku-mcp.exe.
```

- stdin/stdout: command output on stdout
- logs: stderr (Serilog)

## Run

```powershell
peeku --help
peeku --format json windows list --limit 10
peeku click "OK" --app notepad
peeku type "hello" --app notepad
peeku find "Save" --app notepad
peeku see --app notepad --depth 3
```

## Global options (recursive)

Use before/after subcommands.

- `--format json|pretty` (default `json`)
- `--timeout <ms>` — bare integer = **milliseconds** (e.g. `5000`); also `500ms` / `5s` / `2m` / `hh:mm:ss` (default 10s)
- `--log-level trace|debug|info|warn|error` (default `info`)
- `--log-file <path>` (optional)
- `--trace-id <id>` (optional; else generated)
- `--profile <name>` (reserved; no-op for now)
- `--no-daemon` — never use or spawn the daemon; run in-process (also via `PEEKU_NO_DAEMON=1`)

## Daemon (fast mode)

A background daemon keeps a warm UIA host and a shared element cache, so a `uia snapshot` in one
CLI process and a follow-up `element get --ref <refId>` / `click` in a **separate** process resolve
against the same session. It is also faster (no per-call UIA cold start).

**Auto-spawn (default).** Normal commands connect to a running daemon automatically; if none is
running, the CLI spawns one in the background, then uses it. The spawning call may briefly fall back
to in-process while the daemon comes up (`meta.warning: "Daemon starting; used in-proc for this
call"`) — the next call connects to the now-warm daemon. Auto-spawn only triggers when the real
`peeku-daemon` executable sits next to the CLI (a dev `dotnet run` layout stays in-process).

**Opt out.** `--no-daemon` (or `PEEKU_NO_DAEMON=1`) forces the pure in-process path: it neither uses
nor spawns a daemon. The MCP server never auto-spawns.

**Manual lifecycle** (the `daemon` subcommand):

```powershell
peeku daemon serve     # run server in the foreground (blocks until Ctrl-C)
peeku daemon start     # start background daemon (idempotent)
peeku daemon status    # query state (always exits 0)
peeku daemon stop      # stop the running daemon
```

- `daemon start` spawns the background daemon and writes `%LOCALAPPDATA%\peeku\daemon.json`
- `watch` requires a running daemon (auto-spawned, or `peeku daemon start`)

## Commands (implemented)

### `doctor`

```powershell
peeku doctor
peeku doctor --deep
```

- `--deep`: slower checks (focused window + capture + UIA snapshot)

### `windows list`

```powershell
peeku windows list
peeku windows list --titleContains Edge
peeku windows list --processName msedge
peeku windows list --limit 10
peeku windows list --includeMinimized
```

- `--titleContains <text>`: title substring filter
- `--processName <name>`: process name filter
- `--limit <n>`: max results (default 50)
- `--includeMinimized`: include minimized windows (default: minimized skipped; warning added)

### `windows focused`

```powershell
peeku windows focused
```

### `capture image`

```powershell
peeku capture image
peeku capture image --hwnd 0x000000000001047C
peeku capture image --out C:\temp\peeku.png
peeku capture image --includeBase64
```

- default target: focused window
- `--hwnd <hex>`: capture by HWND (hex; `0x...`)
- `--out <path>`: output PNG path (default: `%TEMP%\peeku\peeku_capture_<traceId>.png`)
- `--includeBase64`: include base64 PNG in JSON result

### `uia snapshot`

```powershell
peeku uia snapshot --depth 2 --maxNodes 500
peeku uia snapshot --hwnd 0x000000000001047C --includeProperties all
```

- target flags: `--focused` (default), `--desktop`, `--screenIndex`, `--hwnd`, or query (`--titleContains`/`--processName`/`--processId`)
- `--depth <n>` (default 6)
- `--maxNodes <n>` (default 5000)
- `--includeProperties basic|all` (default basic)
- tab strips: if `controlType: Tab` has no native UIA name, `name` may be populated as `tabs: ...` from descendant `TabItem` labels (`*` = selected)

### `see`

```powershell
peeku see --depth 2 --maxNodes 500
peeku see --includeBase64
```

- same target + depth/maxNodes/includeProperties as `uia snapshot`
- `--includeBase64`: include base64 PNG in response

### `find`

```powershell
peeku find --selector "window[name~=\"Notepad\"]/edit" --limit 5
```

- `--selector <expr>`: selector DSL (see `docs/learned/selector.md`)
- `--live`: evaluate selector on live UIA tree (no snapshot)
- `--limit <n>`: max matches (default 20)
- target flags optional; if omitted, defaults to focused window

### `element get`

```powershell
peeku element get --ref uia:123:abc --snapshotId <id>
peeku element get --selector "window"
peeku element get --selector "window[name~=\"Notepad\"]/edit" --includeProperties all
```

- `--ref <refId>` + optional `--snapshotId <id>` OR `--selector <expr>`
- `--live`: evaluate selector on live UIA tree (no snapshot)
- `--includeProperties basic|all` (default all)
- target flags optional; if omitted, defaults to focused window

### `click`

```powershell
peeku click --selector "window[name~=\"Notepad\"]/button[name=\"OK\"]"
peeku click --ref uia:123:abc --method uia
```

- `--ref <refId>` + optional `--snapshotId <id>` OR `--selector <expr>`
- `--live`: evaluate selector on live UIA tree (no snapshot)
- target flags: `--focused`, `--desktop`, `--screenIndex`, `--hwnd`, or query (`--titleContains`/`--processName`/`--processId`) (default: focused)
- `--method auto|uia|input` (default auto)

### `invoke`

```powershell
peeku invoke --selector "window[name~=\"Notepad\"]/menuitem[name=\"File\"]"
```

- `--ref <refId>` + optional `--snapshotId <id>` OR `--selector <expr>`
- `--live`: evaluate selector on live UIA tree (no snapshot)
- target flags: `--focused`, `--desktop`, `--screenIndex`, `--hwnd`, or query (`--titleContains`/`--processName`/`--processId`) (default: focused)

### `set-value`

```powershell
peeku set-value --selector "window[name~=\"Notepad\"]/edit" --value "hello"
```

- `--ref <refId>` + optional `--snapshotId <id>` OR `--selector <expr>`
- `--live`: evaluate selector on live UIA tree (no snapshot)
- target flags: `--focused`, `--desktop`, `--screenIndex`, `--hwnd`, or query (`--titleContains`/`--processName`/`--processId`) (default: focused)
- `--value <text>` (required)
- verification: if `ValuePattern` is supported, result is auto-verified against expected value
  - mismatch => `ok=false`
  - unsupported => `ok=true` + warning
- includes `evidence` payload in result (operation/status/expected/actual/verification flags)

### `type`

```powershell
peeku type --selector "window[name~=\"Notepad\"]/edit" --text "hello"
peeku type --selector "window[name~=\"Notepad\"]/edit" --text "hello" --append false --delay-ms 10
```

- `--ref <refId>` + optional `--snapshotId <id>` OR `--selector <expr>`
- `--live`: evaluate selector on live UIA tree (no snapshot)
- target flags: `--focused`, `--desktop`, `--screenIndex`, `--hwnd`, or query (`--titleContains`/`--processName`/`--processId`) (default: focused)
- `--append true|false` (default true)
- verification: if `ValuePattern` is supported, final value is auto-verified
  - mismatch => `ok=false`
  - unsupported => `ok=true` + warning
- includes `evidence` payload in result (operation/status/expected/actual/verification flags)

### `scroll`

```powershell
peeku scroll --selector "window[name~=\"Notepad\"]/edit" --delta 120
peeku scroll --selector "window[name~=\"Notepad\"]/edit" --lines -3 --direction vertical
```

- `--ref <refId>` + optional `--snapshotId <id>` OR `--selector <expr>`
- `--live`: evaluate selector on live UIA tree (no snapshot)
- target flags: `--focused`, `--desktop`, `--screenIndex`, `--hwnd`, or query (`--titleContains`/`--processName`/`--processId`) (default: focused)
- exactly one of `--delta` or `--lines` is required
- `--direction vertical|horizontal` (default vertical)

### `hotkey`

```powershell
peeku hotkey --keys "CTRL+SHIFT+S"
```

### `observe`

```powershell
peeku observe --events focus --duration 00:00:05 --max-events 50
```

- target flags: `--focused`, `--desktop`, `--screenIndex`, `--hwnd`, or query (`--titleContains`/`--processName`/`--processId`) (default: focused)
- `--events structure|property|focus|all` (default all; v1 currently supports focus)
- output: JSON array of events

### `wait`

```powershell
peeku wait --selector "window[name~=\"Notepad\"]/edit" --timeout 00:00:10
```

- target flags: `--focused`, `--desktop`, `--screenIndex`, `--hwnd`, or query (`--titleContains`/`--processName`/`--processId`) (default: focused)
- `--live`: event-driven live UIA evaluation for selector (no snapshot)
- `--timeout` is the global CLI timeout

### `watch`

```powershell
peeku watch --selector "window[name~=\"Notepad\"]/edit"
peeku watch --selector "window/button[name=\"OK\"]" --debounce-ms 100 --limit 20
```

- daemon-only command (requires `peeku --daemon`)
- live selector evaluation stream (`--live` semantics built in)
- emits JSONL `watch.update` lines when match set changes
- `--debounce-ms <n>` default `100`
- `--limit <n>` default `20`
- runs until Ctrl+C

### `batch`

```powershell
peeku batch --in ops.json --stop-on-error true
```

- `--in <path>` (required): JSON ops array
- `--stop-on-error true|false` (default true)

`ops.json` format (array):

```json
[
  { "tool": "peeku_click", "args": { "selector": { "expr": "window/edit" } } }
]
```

### `app launch`

```powershell
peeku app launch notepad.exe
peeku app launch "shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App" --waitUntilReady
peeku app launch mspaint.exe --waitUntilReady --waitMs 8000 --noFocus
```

- `--waitUntilReady`: wait for the app's main window to appear (default false)
- `--waitMs <ms>`: readiness poll timeout in milliseconds (default 5000)
- `--noFocus`: do not bring the launched window to the foreground (default false)
- Supports both classic Win32 exe paths and packaged app AUMIDs (`shell:AppsFolder\...`)

### `app quit`

```powershell
peeku app quit --pid 12345
peeku app quit --processName notepad
peeku app quit --processName notepad --all
peeku app quit --processName notepad --force
```

- `--pid <int>`: target by process ID
- `--processName <name>`: target by process name (first match; use `--all` for all matches)
- `--all`: quit all processes matching `--processName`
- `--force`: skip graceful WM_CLOSE and terminate immediately
- `--waitMs <ms>`: graceful-close poll timeout before killing (default 3000)
- `--except <pid,...>`: exclude these PIDs when using `--all`

### `app relaunch`

```powershell
peeku app relaunch --pid 12345
peeku app relaunch --processName mspaint
peeku app relaunch --processName mspaint --waitUntilReady --waitMs 8000
peeku app relaunch --processName mspaint --noFocus
```

Captures the running process's executable path, quits it gracefully, then re-launches the same exe.
Exactly one of `--pid` or `--processName` is required; if neither is provided the call returns
`ok=false` with `error.code=InvalidArgument`.

- `--pid <int>`: select target by process ID
- `--processName <name>`: select target by process name (first match)
- `--waitUntilReady`: wait for the relaunched window to appear (default false)
- `--waitMs <ms>`: timeout reused for BOTH the graceful-quit grace period AND launch readiness (default 5000)
- `--noFocus`: do not bring the relaunched window to the foreground (default false)

**Reliability note:** relaunch is reliable for classic Win32 executables (e.g. `mspaint.exe`,
`notepad.exe`). For packaged/UWP apps (e.g. Calculator), `MainModule.FileName` returns the on-disk
exe, which may not honour the package identity — re-launch via AUMID is more reliable for those.
Use `app launch <aumid>` after `app quit` for packaged apps.

Example output:

```json
{
  "ok": true,
  "traceId": "abc123",
  "timestamp": "2026-06-16T10:00:00Z",
  "durationMs": 1823,
  "processId": 9876,
  "executablePath": "C:\\Windows\\system32\\mspaint.exe",
  "window": { "hwnd": "0x00010ABC", "processId": 9876, "title": "Untitled - Paint", "processName": "mspaint" }
}
```

### `app list`

```powershell
peeku app list
peeku app list --limit 20
```

Returns one entry per distinct process that owns at least one visible top-level window.
Each entry carries a single representative window title (prefers the first window with a non-empty
title). The entry with `active: true` is the process that owns the current foreground window; at
most one entry is active.

- `--limit <n>`: max distinct apps returned (default 100); applied after grouping by process

Example output:

```json
{
  "ok": true,
  "traceId": "def456",
  "timestamp": "2026-06-16T10:00:01Z",
  "durationMs": 42,
  "apps": [
    { "processId": 1234, "processName": "msedge",   "title": "GitHub - Microsoft Edge", "active": true },
    { "processId": 5678, "processName": "mspaint",  "title": "Untitled - Paint",        "active": false },
    { "processId": 9012, "processName": "explorer", "title": "This PC",                 "active": false }
  ]
}
```

## Exit codes

| Code | Class | `error.code` values |
|------|-------|---------------------|
| `0` | Success | — (`ok=true`) |
| `1` | Generic failure | `Unknown`, `Internal` |
| `2` | Usage / validation | `InvalidArgument`; also parser-level errors (unknown flags, missing required) |
| `3` | Not found | `NotFound`, `ElementNotFound`, `WindowNotFound`, `SnapshotNotFound` |
| `4` | Timeout (wall-clock `--timeout` elapsed) | `Timeout`; also `Canceled` when the CLI deadline fires before Ctrl-C |
| `5` | Daemon unreachable | `Unavailable` |
| `6` | Permission denied | `PermissionDenied` |
| `7` | Not supported | `NotSupported` |
| `8` | Ctrl-C / SIGINT cancellation | `Canceled` (only when invocation token fires, not a wall-clock deadline) |

## Contributing / dev

For development without publishing, run directly via the SDK:

```powershell
dotnet run --project src/peeku.Cli -c Release -- --help
dotnet run --project src/peeku.Cli -c Release -- --format json windows list --limit 10
```

This pays MSBuild up-to-date check + cold JIT on every invocation. For repeated use, publish once and use the binary.
