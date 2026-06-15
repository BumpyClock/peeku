# P1b Scoping Document — Synthetic Input, Window Management & App Lifecycle

**Status:** FINAL (adversarial review incorporated, verdict GO). Handoff for slice-by-slice
implementation with review gates. Sibling of [`peekaboo-parity-plan.md`](./peekaboo-parity-plan.md)
(§3 matrix / §5 Wave P1b) — this is the detailed, grounded slice plan.

> Source: 6-agent scoping workflow (map current input/foreground/element-point layers + locked plan →
> design sequenced slices → adversarial buildability/coordinate/focus-steal review → revise). Every
> load-bearing claim verified against live `file:line`.

## 1. TL;DR

**What P1b is:** the core-interaction breadth peekaboo has but peeku stubs — synthetic mouse input,
window management, app lifecycle. It replaces the two `"Input click not supported yet"` stubs
(`UiaClient.Actions.cs:44-51`, `DaemonPeekuClient.Actions.cs:44-51`) with a real `SendInput` mouse
transport, then layers coordinate/button/multi-click flags, a `window` group, and an `app` group.

**The honest Windows input model:** Windows has **no `CGEventPostToPid` analog** — `SendInput` is
global and always lands on the foreground window; it cannot target a process. So P1b **reframes, does
not port**: UIA-pattern actions (Invoke/Toggle/Value/Scroll) stay the **background default** (drive
controls over COM, never steal focus); `--foreground` (new `Option<bool>`, **default false**) opts
into `BringToForegroundReliable` + `SendInput`, which **steals focus** — mitigated by mandatory
capture-and-restore of the prior foreground window. `PostMessage(WM_CLOSE)` is best-effort, close-only,
never a default input path.

**Slice sequence (foundational-first, each independently shippable + review-gated):**

| Slice | Name | Effort | New verb? | Depends on |
|---|---|---|---|---|
| **S0** | DPI awareness + virtual-screen metrics + `WindowFocusScope` | S (1.5–2d) | No | none |
| **S1** | Synthetic-mouse transport + `--foreground` + stub replacement | L (4–5d) | No (flag only) | S0 |
| **S2** | Click flags: `--coords`/`--globalCoords`/`--double`/`--right` | M (2–3d) | No (flags only) | S1 |
| **S3** | `window move\|resize\|set-bounds\|minimize\|maximize\|restore\|close` | M (2–3d) | **Yes** | S0 |
| **S4** | `app launch \| app quit` | M (2–3d) | **Yes** | S3 |

S0 isolates the single cross-cutting risk (DPI flip shifting UIA/capture goldens) behind a review gate
**before any mouse byte is sent**. S3/S4 are the only slices touching `CliCommandTreeParityTests` + the
four drift tests.

## 2. Input model + focus-steal contract (the locked spine — do not relitigate)

### 2.1 The three tiers
1. **UIA-pattern actions = background path (DEFAULT).** No event injection, never steal focus. Stays default.
2. **`--foreground` = synthetic path (OPT-IN, default false).** `BringToForegroundReliable` + `SendInput`. `--method input` **implies** `--foreground=true`.
3. **`PostMessage(WM_*)` = best-effort fallback ONLY.** Used only for `window close`, never an input path. A literal `CGEventPostToPid` port is an explicit non-goal.

### 2.2 Focus-steal contract (mandatory, ships WITH the foreground path)
- **CAPTURE** prior foreground via `GetForegroundWindow()` (`Win32Windows.cs:339`) **before** the op.
- **RESTORE** via `BringToForegroundReliable(prior)` **after** the op.
- Implemented as `WindowFocusScope` (S0): `IDisposable` — ctor captures, `Dispose` restores — so every synthetic-input call wraps activate+send in a `using` for exception-safe restoration.
- Activation MUST go through `BringToForegroundReliable` (`Win32Windows.cs:142`, the full unlock dance). **Never** a bare `SetForegroundWindow`.
- `KeyPress.PressAsync` currently activates but does not restore; S0/S1 migrate it to `WindowFocusScope` so keyboard + mouse share one contract.

### 2.3 Daemon implications
The daemon accepts one connection at a time (serial loop) and marshals all UI work to a single
process-lifetime **MTA** actor thread (`Program.cs:42-55`). That invariant is load-bearing: capture →
op → restore all complete before the next connection is accepted; no input-queue race. The serial loop
only *serializes* the hijack — capture+restore is what returns the human's focus. **Do not** add a
second actor thread or switch to STA (breaks cached COM-ref safety). Capture/restore is best-effort:
guard `prior==Zero` / dead-hwnd. **Known limitation:** between capture and restore a human may move on;
restore then yanks focus to a now-unwanted window — inherent, documented.

