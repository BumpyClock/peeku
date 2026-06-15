# peeku CLI Refinement Plan

**Status:** Official plan for implementers. Derived from 47 verified findings.
**Scope:** `peeku.Cli`, with ripple notes for `peeku.Mcp` (1:1 mirror) and `peeku.Daemon` (CLI-only fast-path).
**Audience priority:** Agents/scripts first, humans second.

> **Locked decisions (do not relitigate):**
> 1. **Flag casing is out of scope.** Existing flag names (`--titleContains`, `--maxNodes`, `--includeBase64`, `--snapshotId`, `--screenIndex`, `--processName`, `--processId`, `--includeMinimized`, `--includeProperties`, etc.) stay **exactly as-is**. No camelCase→kebab rename exists anywhere in this plan. The mixed casing is an accepted non-goal. Only flag **behavior/shape** is on the table, and only when it is a real win.
> 2. **Agents-first.** JSON-first stable contracts, terse output, a real exit-code map, stdin piping. Human pretty-printing is secondary but not zero.
> 3. **Distribution = self-contained single-file + ReadyToRun (R2R).** Drop `dotnet run` as the documented entry point. NativeAOT deferred (WinRT/FlaUI/COM reflection risk). Keep and improve the daemon fast-path.
> 4. **Pre-1.0: break freely** when a change is substantively better. No back-compat aliases required. Bias toward additive wins.

---

## 0. Verification corrections (post-review)

Two audit claims were **refuted** during adversarial verification (DLL reflection + version pin) but partially leaked back into the prose below via unverified dimension summaries. The corrected positions, which override any contrary statement later in this document:

- **Ctrl-C is NOT broken.** `System.CommandLine` is pinned to **2.0.2** (`Directory.Packages.props:8`), whose `InvocationConfiguration.ProcessTerminationTimeout` defaults to ~2s and installs a `ProcessTerminationHandler` (SIGINT/SIGTERM) inside the `InvokeAsync` path. The framework hands the action delegates a **cancellable** token, so passing `CancellationToken.None` at `Program.cs:135` does **not** disable Ctrl-C — `watch`'s `while (!ct.IsCancellationRequested)` and `observe`'s linked CTS already cancel and flush on Ctrl-C. **Net:** "pass the real token to fix Ctrl-C" is largely a **no-op**; demote it from a P0 *fix* to a P1 *verify-and-harden*. The genuine residuals are narrow: (a) the daemon `FindAsync` RPC inside `watch` isn't guaranteed to complete inside the 2s graceful window, and (b) the `Canceled` exit code (§5 → 8) and Ctrl-C behavior are undocumented. Treat those as the real work; **run a live Ctrl-C smoke test before writing any code.**
- **`--events` scalar→array is low-value, not a parity gap.** The MCP "array of enums" is not a real multi-select: `BatchArgs.ReadObserveEventSet` collapses any array to **3** effective states (`property` is absorbed into `Structure`), while the CLI scalar already reaches **4** (`Structure`/`Property`/`Focus`/`All`). Both funnel through the shared non-`[Flags]` `ObserveEventSet` enum, and v1 only implements **focus** events (`UiaObserve.cs`). So the CLI is not *less* expressive than MCP. Keep the widening as **optional/low-priority** (§10 P1), and drop the "CLI can't request a subset / MCP models an array" framing wherever it appears (§2, §4).

---

## 1. Executive summary

