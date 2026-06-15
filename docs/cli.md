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
- `--timeout 00:00:10` (default 10s; applied per command)
- `--log-level trace|debug|info|warn|error` (default `info`)
- `--log-file <path>` (optional)
- `--trace-id <id>` (optional; else generated)
- `--profile <name>` (reserved; no-op for now)

## Daemon (fast mode)

```powershell
peeku daemon start
peeku daemon stop
peeku daemon status
peeku daemon serve
peeku daemon          # same as status
```

- `daemon start` spawns background daemon and writes `%LOCALAPPDATA%\peeku\daemon.json` (idempotent; does nothing if already running)
- When the marker exists, normal CLI commands connect to daemon by default
- `watch` requires a running daemon (`peeku daemon start`)
- Lifecycle:
  - foreground server: `peeku daemon serve` (blocks until Ctrl-C)
  - background daemon: `peeku daemon start`
  - manual stop: `peeku daemon stop`
- `daemon status` emits a machine-readable JSON envelope (always exits 0):
  ```json
  { "running": true, "pid": 12345, "pipeName": "peeku.user.v1",
    "startedAt": "2026-06-15T10:00:00Z",
    "protocolVersion": "1", "buildVersion": "0.1.0+abc" }
  ```
  `running: false` when no marker exists or ping fails; remaining fields omitted when not running.

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
- element fields:
  - `actions`: array of capability tokens (e.g., `invoke`, `toggle`, `value`, `expand`, `pick`, `scroll`, `read`, `grid`, `range`, plus always-present `focus`, `click`, `hover`); populated only when `--includeProperties all`
  - `state`: object of current pattern state (keys like `toggleState`, `expandState`, `isSelected`, `rangeValue`, `value`); omitted if no state is present; populated only when `--includeProperties all`
  - basic snapshots omit both fields (null) for lean output
- tab strips: if `controlType: Tab` has no native UIA name, `name` may be populated as `tabs: ...` from descendant `TabItem` labels (`*` = selected)

### `see`

```powershell
peeku see --depth 2 --maxNodes 500
peeku see --includeBase64
peeku see --includeProperties all
```

- same target + depth/maxNodes/includeProperties as `uia snapshot`
- `--includeBase64`: include base64 PNG in response
- element fields: same `actions` + `state` as `uia snapshot` when `--includeProperties all`

### `find`

```powershell
peeku find --selector "window[name~=\"Notepad\"]/edit" --limit 5
```

- `--selector <expr>`: selector DSL (see `docs/learned/selector.md`)
- `--live`: evaluate selector on live UIA tree (no snapshot)
- `--limit <n>`: max matches (default 20)
- target flags optional; if omitted, defaults to focused window

### `element get` (inspect)

```powershell
peeku element get --ref uia:123:abc --snapshotId <id>
peeku element get --selector "window"
peeku element get --selector "window[name~=\"Notepad\"]/edit" --includeProperties all
```

- `--ref <refId>` + optional `--snapshotId <id>` OR `--selector <expr>`
- `--live`: evaluate selector on live UIA tree (no snapshot)
- `--includeProperties basic|all` (default all)
- target flags optional; if omitted, defaults to focused window
- element fields in result: same `actions` + `state` as `uia snapshot` when `--includeProperties all`
- error hints: when selector/element not found, `error.details` includes `candidates` array (up to 3 ranked "did you mean" elements) with shape `{ controlType, name, automationId, rect }`

### `click`

```powershell
peeku click --selector "window[name~=\"Notepad\"]/button[name=\"OK\"]"
peeku click --ref uia:123:abc --method uia
```

- `--ref <refId>` + optional `--snapshotId <id>` OR `--selector <expr>`
- `--live`: evaluate selector on live UIA tree (no snapshot)
- target flags: `--focused`, `--desktop`, `--screenIndex`, `--hwnd`, or query (`--titleContains`/`--processName`/`--processId`) (default: focused)
- `--method auto|uia|input` (default auto)
- error hints: when selector/element not found, `error.details` includes `candidates` array (see Error envelope)

### `invoke`

```powershell
peeku invoke --selector "window[name~=\"Notepad\"]/menuitem[name=\"File\"]"
```

- `--ref <refId>` + optional `--snapshotId <id>` OR `--selector <expr>`
- `--live`: evaluate selector on live UIA tree (no snapshot)
- target flags: `--focused`, `--desktop`, `--screenIndex`, `--hwnd`, or query (`--titleContains`/`--processName`/`--processId`) (default: focused)
- error hints: when selector/element not found, `error.details` includes `candidates` array (see Error envelope)

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
- error hints: when selector/element not found, `error.details` includes `candidates` array (see Error envelope)

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
- error hints: when selector/element not found, `error.details` includes `candidates` array (see Error envelope)

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
- error hints: when selector/element not found, `error.details` includes `candidates` array (see Error envelope)

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

- daemon-only command (requires `peeku daemon start`)
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

## Error envelope

All results include a `meta` object (traceId, timestamp, durationMs, optional warning) and optional `error` object:

```json
{
  "ok": false,
  "meta": { "traceId": "...", "timestamp": "...", "durationMs": 50 },
  "error": {
    "code": "ElementNotFound",
    "message": "...",
    "details": {
      "candidates": [
        { "controlType": "Edit", "name": "Search", "automationId": "SearchBox", "rect": { "x": 10, "y": 20, "width": 100, "height": 25 } }
      ]
    }
  }
}
```

- `error.details.candidates` (optional): ranked "did you mean" elements (up to 3) when `click`, `invoke`, `set-value`, `type`, `scroll`, or `element get` can't resolve the selector/element. Each candidate has `controlType`, `name`, `automationId`, `rect`.
  - `find`'s zero-match behavior is unchanged: `ok=true` with empty `matches` array (no error envelope, no hints)

## Exit codes

| Code | Class | `error.code` values |
|------|-------|---------------------|
| `0` | Success | — (`ok=true`) |
| `1` | Generic failure | `Unknown`, `Internal` |
| `2` | Usage / validation | `InvalidArgument`; also parser-level errors (unknown flags, missing required) |
| `3` | Not found | `NotFound`, `ElementNotFound`, `WindowNotFound`, `SnapshotNotFound` |
| `4` | Timeout (wall-clock) | `Timeout` |
| `5` | Daemon unreachable | `Unavailable` |
| `6` | Permission denied | `PermissionDenied` |
| `7` | Not supported | `NotSupported` |
| `8` | User cancellation (Ctrl-C) | `Canceled` |

> **Known P1 limitation:** wall-clock timeouts currently surface as exit code `8` (`Canceled`) rather than `4` (`Timeout`). The deadline-flag refinement that splits the two is pending.

## Contributing / dev

For development without publishing, run directly via the SDK:

```powershell
dotnet run --project src/peeku.Cli -c Release -- --help
dotnet run --project src/peeku.Cli -c Release -- --format json windows list --limit 10
```

This pays MSBuild up-to-date check + cold JIT on every invocation. For repeated use, publish once and use the binary.