## 3. The ordered slices

### S0 — DPI awareness + virtual-screen metrics + scoped focus-restore (foundation, no new verb)
**Goal:** make every coordinate agree in **physical pixels** and provide the capture/restore primitive,
**before any mouse byte is sent**. peeku is DPI-**unaware** today (no manifest, no csproj prop, no
`SetProcessDpiAwarenessContext`), so UIA `BoundingRectangle`, `GetWindowRect`, and `SendInput`-ABSOLUTE
silently disagree on scaled/multi-monitor displays. **Effort:** S, 1.5–2d. **Depends on:** none.

**Files:** `src/peeku.Cli/Program.cs` (DPI call at `Main` top, before `new RootCommand`);
`src/peeku.Daemon/Program.cs` (DPI call at `Main` top, **before `new DaemonSession()` line 55**);
`src/peeku.Core/Windows/Win32Screen.cs` **(NEW)**; `src/peeku.Core/Windows/WindowFocusScope.cs` **(NEW)**.

**Approach:**
- `SetProcessDpiAwarenessContext(PER_MONITOR_AWARE_V2 = (IntPtr)-4)` once at each `Main` top, **before any FlaUI/UIA3Automation/capture/window touch** (awareness is process-wide, immutable, and *changes the rects UIA returns*). Fallback chain: PerMonitorV2 → `SetProcessDpiAwareness(PerMonitor)` → `SetProcessDPIAware()`; log which tier took effect.
- `Win32Screen`: `GetSystemMetrics` SM_XVIRTUALSCREEN=76/Y=77/CX=78/CY=79 (origin **can be negative**). `ToAbsolute(sx,sy)`: `ax = round((sx - vsX) * 65535.0 / (vsCx - 1))`, `ay = round((sy - vsY) * 65535.0 / (vsCy - 1))`. The `(vsCx-1)` divisor + `vsX/vsY` origin subtraction are the two traps; callers OR `MOUSEEVENTF_VIRTUALDESK`.
- `WindowFocusScope`: ctor `prior = GetForegroundWindow()`; `Dispose` → `BringToForegroundReliable(prior)` only if `prior != Zero` and still a window.

