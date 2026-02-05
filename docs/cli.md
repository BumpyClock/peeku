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
peeku windows list --includeMinimized
```

- `--titleContains <text>`: title substring filter
- `--processName <name>`: process name filter
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

## Commands (planned; not implemented yet)

Per `prd.md`:

- `uia snapshot`
- `see`
- `find`
- `element get`
- `click` / `invoke` / `set-value` / `type` / `scroll` / `hotkey`
- `observe`
- `wait`
- `batch`

## Exit codes

- `0`: ok=true
- `1`: ok=false