- **Biggest ergonomics win:** add an optional **positional element query** (`peeku click "OK"`) plus **`--app`/`--pid` app targeting** so the common path stops requiring `--selector "window/button[name=…]"`. This is the difference between an agent one-shotting an action and constructing a selector DSL. Additive — `--selector`/`--ref` keep working.
- **Biggest speed win:** ship **R2R self-contained single-file** publish and a real `peeku.exe` on PATH, and **stop documenting `dotnet run`** (which pays MSBuild up-to-date + cold JIT on every call). Pair with **daemon auto-spawn + PID-liveness** so the warm UIA host is the default, not an obscure manual step.
- **Biggest contract win:** replace the flat **0/1 exit model** with a stable **exit-code map** keyed off the existing `PeekuErrorCode` taxonomy (usage=2, not-found=3, timeout=4, daemon-unreachable=5, …) and **flip the default output to compact JSON**. Agents branch on `$?` instead of parsing stdout.
- **Daemon lifecycle gets a real `daemon start|stop|status|serve` subcommand**, removing three recursive global booleans (`--server`/`--daemon`/`--stop`) and the `--stop requires --daemon` foot-gun, and finally exposing a machine-readable `status`.
- **Robustness gaps closed:** accept **stdin** for `batch`/`run` (`… | peeku batch`) and stop the **stale-marker 10s timeout tax** with a microsecond PID check. (Ctrl-C is already handled by SCL 2.0.2 — see §0 — so it's a verify/harden item, not a fix.)
- **Parity is hardened, not just patched:** adopt a **single source of truth** (ToolDescriptor → typed request record) so the published MCP schema, the executor, and the daemon writer stop drifting; add a **CI parity test** as the durable backstop. Several schemas are already lying today (`wait.pollMs`, `capture.imageFormat` are dead; `scroll.lines` is omitted).

---

## 2. Current state assessment (per dimension)

| Dimension | Honest state |
|---|---|
| **Flags** | Shapes mostly sound — most booleans already `Option<bool>`, ints already `Option<int?>`. Two real defects: `--append`/`--stop-on-error` are string `"true\|false"` with hand-rolled validators (and the MCP schema already declares them `boolean`), and closed enums (`--method`/`--includeProperties`/`--direction`/`--format`/`--log-level`) are validated strings with duplicated validators instead of enum binding → no completion. `--events` is a scalar where MCP models an array. |
| **Structure** | Verb-rich but expensive: zero positional args, no `--app`/`--pid`, daemon as three recursive global booleans with a foot-gun and no `status`. Selection option block hand-copied in 7+ builders (already drifted — `element get` doesn't enforce exactly-one-of). Noun-verb nesting inconsistent (`uia snapshot`/`element get` nested; `click`/`find`/`see` flat). |
| **Output** | Right on the axis that matters: data→stdout, all Serilog→stderr. But **wrong default** (`pretty` indented JSON, not compact), no `--plain`, no `-q/-v`, no TTY/NO_COLOR handling. `watch` hand-rolls a serializer that drifts (emits nulls others omit). No exit-code-to-envelope mapping. |
| **Discovery** | `--version` resolves but reports default `1.0.0` (no `<Version>`). Flat 0/1 exit codes. One CLI-local error uses a phantom code `"InvalidOperation"` not in `PeekuErrorCode`. No `completions`. Help has descriptions but no examples / next-steps. Daemon flags emit no JSON envelope. |
| **Speed** | **No distribution story at all.** csproj has zero publish tuning, no `Directory.Build.props`. Docs mandate `dotnet run`. Daemon never auto-spawns; stale marker is pinged (not PID-checked) → full `--timeout` penalty per call after a crash; ping is sync-over-async on the hot path. No version/timing observability. |
| **Robustness** | Ctrl-C **already handled** by SCL 2.0.2 termination handler (see §0 — `CancellationToken.None` at the call site is *not* a bug); residuals are only the daemon-find graceful window + undocumented `Canceled` behavior. No stdin. `--profile` parsed-but-dead, no config/precedence. Global `--timeout` silently truncates `observe --duration`. Daemon start not crash-only/idempotent. No `--no-input`. |
| **Parity** | "1:1 mirror" is actually **3–4 hand-authored copies** of the same arg vocabulary (schema strings, BatchRunner switch, daemon writer, CLI builders). Nothing cross-checks them → silent drift (verified dead/omitted params). No guard test. |

---

## 3. Target CLI surface

Noun-verb, flat for single verbs, nested only for genuine groups. New/changed nodes marked.

```
peeku
├── doctor                 [--deep]
├── windows
│   ├── list               [target/query flags] [--includeMinimized] [--limit]
│   └── focused
├── capture                (was: capture image — flatten single child)   ★ change
├── snapshot               (was: uia snapshot — flatten single child)    ★ change
├── see                    [target] [--depth] [--includeBase64] …
├── find        QUERY?     [--selector] [--live] [target]                 ★ positional
├── inspect     QUERY?     (was: element get — flatten + positional)      ★ change
├── click       QUERY?     [--app|--pid|target] [--ref|--selector] [--method] [--foreground]   ★ positional
├── invoke      QUERY?     …                                              ★ positional
├── set-value   QUERY?     [--value …] …                                  ★ positional
├── type        TEXT?      [--text …] [--foreground] …                    ★ positional
├── scroll      QUERY?     [--direction] [--delta|--lines] …              ★ positional
├── hotkey      [--keys …] [--foreground]
├── observe     [--duration] [--events …]
├── wait        QUERY?     [--selector] [--timeout]                       ★ positional
├── watch       QUERY?     [--selector] [--live]                          ★ positional
├── batch       [--in PATH | -]  (now reads stdin)                        ★ stdin
├── run         SCRIPT|-   [--output PATH] [--continue-on-error]          ★ new
├── daemon                                                                ★ new subcommand
│   ├── status   (default)   → {running, pid, pipeName, startedAt, protocolVersion, buildVersion}
│   ├── start
│   ├── stop
│   └── serve                (foreground; was --server)
├── completions  <powershell|bash|zsh|fish>                              ★ new
└── config       <init|show|path>   (only if config story lands; else omit)   ★ new (P2)
```

**Global/recursive options:** `--format json|pretty` (default **json**), `--pretty`, `--plain`, `-q/--quiet`, `-v/--verbose`, `--no-color`, `--timeout`, `--log-level`, `--log-file`, `--traceId`, `--no-daemon`, `--no-input`. **Removed:** `--server`, `--daemon`, `--stop` (replaced by `daemon` subcommand). `--profile` removed unless the config story (P2) lands.

### USAGE synopsis

```
peeku [global-options] <command> [args] [options]

# Common agent flows (positional + app targeting)
peeku --format json windows list --limit 10
peeku click "OK" --app msedge
peeku type "hello" --app notepad
peeku find "Save" --app notepad
peeku see --app notepad --depth 3

# Streaming
peeku watch "Save" --app notepad
peeku observe --duration 00:00:30

# Scripting (stdin pipe)
cat ops.json | peeku batch
peeku run script.peeku.json --output report.json
echo '[{"tool":"peeku_windows_list","args":{}}]' | peeku run -

# Daemon lifecycle
peeku daemon start
peeku daemon status --format json
peeku daemon stop

# Discovery
peeku --version
# NOTE: `| Out-File -Append $PROFILE` is the STATIC-generated-script form (option (a) in §9).
# If peeku ships dynamic `[complete]` registration instead, the setup is a different
# one-liner (a registration shim), not a piped script — see §9.
peeku completions powershell | Out-File -Append $PROFILE
```

---

## 4. Flag ergonomics (camelCase intentionally preserved)

> **Flag casing is intentionally kept AS-IS per project owner. There is NO camelCase→kebab rename in this plan.** The table below lists only **substantive flag-shape/behavior** changes that survived verification.

| Command | Flag | Change | Why | Breaking? | MCP ripple |
|---|---|---|---|---|---|
| `type`/`set-value` (batch) | `--append` | `Option<string> "true\|false"` → `Option<bool>` (default **true**); delete validator + `!Equals("false")` re-derive; `--append false` still works | Standard bool ergonomics; matches existing `Option<bool>` flags **and** MCP schema (`"append":{"type":"boolean"}`) | No (closes a divergence) | None — MCP already boolean |
| `batch`/`run` | `--stop-on-error` | Replace with `--continue-on-error` (`Option<bool>`, default **false**); `StopOnError = !continueOnError`; delete string validator + re-parse | Common "keep going" case becomes a bare flag, not `--stop-on-error false`; MCP already carries `StopOnError` as real bool | **Yes** (flag shape) | None — MCP `BatchRequest.StopOnError` already bool |
| `click`/`invoke`/`set-value`/`type`/`scroll` | `--method` | `Option<string>`+validator → `Option<ActionMethod>` (enum exists, `Contracts.cs:211`); delete `ParseMethod` | Free parse/validation/value-completion; one source for allowed set | No (same values) | None — confidence only |
| `snapshot`/`see`/`inspect` | `--includeProperties` | `Option<string>`+3 duplicated validators → `Option<UiaPropertiesMode>` (`Contracts.cs:129`); delete `ParseProps` | Dedup across 3 files; completion | No | None |
| `scroll` | `--direction` | `Option<string>`+validator → `Option<ScrollDirection>` (`Contracts.cs:243`); delete `ParseScrollDirection` | Completion + dedup | No | None |
| global | `--format` | `Option<string>`+validator → enum or `AcceptOnlyFromAmong("json","pretty")`; default flips to `json` (§5) | Completion; correct agents-first default | **Yes** (default flip) | None (MCP returns objects) |
| global | `--log-level` | `Option<string>`+validator → enum / `AcceptOnlyFromAmong`; maps to Serilog `LogEventLevel` via existing switch | Completion + dedup | No | None |
| action commands | `--foreground` *(new)* | Add `Option<bool>` (default **false** = background, no focus steal); `--method input` implies foreground | Background, non-focus-stealing input is the core reason agents drive many apps reliably; today it's implicit/unselectable | No (additive) | **Yes** — each action tool gains `foreground` bool; default-background must match across CLI/MCP/daemon |
| `observe` | `--events` | Scalar `Option<string>` → repeated/array — **low-value, optional (see §0)** | NOT a parity gap: the CLI scalar already reaches 4 states vs MCP's 3, and v1 only implements `focus`. Defer until `structure`/`property` events are actually emitted, and only after `ObserveEventSet` gains `[Flags]` semantics | No (widening) | Shared `ObserveEventSet` enum is the real constraint, not the surface |

**Enum-binding caveats for implementers:**
- The target enums already exist in `Contracts.cs` (`ActionMethod`, `UiaPropertiesMode`, `ScrollDirection`). System.CommandLine parses enums case-insensitively and member casing differs from CLI tokens only by case (`Auto`/`auto`, `Basic`/`basic`, `Vertical`/`vertical`) — default binding works without aliases. If any alias concern arises, fall back to `AcceptOnlyFromAmong(...)`, which still removes the hand-rolled `AddError` and yields completion.
- **`--no-append`/`--no-stop-on-error` auto-negation does NOT exist** in System.CommandLine 2.0.x. An alias on the same option shares the bound value — it cannot invert it. So negation requires a *separate* `Option<bool>` + reconciliation. Recommend the minimal form: a single `Option<bool>` per flag (or the `--continue-on-error` rename for stop-on-error). Treat bare `--no-x` toggles as optional follow-up only.

---

## 5. Output & exit-code contract

### stdout / stderr (sacred — do not regress)

- **stdout** carries the JSON result/error envelope only (`CliOutput.Write` → `Console.Out`).
- **stderr** carries all Serilog log lines (`standardErrorFromLevel: Verbose`). Already correct — machine output is never polluted by logs.
- **Error envelopes stay on stdout** (they are part of the parseable JSON contract, not log noise). The *exit code* carries the failure class.

### Format defaults (agents-first)

- **Default = compact JSON.** Flip `formatOpt.DefaultValueFactory` from `"pretty"` to `"json"` and update the `?? "pretty"` fallbacks (Program.cs:25,85) to `?? "json"`. Update `docs/cli.md:16` to `--format json|pretty (default json)`.
- **`--pretty`** = indented JSON for humans (rename the concept; `pretty` was never a table). Precedence: explicit `--format pretty` or `--pretty` wins, else compact JSON.
- **`--plain`** *(new)* = stable tab-separated lines for list-shaped results (`windows list`, `find`, `see` node summaries): one record/line, documented fixed field order, **no header by default**. This is the clig.dev `grep`/`cut` surface; JSON stays the structured default. `--plain` is independent/lower-risk; ship it on its own.
- **`watch` always streams compact JSONL** regardless of `--format`/`--pretty`/`--plain` (document the exception). Route its two in-loop lines through a new `CliOutput.WriteLine(object?)` that reuses the canonical `JsonOptions` (compact, CamelCase, `WhenWritingNull`) and **delete the local `streamOptions` block** (CliFlowCommands.cs:285-290) so null-property drift stops.

### Verbosity & color

- Add recursive `-q/--quiet` (→ Serilog level `Error`) and `-v/--verbose` (→ `Debug`, `-vv`→`Verbose`) mapped onto the **existing** `--log-level` pipeline (single source — no second logger). Add a validator rejecting `-q` + `-v` together. Note: `-q` raises the stderr log floor; result-embedded `Meta.Warning` stays in JSON by design.
- **TTY/NO_COLOR (forward-looking):** no color is emitted today, so nothing to strip yet. Lock the contract before color is ever added: any future color is gated on `Console.IsOutputRedirected == false` **AND** `Environment.GetEnvironmentVariable("NO_COLOR") is null` **AND** `TERM != "dumb"`, plus a `--no-color` escape hatch. Document as: "no color emitted today; NO_COLOR/--no-color honored if/when added."

### Exit-code map

Centralize one `ExitCodes.For(PeekuError?)` helper next to `CliOutput`, keyed off the **string `error.code`** (note: `res.Error.Code` is a string, not the enum). Replace every `res.Ok ? 0 : 1` with `res.Ok ? 0 : ExitCodes.For(res.Error)`. Codes are sourced from the existing `PeekuErrorCode` taxonomy (`Results.cs:82-119`).

> ⚠️ **DO NOT `(int)code` — switch on the string.** The `PeekuErrorCode` enum **values** (`InvalidArgument=1`, `Timeout=2`, `Unavailable=3`, `PermissionDenied=4`, …) deliberately differ from the assigned **exit codes** (`InvalidArgument→2`, `Timeout→4`, `Unavailable→5`, `PermissionDenied→6`, …). An implementer who shortcuts `ExitCodes.For` to a cast of the enum int will silently ship the wrong table. The helper must `switch` on the string `error.code`. **This is the single most likely implementation mistake in §5.**

| Exit | Class | `error.code` mapped | Notes |
|---|---|---|---|
| `0` | Success | — | `ok=true` |
| `1` | Generic failure | `Unknown`, `Internal` | fallback |
| `2` | Usage / validation | `InvalidArgument` | also parser-level errors (see below) |
| `3` | Not found | `NotFound`, `ElementNotFound`, `WindowNotFound`, `SnapshotNotFound` | target/element family |
| `4` | Timeout (wall-clock) | `Timeout` | deadline elapsed before completion |
| `5` | Daemon unreachable | `Unavailable` | replaces the phantom `"InvalidOperation"` |
| `6` | Permission denied | `PermissionDenied` | |
| `7` | Not supported | `NotSupported` | |
| `8` | User cancellation (Ctrl-C) | `Canceled` | **distinct from timeout** — agents want to tell "I aborted" from "it timed out" |

> **Decision (loud, not buried): split timeout from cancellation.** `Canceled` and a wall-clock `Timeout` are kept as **distinct exit codes** (8 vs 4). Pre-1.0 we break freely (locked decision #4), and a flat collapse would force agents to parse stderr to recover the distinction. Because the CLI currently surfaces wall-clock timeouts as `Canceled` (see caveat below), `ExitCodes.For` must consult the command-deadline flag to assign `4` vs `8` — do **not** key both off the bare `Canceled` string.

**Implementer caveats:**
- **Parser usage errors** (unknown flags, missing required, `Option.Validators.AddError`) are handled by `parse.InvokeAsync` (Program.cs:129), which returns `1` and never reaches a handler. To deliver `2` for these, inspect `parse.Errors` before `InvokeAsync` (or set the invocation result). The `res.Ok ? 0 : ExitCodes.For(...)` swap alone does **not** cover them.
- **Timeouts surface as `Canceled`, not `Timeout`,** at the CLI today (`CreateTimeoutCts` cancels the linked token; the daemon maps cancellation → `Canceled`). To honor the split above, `ExitCodes.For` must branch on a command-deadline signal: if the timeout CTS fired (wall-clock elapsed) → **4**; if cancellation came from the SIGINT/Ctrl-C token → **8**. Thread a small flag (e.g. `bool deadlineElapsed`) from the timeout CTS into the exit-code helper rather than keying both off the bare `Canceled` string.
- **Daemon management paths** (`Program.cs` `return 1` sites) must route through the same map: stop-without-running → `2` (usage), unreachable → `5`.
- **Eliminate the phantom code:** CLI-local failures must go through `PeekuErrors.Create(PeekuErrorCode.X, msg)` and serialize the real `PeekuError`. There are **5** hand-rolled anonymous error envelopes (CliActionCommands.cs:370 used by 6 callers; CliFlowCommands.cs:146,163,177,267). Map daemon-unreachable → `Unavailable` and **drop `"InvalidOperation"`** (not a `PeekuErrorCode` member). Provide one shared `CliErrors.Write(ctx, PeekuError)` so the envelope (ok/meta/error/traceId) is byte-identical to Core results — this is the same enum the MCP dispatcher already emits, closing a parity gap.
- **Optional:** surface the chosen `exitCode` in JSON `meta` so MCP/daemon stay informationally aligned (the `error.code` vocabulary must remain the single source shared by CLI and MCP).
- **Daemon subcommand envelopes:** `daemon start|stop|status|serve` each write a standard envelope via `CliOutput.Write` and return a mapped exit code, so daemon ops are JSON-observable like every other command (today they emit Serilog only).

---

## 6. Speed & distribution plan

### Why not NativeAOT

FlaUI/UIA3 + Vortice (COM/WinRT) interop is reflection- and COM-heavy and **trim-unsafe**. NativeAOT requires trimming and is high-risk for this dependency set. **R2R + self-contained single-file** captures most of the cold-start win without that risk. `PublishTrimmed` is an explicit **non-goal** for the same reason.

**R2R scope caveat:** R2R only AOT-compiles managed IL to native code; it does **not** pre-JIT generics-over-COM-interop call sites. The first WGC/UIA call still pays JIT for the interop thunks, and first-UIA-COM-activation (the WinRT runtime spin-up) can dominate cold start regardless of R2R. The cold-start budget below therefore **excludes** first-UIA-COM-activation — R2R was never going to fix it, and measuring it as "missed" would be a false negative.

### Shared `Directory.Build.props` (repo root, new)

All three exes (`peeku.Cli`, `peeku.Daemon`, `peeku.Mcp`) share the same FlaUI/Vortice deps and must publish identically. Centralize:

```xml
<Project>
  <!-- Provenance: shared version for all projects; SDK appends +<sha> to InformationalVersion -->
  <PropertyGroup>
    <VersionPrefix>0.1.0</VersionPrefix>
  </PropertyGroup>

  <!-- Publish tuning, gated so a plain `dotnet build` is unaffected -->
  <PropertyGroup Condition="'$(PublishProfileTuning)' == 'true' or '$(_IsPublishing)' == 'true'">
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>true</SelfContained>
    <PublishSingleFile>true</PublishSingleFile>
    <PublishReadyToRun>true</PublishReadyToRun>
    <!-- Non-optional: Vortice D3D11/DXGI native binaries + FlaUI's UIA interop must
         self-extract at runtime under PublishSingleFile. R2R does NOT cover these native
         deps — it only AOT-compiles managed IL. Failure to self-extract surfaces only at
         runtime on the capture path, never at publish. -->
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
    <InvariantGlobalization>true</InvariantGlobalization>
    <!-- Keep tiered + QuickJit + TieredPGO defaults; do NOT disable -->
    <TieredCompilation>true</TieredCompilation>
    <TieredPGO>true</TieredPGO>
    <!-- Explicit non-goal: trimming is unsafe for FlaUI/UIA3/COM/WinRT -->
    <PublishTrimmed>false</PublishTrimmed>
  </PropertyGroup>
</Project>
```

> **`RuntimeIdentifier=win-x64`** is safe because the `TargetFramework` is already Windows-pinned (`net10.0-windows10.0.19041.0`).
>
> **`InvariantGlobalization=true`** is safe for a different reason — *not* RID-pinning. It only drops ICU/culture data; it does not touch WinRT activation or COM marshalling. No code depends on culture-specific casing/collation/formatting for correctness (verified: the one culture-sensitive call in Core is `long.TryParse(..., NumberStyles.HexNumber, CultureInfo.InvariantCulture)` for hex hwnd parsing — unaffected). WGC/WinRT activation and Vortice D3D11/DXGI COM interop are culture-independent.

### Publish command + PATH install

```bash
# x64 (primary); also document win-arm64 as a second RID
dotnet publish src/peeku.Cli -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:PublishReadyToRun=true -o dist/
# Then put dist/ on PATH (or copy peeku.exe to a user bin dir).
# Ensure the same publish emits peeku-daemon.exe (and peeku-mcp.exe) into dist/
# so the daemon launcher + MCP host registration resolve the fast binaries.
```

- **Rewrite `docs/cli.md` + README** to lead with the `peeku` binary: `peeku --format json windows list --limit 10`. Add an **Install** section. Demote `dotnet run` to a Contributing/dev note only.
- **MCP host registration** (`docs/mcp.md`) must point at published `peeku-mcp.exe`, not `dotnet run --project src/peeku.Mcp`.
- **Daemon launcher:** replace the `dotnet run` fallback in `DaemonProcessLauncher.ResolveCommand` (line 64) with a clear `InvalidOperationException` ("peeku-daemon executable not found next to `<BaseDirectory>`; publish the daemon alongside the CLI") instead of silently degrading to `dotnet run`. The `peeku-daemon.exe` next-to-base resolution (lines 56-83) already exists.

### Startup-budget targets (documented contract)

| Path | Target |
|---|---|
| Warm daemon call (UIA already hot) | **< 100ms** to first byte of output |
| Cold R2R in-proc first call | **< 500ms** of framework overhead (excluding the UI op itself **and** first-UIA-COM-activation / WinRT runtime spin-up, which R2R does not address) |
| Stale-marker fallback | no per-call timeout penalty (PID check, not ping) |

### Per-call init + daemon fast-path improvements

1. **Auto-spawn (opt-in-by-default).** When `CliPeekuClient.CreateDefault()` finds no live daemon, spawn `peeku-daemon.exe` via `DaemonProcessLauncher.StartBackground`, write the marker, poll `TryPingAsync` on a bounded budget (~750ms / a few retries), use the daemon if it comes up, else fall back to in-proc with the existing warning. Gate with env `PEEKU_NO_DAEMON=1` and recursive `--no-daemon`. Factor the existing `StartDaemonAsync` spawn+marker logic into one shared helper so the manual command and `CreateDefault` don't drift. **MCP must NOT auto-spawn** (it's its own long-lived warm UIA host) — gate auto-spawn to CLI only.
2. **PID-liveness before ping.** Add `DaemonMarker.IsAlive()` (`Process.GetProcessById(Pid)`, catch `ArgumentException` = dead, verify process name == `peeku-daemon`). If dead/mismatched, delete the stale marker and skip the pipe round-trip. Call it from all three sites (`CliPeekuClient.CreateDefault`, `Program.StartDaemonAsync`, `Program.StopDaemonAsync`). This replaces a worst-case full-`--timeout` (10s) connect wait with a microsecond check. Also cap the connect probe with its own short budget (~300ms) instead of reusing the full command `--timeout`. Swallow IO races on stale-marker delete (try/catch, like `StopDaemonAsync` already does).
3. **Async client factory.** Convert `CreateDefault()` → `static Task<IPeekuClient> CreateDefaultAsync(CancellationToken ct)` and `await rpc.TryPingAsync(ct)`. Update the ~18 call sites (all already in async handlers) to `await …CreateDefaultAsync(cts.Token)`. **Fix both sites** of the sync-over-async ping: `CliPeekuClient.cs:24` and `CliFlowCommands.cs:375` (`.GetAwaiter().GetResult()`).
4. **Atomic marker write + idempotent start.** `DaemonMarker.Save` should write a temp file then `File.Move(temp, path, overwrite: true)` (same-volume NTFS move is atomic). In `StartDaemonAsync`: if PID is dead, delete stale marker and skip ping; if ping fails but PID alive, graceful shutdown then kill before respawn. (PID-reuse caveat: a recycled PID can false-positive; gate on process name/start-time or treat ping as authoritative and use PID only to skip the wait.)

### Observability (verifiability)

- **Stamp version** via `VersionPrefix` in shared props; the SDK appends `+<sha>` to `InformationalVersion`, so `peeku --version` (auto-provided) reports e.g. `0.1.0+b3d26ce`. Consolidate one build-version helper so the **CLI marker write** (`Program.cs:200`, currently hardcoded `"unknown"`) and the **daemon self-report** (`JsonRpcDispatcher.ResolveBuildVersion`, currently `GetName().Version`) both read `AssemblyInformationalVersion` and never diverge.
- **Emit `meta.timing`** fields in JSON results: `initMs`, `clientPath: "daemon"|"in-proc"`, `totalMs`. (Today `ResultMeta.DurationMs` measures only op time, started inside the operation — it can't show cold vs warm.) This is a **result-shape change** that ripples into MCP (same result shapes). Document the new `meta` fields once in the shared result contract **and add them to the §8 CI parity-test scope** (or explicitly exempt them) so `meta.timing` cannot drift CLI-vs-MCP the way the schema lies did.

---

## 7. Scripting & robustness

### stdin piping (batch + run)

- Make `--in` **optional** (drop `Required=true`) and accept `-` or absent-with-redirected-stdin. Control flow, **before** the existing `File.Exists` guard:
  ```
  path = parse.GetValue(inOpt)               // Required = false
  if (path == null || path == "-")
      if (!Console.IsInputRedirected) → error InvalidArgument "provide ops via --in <path> or pipe JSON to stdin"
      else json = Console.In.ReadToEnd()
  else if (!File.Exists(path)) → existing "Input file not found." error
  else json = File.ReadAllText(path)
  ```
  Feed `json` into the unchanged `JsonDocument.Parse` + array-validation path. For stdin, set the error-payload `path` to `<stdin>` so envelopes stay coherent. Enables `cat ops.json | peeku batch` and `peeku batch -`. CLI-only — MCP calls `BatchAsync` with an in-memory request, no ripple.

### `run` command (script primitive)

- `peeku run <scriptPath>` positional, reusing the batch pipeline (same ops-array parse loop + `client.BatchAsync`). `--output <path>` writes the structured report to a file (honoring `ctx.Format`). `--continue-on-error` (real bool, default false) → `StopOnError = !continueOnError`. `run -` reads the script from stdin. Keep `batch` as a thin alias (one shared private method registered under both names) to avoid breaking existing docs/tests.

### Ctrl-C / signal handling

- **Already works (see §0).** SCL 2.0.2 installs a SIGINT/SIGTERM `ProcessTerminationHandler` in `InvokeAsync` with a ~2s graceful window; the action delegates already receive a cancellable token, so `watch`/`observe` cancel and flush on Ctrl-C today. Passing the real token instead of `CancellationToken.None` is a tidy-up, **not** a fix, and does not change Ctrl-C behavior.
- **Real residuals to harden (P1):** (a) verify with a **live Ctrl-C smoke test** on `watch`/`observe`; (b) ensure the in-flight daemon `FindAsync` RPC inside `watch` either completes or is abandoned cleanly within the 2s window (it may currently outrun it); (c) document the `Canceled` exit code (§5 → 8) and the streaming-command Ctrl-C contract in `docs/cli.md`.

### Timeout semantics for streams

- **Decouple per-call timeout from stream lifetime.** For `observe`, do **not** feed the global `--timeout` into the stream CTS — let `--duration` own it (or `max(duration, timeout)`). Fix it at **both** layers: in-proc `UiaObserve.ObserveAsync` (builds `CreateLinkedTokenSource(ct).CancelAfter(duration)` where `ct` is the 10s timeout) and the daemon path (awaits the RPC under the same token). For `watch`, per-RPC `--timeout` is correct — document it. In the observe cancel-catch, emit a one-line **stderr warning** when cancellation truncated a stream before its intended `--duration` so silent truncation becomes observable. Document in `docs/cli.md`: for streaming commands `--timeout` is per-RPC, `--duration` owns `observe`'s stream; for one-shots `--timeout` is the whole-command bound. (`wait` is correctly unaffected — it passes `--timeout` as a semantic `WaitRequest.Timeout`.)

### Config & precedence

- **Recommended now (least-surprise):** **remove `--profile`** — a documented "reserved; no-op" flag actively misleads agents who pass it and silently get defaults.
- **Optional follow-up (P2):** real precedence **flags > env (`PEEKU_TIMEOUT`/`PEEKU_FORMAT`/`PEEKU_LOG_LEVEL`) > user config (`%APPDATA%\peeku\config.json`, Roaming — distinct from the daemon's `%LOCALAPPDATA%` marker) > built-in defaults**, with `--profile` selecting a named section. Load config once in `Program.cs` before building the `DefaultValueFactory` lambdas, then resolve builtin→config→env inside each factory (factories fire only when the flag is absent, so flags > env > config > builtin falls out naturally). Reuse the existing validators so env/config values get the same checks as flags. Add `peeku config init|show|path`.

### Non-interactive posture

- Add recursive `--no-input` (`Option<bool>`) — a documented guarantee the process never blocks on stdin for prompts (trivially satisfied today; future-proofs the agents-first contract and is the clig.dev standard name). Nothing prompts today, so this is a cheap, greppable contract lock.

---

## 8. MCP parity strategy

**Root cause:** the same arg vocabulary (`target`/`selector`/`depth`/`maxNodes`/`delayMs`/`lines`/…) is independently authored in 3–4 places — `ToolRegistry` schema string constants, the `BatchRunner` switch, the daemon `DaemonPeekuClient` writer, and the CLI builders — with **nothing cross-checking them**. Drift is silent and already real.

### Single source of truth (the enabler)

Make **`ToolDescriptor` the single source of truth** and derive the rest:
1. Bind each tool name to its Core **request record** via a typed `RequestType`/handler (a vestigial `Handler` field already exists on `ToolDescriptor` — repurpose it; today every one is stubbed to throw).
2. **Generate** `inputSchema`/`outputSchema` from those record types instead of hand-written JSON strings (prd §6 anticipated this; the MCP C# SDK can auto-generate input schema from parameters).
3. Replace `BatchRunner`'s hand-written per-tool JSON parsing with a single `JsonSerializer.Deserialize(args, descriptor.RequestType)` path plus a custom `Target`/`Selector`/`ElementRef` converter. The dispatcher already uses `JsonSerializer.Deserialize<BatchRequest>` — this is an extension of an existing pattern, not a rewrite.
4. The **Core request record's default value is the canonical default holder** (e.g. `maxNodes` default `5000` lives on the record once; the schema generator, the deserialize path, and the CLI `DefaultValueFactory` all inherit it).

### Specific lies to fix now (independent of the refactor)

| Tool | Defect | Fix |
|---|---|---|
| `peeku_wait` | Schema advertises `pollMs`/`returnSnapshot` (in) and `snapshotId`/`elements` (out) that the request/result/executor lack (poll cadence is a hardcoded `200ms` constant) | **Drop** those phantom fields so the contract matches `WaitRequest`/`WaitResult` |
| `peeku_scroll` | Real `lines` param exists everywhere except the schema; schema also wrongly marks `delta` `required` | **Add** `"lines"`; change `required:["delta"]` → `[]`; note one-of-delta-or-lines in both field descriptions |
| `peeku_capture_image` | `imageFormat` advertised but never read (single enum value `png`); path key is `outPath` in schema but executor/CLI use `out` | **Remove** `imageFormat` (and from prd.md:575); set schema property to `out`. **Drop `outPath` outright — no tolerated alias** (locked decision #4: pre-1.0, break freely, no back-compat aliases) |
| all target-taking tools | `target`/`selector`/`elementRef` collapsed to bare `{"type":"object"}`; executor secretly accepts the rich kind-discriminated shape **and** string shorthands | **Restore** inline (flat, no `$ref`) shaped schemas: `target` as `oneOf[string-enum, shaped-object]` with `kind` enum `{desktop, focused_window, screen, window_hwnd, window_query}` + `screenIndex`/`hwndHex`/`query{titleContains,processName,processId}`; `selector{expr,preferCachedSnapshot}`; `elementRef{refId,snapshotId}` |

### What ripples vs what must NOT

| CLI change | MCP ripple? | Mechanism |
|---|---|---|
| Positional `QUERY`/`TEXT` sugar | **NO** | Collapses to an existing schema field (`selector`/`text`); add zero MCP surface. Intentionally asymmetric. |
| `--app`/`--pid` targeting | **YES** | New capability. Add `app`/`pid` **alias key reads** in `BatchArgs.TryTargetFromKind` (`ReadString(q,"app") ?? ReadString(q,"processName")`, `ReadInt(q,"pid") ?? ReadInt(q,"processId")`). There is no generated Target schema property surface today — the keys live only in the runtime parser. Decide whether `app` is a pure alias for `processName` or a distinct WindowQuery field (distinct ⇒ ripples into the `WindowQuery` record + `Win32Windows` matcher — a larger change). |
| `--foreground` | **YES** | Each action tool gains a `foreground` boolean; default-background semantics identical across CLI/MCP/daemon. |
| `--append`/`--stop-on-error` shape | **NO** | MCP already declares these `boolean`; the CLI change *closes* a divergence. |
| Flatten `snapshot`/`inspect`/`capture` | **Decide explicitly** | MCP tool names are flat snake_case already (`peeku_uia_snapshot`, `peeku_element_get`). Either rename MCP to match (`peeku_snapshot`/`peeku_inspect`) in the same change, or accept a documented naming-map divergence. |
| `daemon` subcommand, exit codes, completions, stdin, config | **NO** | CLI-only (daemon has no MCP tool; exit codes don't exist in JSON-RPC; the **`error.code` taxonomy** stays the shared single source). |

### Durable backstop (CI parity test)

Add a test in `tests/peeku.Core.Tests` (BatchRunner/ToolRegistry/IPeekuClient all live in `peeku.Core`). Define `specials = { "peeku_doctor", "peeku_batch" }` (routed outside the switch). Then:
- **(a) Executor parity:** for each `ToolRegistry.All` name not in `specials`, dispatch with minimal schema-valid args through a throwing fake client and assert `Error.Code != NotSupported` (i.e. a `BatchRunner` arm exists).
- **(b) Interface parity (reflection):** assert `set(tool names) − specials`, mapped `snake→Pascal + "Async"` (noting `peeku_windows_list→WindowsListAsync`, `peeku_uia_snapshot→UiaSnapshotAsync`), equals the public `Task`/`IAsyncEnumerable`-returning methods on `IPeekuClient` minus `DoctorAsync`/`BatchAsync`.
- **(c) Result-meta parity:** assert the shared `meta` shape (including the new `meta.timing` fields `initMs`/`clientPath`/`totalMs` from §6) is identical across the CLI result envelope and the MCP result envelope — or list an explicit exemption set — so new `meta` fields cannot drift CLI-vs-MCP the way the schema lies did.
- This converts the entire drift class (parity-02/03/06) into a red build and backstops the SSOT refactor even before it lands. The existing `All_ToolNames_MatchExpectedList` hardcoded-list test catches registry-only additions at the wrong layer with a non-actionable message — these new tests cover the dangerous executor/interface gaps it misses.

---

## 9. Peekaboo patterns to adopt / skip

| Peekaboo pattern | Decision | peeku mapping |
|---|---|---|
| Positional query (`peekaboo click "Reload"`) + `--on <id>` | **Adopt** | Optional positional `QUERY` on action/find/inspect/wait/watch; `--ref` stays (optional `--on` alias). Precedence `--ref`/`--on` > `--selector` > positional. |
| `--app "Name"` / `--app PID:1234` / `--pid` | **Adopt** | `--app` (process name → title-substring fallback, ordered resolution in Core; **not** a pure flag-alias) + `--pid` alias of `--processId`. Keep all existing target flags. |
| Background-vs-foreground input model (`--foreground`, default background) | **Adopt** | `--foreground` (default background/no-focus-steal). Implies implementing the currently-stubbed synthetic-input transport; `hotkey` is inherently foreground-ish and needs separate handling. |
| `daemon start\|stop\|status\|serve` (default `status`) | **Adopt** | Replaces the three recursive global booleans + the foot-gun; adds the missing machine-readable `status`. |
| `run <script>` | **Adopt** | `peeku run <script>` reusing the batch engine, with `--output` and stdin (`run -`). |
| `completions [shell]` | **Adopt (clarify mechanism)** | `peeku completions powershell` (Windows-first). **Reality check (SCL `2.0.2`):** the `dotnet-suggest`/`[suggest]` static-script path is **removed**. SCL 2.0.x exposes `GetCompletions`/`[complete]`, which is **dynamic** — the shell calls back into the exe per-keystroke; it is **not** a static script you `Out-File -Append $PROFILE`. **Decide and document which ships:** (a) a peeku-authored **static generated script** that hardcodes the verb/flag/enum lists at generation time and is appended to `$PROFILE` (no per-keystroke callback; goes stale if the CLI surface changes), or (b) a **dynamic-registration shim** that wires `[complete]` callbacks into the shell (live, but PowerShell shim quality in `2.0.2` is weak). Verify the chosen path before shipping the example. |
| `config init\|show\|...` + precedence | **Adopt (P2)** | `%APPDATA%\peeku\config.json` + flags>env>config>defaults. Lower urgency (env vars partly cover it). |
| Distinct exit-code map | **Adopt & exceed** | peekaboo itself mostly uses 0/1 — peeku goes beyond with the §5 map. |
| `space` / `dock` / `menubar` extras | **Skip** | No Windows analog. |
| `permissions request-screen-recording`/TCC grant flow | **Skip** | macOS TCC-specific. `doctor`/`doctor --deep` is the Windows "are my capabilities working" analog. |
| `agent` (NL AI loop), AppleScript, `paste --restore-delay-ms` | **Skip** | Out of peeku's library-first, non-AI scope. |
| `sleep <ms>` / `clean` (cache prune) | **Optional / low-priority** | Cheap `run`-script primitives; track separately. |

---

## 10. Prioritized roadmap

Grouped by impact × risk. Each task tagged **[S/M/L]** effort and **[breaking]** where the surface changes. **No flag-casing task exists.**

### P0 — highest value, lowest risk (do first)

- [ ] **R2R self-contained single-file publish:** add root `Directory.Build.props` with the §6 properties; verify `dotnet publish … -r win-x64 --self-contained -p:PublishSingleFile=true -p:PublishReadyToRun=true`. **[M]**
- [ ] **Drop `dotnet run` from docs/README;** lead with the `peeku` binary + Install + PATH section; ensure publish emits `peeku-daemon.exe`/`peeku-mcp.exe`. **[S]**
- [ ] **Exit-code map:** `ExitCodes.For(PeekuError?)` (switch on the **string** `error.code`, never `(int)code`); replace all `res.Ok ? 0 : 1`; cover parser-level usage errors (→2) via `parse.Errors`; split wall-clock `Timeout`→4 from Ctrl-C `Canceled`→8 via the command-deadline flag; document the table in `docs/cli.md`. **[M] [breaking]**
- [ ] **Kill the phantom error code:** route all 5 hand-rolled CLI error envelopes through `PeekuErrors.Create`; daemon-unreachable → `Unavailable`; add shared `CliErrors.Write`. **[S] [breaking]**
- [ ] **Flip default `--format` to compact JSON;** add `--pretty` (indented). **[S] [breaking]**
- [ ] **Positional element query** on click/invoke/set-value/type/scroll/find/inspect/wait/watch (precedence `--ref` > `--selector` > positional; scope bare text to focused window; conflict-error if both positional + `--selector`). **[M]**
- [ ] **`--app`/`--pid` targeting** (`--pid` alias of `--processId`; `--app` ordered ProcessName→TitleContains resolution in Core; fold both into the mutual-exclusion check). **[M]**
- [ ] **stdin for `batch`** (optional `--in`, accept `-`/redirected stdin). **[S]**
- [ ] **Stale-marker PID liveness** (`DaemonMarker.IsAlive()`) + short connect budget at all three sites — removes the per-call 10s tax. **[S]**
- [ ] **`--append` → `Option<bool>`** (default true); delete validator + re-derive. **[S]**
- [ ] **Stamp version** (`VersionPrefix` in shared props); consolidate one build-version helper for marker + daemon self-report. **[S]**
- [ ] **CI parity test** (executor + interface parity over `ToolRegistry.All`). **[M]**
- [ ] **Fix MCP schema lies:** drop `wait.pollMs`/`returnSnapshot`/`snapshotId`/`elements`; add `scroll.lines` + unmark `delta` required; remove `capture.imageFormat`, unify on `out`. **[S] [breaking schema]**

### P1 — high value, moderate risk

- [ ] **`daemon start|stop|status|serve` subcommand;** remove the three recursive global booleans + the dispatch block; each writes a JSON envelope; update `watch` error strings and `docs/cli.md`. **[M] [breaking]**
- [ ] **Daemon auto-spawn** (opt-in-by-default; `PEEKU_NO_DAEMON`/`--no-daemon`; CLI-only, not MCP) + idle self-reap. **[M]**
- [ ] **Async client factory** `CreateDefaultAsync(ct)`; fix both sync-over-async pings; thread the invocation token. **[M]**
- [ ] **Crash-only daemon start:** atomic marker write (`File.Move`), reap dead/hung PID before respawn. **[M]**
- [ ] **Verify & harden Ctrl-C** (corrected from §0 — NOT a "fix"): live smoke-test `watch`/`observe` on SCL 2.0.2; ensure the in-flight daemon `FindAsync` inside `watch` resolves within the ~2s graceful window; document the `Canceled` exit code (8) + streaming Ctrl-C contract. Optionally pass the invocation token instead of `CancellationToken.None` as tidy-up. **[S]**
- [ ] **`--stop-on-error` → `--continue-on-error`** (real bool, default false). **[S] [breaking]**
- [ ] **Enum binding** for `--method`/`--includeProperties`/`--direction`/`--log-level` (bind to existing enums or `AcceptOnlyFromAmong`); delete the duplicated validators + `Parse*` helpers. **[M]**
- [ ] **`--plain` stable-line format** for list-shaped results. **[M]**
- [ ] **`watch` serializer** through shared `CliOutput.WriteLine`; delete local `streamOptions`. **[S]**
- [ ] **`-q/--quiet` / `-v/--verbose`** mapped onto the existing log pipeline. **[S]**
- [ ] **Fix stream timeout semantics:** `observe` driven by `--duration`, not global `--timeout`; warn on truncation; document. **[M]**
- [ ] **`run <script>`** command (reuse batch engine; `--output`; `run -`; `batch` becomes alias). **[M]**
- [ ] **`completions powershell`** (+ bash/zsh/fish) on System.CommandLine `2.0.2`; **decide static-generated-script vs dynamic `[complete]` registration** (§9) and ship the matching setup one-liner. **[M]**
- [ ] **`--foreground`** (default background) on action commands; implement synthetic-input transport. **[L]**
- [ ] **`--no-input`** recursive flag. **[S]**
- [ ] **Remove dead `--profile`** (until config lands). **[S] [breaking]**
- [ ] **Restore shaped `target`/`selector`/`elementRef` schemas** (inline `oneOf`, no `$ref`). **[M]**
- [ ] **`--events` scalar → array** on `observe` — **low priority, optional (see §0);** defer until non-focus events exist + `ObserveEventSet` is `[Flags]`. **[S]**

### P2 — structural / longer horizon

- [ ] **Single source of truth (ToolDescriptor → typed request record)**: generate schemas, deserialize executor args, canonical defaults on records. The enabler that makes all parity drift a compile error. **[L]**
- [ ] **Shared selection helper** (`CliSelection`): promote `AddSelectionOptions` + `TryParseSelection`; fix `element get`/`inspect` to enforce exactly-one-of; narrower `AddSelectorOnlyOptions` for find/wait/watch. **[S]**
- [ ] **Flatten single-child groups:** `uia snapshot`→`snapshot`, `element get`→`inspect`, `capture image`→`capture`; keep `windows` nested; decide MCP name alignment. **[M] [breaking]**
- [ ] **Demote query-trio in action help/docs** (lead with `--app`/`--pid` + positional). **[S]**
- [ ] **`meta.timing`** (`initMs`/`clientPath`/`totalMs`) + documented startup budgets. **[M]**
- [ ] **Help examples + next-steps** on root and high-traffic verbs (via custom `HelpAction`). **[S]**
- [ ] **Config story** (`%APPDATA%\peeku\config.json`, `PEEKU_*` env, precedence, `config init|show|path`, `--profile` → named section). **[L]**
- [ ] **NO_COLOR/TTY contract** gate (forward-looking, before any color is added). **[S]**
- [ ] **`sleep`/`clean`** script primitives (optional, low priority). **[S]**
