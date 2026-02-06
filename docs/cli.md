# peeku CLI

## Run

```powershell
dotnet run --project src/peeku.Cli -c Release
```

- stdin/stdout: command output on stdout
- logs: stderr (Serilog)

## Global options (recursive)

Use before/after subcommands.

- `--format pretty|json` (default `pretty`)
- `--timeout 00:00:10` (default 10s; applied per command)
- `--log-level trace|debug|info|warn|error` (default `info`)
- `--log-file <path>` (optional)
- `--trace-id <id>` (optional; else generated)
- `--profile <name>` (reserved; no-op for now)
- `--server` (run daemon server in foreground)
- `--daemon` (spawn/stop daemon; see below)
- `--stop` (stop daemon; requires `--daemon`)

## Daemon (fast mode)

```powershell
peeku --server
peeku --daemon
peeku --daemon --stop
```

- `--daemon` spawns background daemon and writes `%LOCALAPPDATA%\peeku\daemon.json`
- When the marker exists, normal CLI commands connect to daemon by default
- `watch` requires a running daemon (`peeku --daemon`)
- Lifecycle:
  - foreground server: `peeku --server`
  - background daemon: `peeku --daemon`
  - manual stop: `peeku --daemon --stop`

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

## Exit codes

- `0`: ok=true
- `1`: ok=false