**Wiring:** Core + two `Main` edits. **No new verb → no CliCommandTreeParityTests/ToolRegistry change.**
**Hard ordering acceptance:** awareness call MUST precede ANY FlaUI/UIA construction on ANY path (incl. the daemon actor's first work item + static initializers). **Goldens gate:** re-run capture + UIA snapshot goldens after the flip — unchanged at 100% primary, *shifted-but-correct* at 150% — **before S1 builds.**

**Tests:** `ToAbsolute` round-trips (origin→0,0; far corner→65535,65535; negative-origin case); `WindowFocusScope` restore-on-dispose / not-when-Zero; manual goldens smoke.
**Risks:** DPI flip changes UIA rects globally (reconcile up-front — why S0 is standalone+first); PerMonitorV2 needs Win 1803+ (fallback chain); set awareness before first COM init.

### S1 — Synthetic-mouse transport + `--foreground` + focus-steal capture/restore (the shared primitive)
**Goal:** build the MOUSE half of `HotkeyInputInjector` (move/down/up/wheel) reusing the
**already-declared-but-unused** `MOUSEINPUT` + `SendInput`; add `--foreground Option<bool>` (default
false) across all four surfaces; replace **both** stubs with a real synthetic click at element-rect-center.
**Effort:** L, 4–5d. **Depends on:** S0.

> **Two review checkpoints in one slice:** **S1a** — mouse INPUT builders + `SyntheticPointer` + `WindowFocusScope` wiring + both stub replacements + Contracts + first-light click (gate: manual click on Notepad works, parity green). **S1b** — count-evidence / `injected==0` hard-error + elevation hint (gate: evidence on `ActionResult.Evidence`, hard-error fires on zero send).

**Files:** `HotkeyInputInjector.cs` (`INPUT_MOUSE=0`, `MOUSEEVENTF_*` consts, mouse INPUT builders, **a count-returning `SendInputsCounted` sibling**); `SyntheticPointer.cs` **(NEW)**; `Contracts.cs` (`bool Foreground=false` on `ClickRequest`); `UiaClient.Actions.cs` + `DaemonPeekuClient.Actions.cs` (flip `inputSupported:true`, replace stub — **byte-for-byte lockstep**); `CliActionCommands.cs` (`--foreground`, `--method input → Foreground=true`); `ToolRegistry.cs`/`BatchRunner.cs`/MCP (click `foreground` field).

**Approach:** `INPUT_MOUSE=0` beside `INPUT_KEYBOARD=1`; `MOUSEEVENTF_MOVE=0x0001/ABSOLUTE=0x8000/VIRTUALDESK=0x4000/LEFTDOWN=0x0002/LEFTUP=0x0004/RIGHTDOWN=0x0008/RIGHTUP=0x0010/MIDDLEDOWN=0x0020/MIDDLEUP=0x0040/WHEEL=0x0800`. Build `MOUSEINPUT` like `KeyDown`/`KeyUp` build `KEYBDINPUT`; ABSOLUTE coords from S0 `ToAbsolute` + `VIRTUALDESK` (required multi-monitor). Batch move+down+up as **one** `INPUT[]`. `SyntheticPointer.ClickElementAsync`: rect-center from `element.BoundingRectangle` (guard zero/offscreen → `InvalidArgument`); `WindowFocusScope` capture → `BringToForegroundReliable(target)` → 50ms settle (copy `KeyPress.cs:96`) → send → restore.

**Resolutions:**
- **Count-vs-throw (must-fix):** existing `SendInputs` (`:129-144`) is private/void/**throws** on `sent!=length` — can't surface a count, and a zero send throws before any check. → add `internal static uint SendInputsCounted(INPUT[])` returning the raw count **without throwing**; `injected==0` (or partial) → **hard-error `Internal`**; `==expected` → write `{injected,expected}` to `Evidence`. Leave the throwing keyboard path untouched.
- **UIPI/elevation hint:** on `injected==0`, surface a **distinct** message: `"SendInput delivered 0 events; target may be elevated (higher integrity level). Run peeku elevated to drive elevated windows."` (still `Internal`, specific text).
- **`--method input --foreground=false` precedence:** inspect `parseResult.GetResult(foregroundOpt)` for **token presence** — explicit `false` + `--method input` → `InvalidArgument`; omitted → `--method input` sets true. Do not naively read the bool.

> **CliCommandTreeParityTests (S1):** UNCHANGED (flag, no path) — re-verify green. `ToolParityTests` green (schema field only).

**Tests:** flag-bitmask per button/direction; click `INPUT[]` move+down+up in order; zero-rect→`InvalidArgument` before send; `injected==0`→Internal+hint; Evidence carries counts; `--method input` no longer hits stub; precedence error; all parity tests green; manual `--foreground` click on Notepad restores prior foreground.
**Risks:** focus-steal flash (inherent); `injected==0` on elevated targets (hint surfaces it); two parallel click bodies drift if edited unevenly (reviewer diffs both); **gate S1 review on S0 goldens green.**

### S2 — Click flags `--coords`/`--globalCoords`/`--double`/`--right`
**Goal:** explicit coordinate targeting (window-relative default, screen-absolute opt-in) + right/double.
**Effort:** M, 2–3d. **Depends on:** S1.
**Approach:** `--x/--y` window-relative → `GetWindowRect` offset → S0 `ToAbsolute`; `--globalCoords` = already screen-absolute physical (same space as A4 `FromPoint`, `UiaClient.HitTest.cs:31`). **Coords > element**; when coords given, **SKIP `FindByRefId`** (canvas escape hatch). `--right` swaps button flags; `--double` = two down/up pairs in **one** `INPUT[]` (time=0, within `GetDoubleClickTime()`, no sleep). Validate exactly-one-of {coords, element}.
**Resolutions:** minimized target + `--coords` → `IsIconic` guard → `InvalidArgument` (`GetWindowRect` returns bogus `-32000`); window-relative coords need physical `GetWindowRect` (gate on S0).
> **CliCommandTreeParityTests (S2):** UNCHANGED — flags only. **camelCase `--globalCoords`** (not kebab).
**Deferred:** coordinate-click **verification** ships fire-and-forget in P1b (the `FromPoint` before/after verify loop belongs to P2 drag, advisory-only).

### S3 — Window-management group (`window move|resize|set-bounds|minimize|maximize|restore|close`)
**Goal:** direct window manipulation via `SetWindowPos`/`ShowWindow`/`WM_CLOSE`. **No SendInput, no focus steal, no `--foreground`.** Fold the `Win32Arrange` snap-preset seam here. **Effort:** M, 2–3d. **Depends on:** S0.
**Approach:** move=`SetWindowPos(...,SWP_NOSIZE|NOZORDER|NOACTIVATE)`; resize=`SWP_NOMOVE|...`; set-bounds=one call; minimize/maximize/restore=`ShowWindow(SW_MINIMIZE=6/MAXIMIZE=3/RESTORE=9)`; close=`PostMessage(WM_CLOSE=0x0010)` (graceful, never force). hwnd via existing `ResolveTargetWindow`. `IsZoomed`→restore-then-move.
**Resolutions:** **`window close` gains `--waitMs` (default ~2000) and REPORTS `closed:bool`** (poll `IsWindow` until gone/elapsed → `Evidence`) so a modal hang is detectable in-band; **snap presets PRIMARY-only in P1b** (`SPI_GETWORKAREA` is primary; per-monitor `rcWork` via `GetMonitorInfo` deferred — name in code comment).
> **CliCommandTreeParityTests (S3) — REQUIRED same commit:** add `"window"`, `"window move"`, `"window resize"`, `"window set-bounds"`, `"window minimize"`, `"window maximize"`, `"window restore"`, `"window close"` to `ExpectedCommandPaths`. Also `peeku_window_*` in ToolRegistry + BatchRunner + IPeekuClient (`ToolParityTests`; add `nameExceptions` e.g. `peeku_window_set_bounds → WindowSetBoundsAsync`) + MCP dispatcher.

### S4 — App lifecycle (`app launch | app quit`)
**Goal:** `launch` (`Process.Start`+`WaitForInputIdle`/poll) and `quit` (graceful `WM_CLOSE` loop → force `Process.Kill`). **Effort:** M, 2–3d. **Depends on:** S3.
**Approach:** launch=`Process.Start(UseShellExecute=true)`; UWP=`shell:AppsFolder\<AUMID>`; `--waitUntilReady`=`WaitForInputIdle`+poll `ListWindows(pid)`; `--noFocus` skips activation. quit=`PostMessage(WM_CLOSE)` to each top-level window of pid → poll exit up to timeout → on timeout/`--force`→`Process.Kill(entireProcessTree:true)`; `--all --except` filtered loop.
**Resolutions:** `WaitForInputIdle` throws on non-GUI children → try/catch → window-poll fallback; bad AUMID → `InvalidArgument` (not Internal); kill scoped to resolved pid only.
> **CliCommandTreeParityTests (S4) — REQUIRED same commit:** add `"app"`, `"app launch"`, `"app quit"`. Plus `peeku_app_launch`/`peeku_app_quit` ToolRegistry+BatchRunner+IPeekuClient+MCP. **camelCase `--waitUntilReady`/`--noFocus`/`--force`/`--all`/`--except`.**

## 4. Cross-cutting concerns
- **DPI awareness (S0, hardest prerequisite).** Set PerMonitorV2 once at each `Main` top before any FlaUI/UIA/capture touch. Goldens re-run gate in S0 before S1.
- **UIPI / elevated apps.** Non-elevated peeku cannot `SendInput`/`SetForegroundWindow`/`WM_CLOSE` a higher-integrity window. S1 surfaces the distinct elevation-hint on `injected==0`. `doctor input.*` IL self-report is out of P1b scope.
- **Multi-monitor coords.** `MOUSEEVENTF_ABSOLUTE` is primary-only unless ORed with `VIRTUALDESK` + normalized vs virtual-screen. Snap presets primary-only in P1b.
- **`WM_CLOSE` modal hangs.** S3 `close` reports `closed:bool` via `--waitMs`; S4 `app quit` pairs graceful→timeout→`--force`.

## 5. Explicitly deferred
`menu` group; drag / drag-with-verify (P2); click/coordinate verification (P2 drag); `PostMessage`-as-default-input (permanent non-goal); literal `CGEventPostToPid` port (impossible); `doctor input.*` health/F24 probe; per-monitor snap `rcWork`.

## 6. Confidence & open questions
**Confidence: HIGH** — every structural claim verified against live code (unused `MOUSEINPUT`/`InputUnion.mi`; private throw-not-count `SendInputs`; byte-identical stubs; `BringToForegroundReliable` reads-but-never-restores prior; zero DPI awareness; daemon `new DaemonSession()` at line 55; exact-match `ExpectedCommandPaths`). Slicing foundational-first + independently review-gateable.

Open questions (carry into impl, don't block S0/S1): min-OS floor for PerMonitorV2 (fallback chain ships regardless); elevation policy (recommend document+hint, not request-elevation); `window close --waitMs` default; double-click inter-pair timing on real targets; restore-foreground staleness (named limitation).
