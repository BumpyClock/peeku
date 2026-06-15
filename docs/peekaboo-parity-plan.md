# Path to peekaboo parity

**Status:** Official plan for implementers. Authored post-P0.
**Scope:** `peeku.Core` (library-first), with adapter ripple to `peeku.Cli`, `peeku.Mcp`, `peeku.Daemon`.
**Audience priority:** Agents/scripts first, humans second.
**Sibling docs:** [`cli-refinement-plan.md`](./cli-refinement-plan.md) (§9 patterns, §10 P0/P1/P2 roadmap), [`learned/selector.md`](./learned/selector.md), [`learned/elementref.md`](./learned/elementref.md).

> **Locked decisions (do not relitigate):**
> 1. **Flag casing stays camelCase** (`--maxNodes`, `--includeProperties`, `--titleContains`, …). Owner decision. No rename appears anywhere in this plan.
> 2. **Agents-first.** Stable JSON contracts, real exit-code map, stdin piping, terse output. Humans second.
> 3. **Pre-1.0: break freely** when substantively better. No back-compat aliases required. Bias additive.
> 4. **peeku is the tool layer, not the agent.** The NL loop lives in the consumer (this harness via MCP). No AI in peeku.

---

## 1. Executive summary

peeku already has the hard, distinctive parts of a Windows automation CLI that peekaboo took years to build on macOS: a real UIA-pattern action layer (Invoke/Toggle/SelectionItem/Value/Scroll) that is **inherently background** — it drives controls over COM without stealing focus — plus a working `hotkey` via `SendInput`, a selector DSL, a named-pipe daemon, MCP, and (post-P0) an exit-code map that **exceeds** peekaboo's flat 0/1. On the contract axis (JSON-first, exit codes, stdin, targeting) peeku is at or ahead of parity.

The gaps are concentrated in three places: (1) a **correctness bug** that breaks the core agent loop, (2) **agent ergonomics** for addressing deep elements, and (3) **synthetic input + window/app management** breadth that peekaboo has and peeku stubs.

**The 4 highest-leverage moves, in order:**

| # | Move | Why it leads | Effort |
|---|---|---|---|
| 1 | **refId daemon-session fix** (singleton session + durable recomputable refs) | Found by live testing: a `snapshot → click` across two CLI calls returns `ElementNotFound` because each CLI process gets a fresh per-connection daemon session with an empty handle cache. **Daemon path only** (the warm default when the daemon is running); the in-proc path already works. Silently breaks the **fundamental observe→act loop** for the default-running daemon. Also a large latency win. | M |
| 2 | **Selector descendant axis (`//`)** | Found by live testing: agents must spell the full ancestor path to reach a deep control because the DSL has no descendant combinator. `//button[name~="Save"]` in one shot is the #1 ergonomics gap. One parser change, both snapshot + live callers inherit it. | M |
| 3 | **`daemon` subcommand** + machine-readable `status` | Today the daemon is three recursive global booleans (`--server`/`--daemon`/`--stop`) with a foot-gun and no JSON status. Lifecycle logic already exists; only the surface is wrong. Unblocks agent introspection of the warm host. | M |
| 4 | **Synthetic-input transport via `SendInput`** + window management | Unblocks the documented-but-stubbed `--method input` (click on canvas/Electron surfaces UIA can't actuate), `press` (Tab/Enter/arrows), and the entire `window move/resize/min/max/close` group (peeku has zero window manipulation today). High agent value, mostly mechanical. | M–L |

Moves 1 and 2 are pure correctness/ergonomics on the existing substrate and should ship **before** any new surface. Move 3 is surface rewiring over done logic. Move 4 is the real breadth work.

**Honest framing of the input model:** peekaboo's background-vs-foreground model rests on macOS `CGEventPostToPid` (process-targeted event delivery that bypasses the foreground window). **Windows has no clean equivalent** — `SendInput` is global and always lands on the foreground window. So peeku must **reframe, not port**: UIA-pattern actions ARE the background path (no event injection at all, genuinely better than peekaboo for UIA-cooperative apps); `--foreground` opts into `SetForegroundWindow` + `SendInput`. `PostMessage(WM_*)` is the only process-targeted analog and is unreliable on WPF/UWP/Electron — offer it as a best-effort fallback only, never the default.

**Focus-steal warning (foreground path).** The daemon is a long-lived shared host; a `--foreground` `SendInput` action **steals focus from whatever the human is doing** — peekaboo's `CGEventPostToPid` avoids exactly this, and there's no Windows analog. The serial server loop prevents two CLI calls from racing the input queue, but it does **not** stop a foreground op from hijacking the human's desktop. Mitigations to ship with the foreground path: serialize all foreground ops (already implied by the serial loop), and **capture the prior foreground window before the op and restore it after**. Document the steal as inherent to `--foreground`; keep background (UIA-pattern) the default precisely because it never steals focus.

---

## 2. The refId fix (do first)

**This is the #1 agent-workflow blocker.** Verified by reading the daemon code. Two structural facts combine into one bug, and both are fixed independently.

**Scope: daemon path only.** The bug manifests **only when the daemon is alive** (the default-when-running mode, `CliPeekuClient.cs:36`), because only the daemon snapshot routes through `StoreHandle`→`h:` ids (`Snapshot.cs:79,130`). The **in-proc** path (`UiaClient.UiaSnapshotAsync`, `UiaClient.cs:80,131`) emits durable `uia:` ids directly with no `h:` cache, and actions re-walk via `FindByRefId` — so cross-process observe→act **already works** when the daemon is down. Don't hunt for this bug in the in-proc path; it isn't there. It's still the #1 fix because the daemon is the warm default agents hit.

### 2.0 Root cause (verified)

1. **Per-connection session.** `JsonRpcConnection.ProcessAsync` (`JsonRpcConnection.cs:37`) does `using var session = new DaemonSession();` *inside* the per-connection loop. `DaemonServer.RunAsync` (`DaemonServer.cs:39-60`) creates a fresh pipe per accepted connection. So **each CLI process = one connection = one brand-new `DaemonSession` = one brand-new `DaemonPeekuClient` = one empty `HandleIdCache`** (`DaemonPeekuClient.cs:25`). On dispose, the actor thread tears down `UIA3Automation` and the cache.

2. **Snapshot ships ephemeral `h:` ids, never the durable id.** `UiaSnapshotAsync` (`Snapshot.cs:79,130`) calls `StoreHandle(...)`, which computes the durable key (`UiaRefId.Create`) but passes it only as the dedup `stableKey`; `HandleIdCache.Store` (`HandleIdCache.cs:71-88`) returns a freshly minted `h:<guid>`. So every `RefId` on the wire is `h:<guid>`.

**Correction to the original briefing:** the recompute-fallback **already exists** — `ResolveActionElement` (`Helpers.cs:54`) and `ElementGetAsync` (`FindGet.cs:295`) both call `FindByRefId`, which re-walks the live tree computing `UiaRefId.Create` until match (`UiaSearch.cs:8-63`). It never fires for the snapshot→action flow because of the guard at `Helpers.cs:34`:

```csharp
if (_handles.TryGet(refId, out var cached)) { return ...; }   // miss: new process, empty cache
if (_handles.IsHandleId(refId))                                // refId starts with "h:" -> TRUE
{
  return ElementNotFound("Element handle not found.", new { refId });  // short-circuit; fallback never reached
}
// FindByRefId fallback lives BELOW here, unreachable for h: ids
```

Snapshot emits `h:` → every cross-process ref hits the `IsHandleId` short-circuit → `"Element handle not found."`. The durable `uia:pid:hash` path with its working fallback is dead code from the snapshot flow's perspective. **FIX A** makes the cache survive across connections; **FIX B** makes snapshot emit the durable id so the existing fallback becomes reachable. Both are needed — A alone breaks on eviction/TTL/restart; B alone pays a full re-walk on every action in the common same-daemon case.

### 2.1 FIX A — process-lifetime singleton `DaemonSession`

One `DaemonSession` (one actor thread, one warm `UIA3Automation`, one `HandleIdCache`) for the daemon process lifetime, shared across all connections.

| File | Change |
|---|---|
| `src/peeku.Daemon/JsonRpcConnection.cs` | Inject the shared `DaemonSession` via ctor; **delete** `using var session = new DaemonSession();` (line 37); dispatch with the injected `_session`. Connection no longer owns/disposes the session. |
| `src/peeku.Daemon/Program.cs` | Create `using var session = new DaemonSession();` **once**; pass to the `JsonRpcConnection` ctor. `using` disposes on every exit path (cancel/exception/normal) — actor thread joins, `UIA3Automation` disposes cleanly at process exit. |
| `src/peeku.Daemon/DaemonPeekuClient.cs` | `HandleIdCache` tuning: `capacity 2000 → 8000`, `ttl 2 → 5 min` (see below). |
| `DaemonSession.cs` / `DaemonServer.cs` | **No change.** `DaemonSession` is already a self-contained serial actor; `DaemonServer` already loops connections serially. |

**Thread-safety (zero new locks).** `DaemonServer.RunAsync` accepts **one connection at a time** (serial `while`, no fan-out). Within a connection requests dispatch sequentially. `DispatchAsync` marshals all UI work onto the single actor thread via `session.ExecuteAsync`. `HandleIdCache` (plain `Dictionary`/`LinkedList`) is touched **only** on that actor thread. → Connection B can't start until A returns; even concurrent enqueues serialize on the actor's `BlockingCollection`. Leave a comment in `Program.cs` noting the singleton assumes the serial server loop (a future concurrent server still serializes UI work but would interleave enqueues).

**Apartment safety (why cached COM refs are sound).** `DaemonSession` runs exactly **one** actor thread (`DaemonSession.cs:38`, MTA, no `SetApartmentState`) that lives for the process lifetime, and all UI work marshals through `ExecuteAsync`. So the singleton **preserves** today's one-thread-per-process invariant — cached `AutomationElement` COM refs are only ever touched on that one MTA actor thread and never cross apartments. State this explicitly in the `Program.cs` comment: **the cache and its COM refs are safe only because the actor thread is MTA and process-lifetime-stable.** A future maintainer who adds a second actor thread or switches to STA would silently break this — make the invariant loud at the singleton's creation site.

**Cache tuning rationale (also fixes a *same-process* bug).** A single `UiaSnapshotAsync` caps at `MaxNodes: 5000` and stores a handle per node. With `capacity: 2000 < 5000`, `PruneCapacity` evicts the earliest-stored nodes (root + early children) **during the same snapshot** — a follow-up action misses immediately, independent of cross-process. Fix: `capacity: 8000` (above the 5000 ceiling + headroom), `ttl: 5 min` (an observe→act loop can span minutes; never `0` = "always expired"). Tie `capacity ≥ MaxNodes + slack` as a named constant so they can't drift.

**Speed win (the real reason the daemon exists).** Today every CLI command pays: new actor `Thread` start + `new UIA3Automation()` COM init + first cold UIA call. As a singleton, those happen **once** at daemon start; per-command cost drops to a pipe round-trip + the actual UIA op. **FIX A is the single highest-leverage change in this design — correctness *and* the daemon's whole reason for being.**

### 2.2 FIX B — durable `uia:pid:hash` refIds + reachable fallback

Make `uia.snapshot`/`see`/`find`/`element.get` return the **durable** `uia:pid:hash` refId so action resolution falls through to the existing `FindByRefId` re-walk when the live handle is gone.

**Wire-id decision (Option B1, recommended):** `RefId = uia:pid:hash` is the public id; keep the `h:` handle internal for the fast path. Single durable id on the wire, backward-compatible `ElementRef.RefId` field (`Contracts.cs:52`), fallback always reachable. (Rejected B2 dual-id: needs a schema field + plumbing and the `h:` primary still hits the short-circuit on miss.)

| File | Change |
|---|---|
| `src/peeku.Daemon/DaemonPeekuClient.HandleCache.cs` | `StoreHandle` **returns the durable id** (`UiaRefId.Create`) instead of `h:`. Still mints `h:` internally + indexes `stableKey`. Fall back to returning `h:` only if `UiaRefId.Create` yields nothing (no RuntimeId/props — rare); document such refs as non-durable. |
| `src/peeku.Daemon/HandleIdCache.cs` | Add `public bool TryGetByStableKey(string stableKey, out T value)` reusing the existing `_byStableKey` index (`HandleIdCache.cs:20,83`); route through `TryGet` so TTL/LRU touch still applies. |
| `src/peeku.Daemon/DaemonPeekuClient.Helpers.cs` (`ResolveActionElement`, refId branch) | Add **Fast path 2** (`_handles.TryGetByStableKey(refId, …)`) before the `IsHandleId` guard. On durable-id miss, the ref is `uia:` not `h:`, so it sails past the guard into `FindByRefId`. Scope the re-walk root to the ref's **pid** (parsed from `uia:<pid>:<hash>`, `UiaRefId.cs:17,48`) via a new `ResolveRootForPid(pid)` (reuse `Win32Windows.ListWindows` filtered by `ProcessId`). `StoreHandle(found)` re-caches after a successful walk (already at `Helpers.cs:66`). |
| `src/peeku.Daemon/DaemonPeekuClient.Helpers.cs` (`ResolveActionElement`, **selector→cached branch**, `Helpers.cs:126`) | **Must change too.** This branch does `_handles.TryGet(matches[0].RefId, …)` directly — and `TryGet` returns false for any non-`h:` id (`HandleIdCache.cs:99`). Once `matches[0].RefId` is `uia:`, this branch regresses to `"Element handle not available."` **in the same daemon process**. Fix: resolve the matched element from the just-built snapshot's element list (preferred — the element is in-hand), or route through `TryGetByStableKey` then `FindByRefId` on miss. Do **not** leave it on raw `TryGet`. |
| `src/peeku.Daemon/DaemonPeekuClient.FindGet.cs` (`ElementGetAsync`, refId branch) | Same one-line Fast-path-2 addition for symmetry. |
| `src/peeku.Daemon/DaemonPeekuClient.FindGet.cs` (`ElementGetAsync`, **selector→cached branch**, `FindGet.cs:431`) | **Must change too** — same `_handles.TryGet(matches[0].RefId, …)` regression as the Helpers selector branch. Resolve from the snapshot element list or via `TryGetByStableKey`+`FindByRefId`. |
| `UiaSearch.cs` / `FindByRefId` | **No change** — already re-walks computing `UiaRefId.Create` and matching ordinal. It was just unreachable. |

**Re-walk cost (don't oversell the scoping).** `FindByRefId` already re-walks (`UiaSearch.cs:8-63`), capped at `maxNodes:20_000`. Scoping the root to the ref's pid avoids a full-desktop walk, but a pid can own **many** top-level windows and the re-walk is still a full-subtree DFS per window — for a pid with a large visual tree (Explorer, browser) the fallback cost ≈ **one fresh snapshot of that pid's windows**, not a cheap lookup. That's acceptable because FIX B's re-walk only fires on the **fallback** path (TTL-expired / LRU-evicted / daemon-restarted); the common same-daemon case stays on Fast-path-2 (a dictionary hit). Frame it as "fallback cost ≈ one snapshot of the pid's windows," not "scope the re-walk away."

**How it survives each failure mode:**

| Failure | FIX A alone | FIX A + FIX B |
|---|---|---|
| Separate CLI process, same daemon, within TTL | ✅ live cache hit | ✅ live cache hit (Fast path 2) |
| Handle TTL-expired | ❌ `ElementNotFound` | ✅ re-walk by pid |
| Handle LRU-evicted (cache full) | ❌ `ElementNotFound` | ✅ re-walk by pid |
| Daemon restarted between snapshot and action | ❌ empty cache | ✅ re-walk by pid (durable id needs no prior state) |
| Target window closed / pid dead | ❌ | ✅ clean `ElementNotFound` with pid context |

**Known limitations to document (not fix):**
- **Position-sensitivity.** `UiaRefId.Create` prefers `RuntimeId` (stable across moves) but falls back to a property+`BoundingRectangle` hash for the minority of controls lacking RuntimeId — those ids are **position-sensitive** (re-walk fails if the element moved between snapshot and action). RuntimeId-based ids are the common, stable case.
- **Identity-collapse under hash collision.** `Store` treats `stableKey` as a dedup key (`HandleIdCache.cs:59-69`): two elements whose `UiaRefId.Create` collides (the position-hash fallback for RuntimeId-less controls) share one handle slot — the second `Store` overwrites the first (`HandleIdCache.cs:63`). With durable ids now the public RefId, a collision means two snapshot nodes ship the **same** RefId and resolve to whichever was stored last. Low frequency (requires a hash collision among RuntimeId-less controls in one snapshot), but worth a guard or at least a documented caveat.

### 2.3 Sequencing, tests, risks

**Sequence: FIX A first** (smallest diff — `JsonRpcConnection.cs` + `Program.cs` + one cache line — fixes the common case *and* delivers the latency win), **FIX B second** (additive; hardens against eviction/TTL/restart; the re-cache after re-walk is only durable because A keeps the cache alive). They compose: A makes the live cache survive; B makes correctness independent of the cache.

**Tests:**
- **Regression (the bug):** two sequential RPC round-trips against one shared session — connection 1 `uia.snapshot` capturing a refId, connection 2 `element.get --ref <refId>` → expect **Ok**, not `ElementNotFound`. Fails on current code, passes after A.
- `JsonRpcConnection` no longer disposes the injected session (still usable after `ProcessAsync` returns).
- `HandleIdCache` capacity ≥ MaxNodes: store 5000 elements (capacity 8000), read first-stored back → hit.
- Disposal at shutdown: after `server.shutdown`, session disposed + actor thread joined (`ExecuteAsync` throws `ObjectDisposedException`).
- **FIX B:** snapshot `RefId` matches `^uia:\d+:[0-9a-f]+$`; force eviction (`capacity:1`/`ttl:0`) then `set-value --ref uia:...` → re-walk hit + **Ok**; fresh client (daemon-restart sim) with a prior `uia:` id → resolves via re-walk; stale `h:` id → clean `ElementNotFound`, no desktop walk; re-walk roots at the ref's pid, not the full desktop.
- **FIX B same-process regression guard (selector→action and selector→get):** in **one** daemon connection, `act --selector ...` and `element.get --selector ...` (the `PreferCachedSnapshot` paths that hit `Helpers.cs:126` / `FindGet.cs:431`) → expect **Ok** after FIX B, not `"Element handle not available."`. This is the path that would regress if the selector branches stayed on raw `_handles.TryGet`. Must pass.

**Risks:** stale handles across a 5-min TTL may resolve to a dead `AutomationElement` (caught + mapped to `Internal`, e.g. `Actions.cs:74`; FIX B's re-walk is the clean recovery). Memory bounded by capacity (8000 COM refs, LRU+TTL pruned). Malformed `uia:` pid segment → `InvalidArgument`, not a crash.

**Parity tie-in:** this brings peeku to peekaboo's `snapshot_id → action` model — the durable `uia:pid:hash` is peeku's equivalent of peekaboo's stable per-element id (recomputable from the live tree); `FindByRefId` is the equivalent of peekaboo "re-resolve the snapshot element against live UI at action time"; FIX A is the warm long-lived host peekaboo gets from its host process. `ElementRef.SnapshotId` (`Contracts.cs:54`) is already plumbed for future scoping of the re-walk.

---

## 3. Parity gap matrix

One comprehensive table merging input-parity, open P1/P2, and peekaboo-inventory deltas. Feasibility = Windows feasibility. macOS-only SKIP items at the bottom.

| Capability | peekaboo form | peeku status | Proposed Windows form | Feasibility | Priority | Effort |
|---|---|---|---|---|---|---|
| **refId stable across CLI calls** | snapshot_id + re-resolve at action time | **broken** (per-connection cache; `h:` ids) | §2 FIX A (singleton session) + FIX B (durable `uia:pid:hash`) | moderate | **P1a** | M |
| **Selector descendant axis `//`** | name-anywhere deep targeting | missing | §4 `//` combinator in `UiaSelectorEngine`; both snapshot+live callers inherit | moderate | **P1a** | M |
| **`daemon` subcommand + status** | `daemon start\|stop\|status\|serve` | partial (3 global booleans, no JSON status) | `daemon` Command, 4 children; `status` default emits `{running,pid,pipeName,startedAt,protocolVersion,buildVersion}`; reuse existing `RunServerAsync`/`StartDaemonAsync`/`StopDaemonAsync` | easy | **P1a** | M |
| **Background vs foreground input MODEL** | `CGEventPostToPid` bg; `--foreground` opt-in | partial | **Reframe, don't port.** UIA patterns = background (already work). Add `--foreground` (default false) that does `Win32Windows.BringToForeground` + `SendInput` for the synthetic path. No `CGEventPostToPid` analog exists; `PostMessage` is best-effort fallback only | moderate | **P1b** | M |
| **Synthetic click (`--method input`)** | `click` posts mouse-down/up CGEvent | missing (stub at `UiaClient.Actions.cs:50` "Input click not supported yet") | `SendInput` `MOUSEINPUT`: move to rect-center (`MOUSEEVENTF_ABSOLUTE` normalized to 65535) then `LEFTDOWN`/`LEFTUP`; requires `--foreground`. Reuse `HotkeyInputInjector`'s already-declared-but-unused `MOUSEINPUT` struct | moderate | **P1b** | M |
| **Mouse click by coords / double / right** | `--coords x,y`, `--double`, `--right` | missing | Add `--coords` (window-relative default, `--globalCoords` for screen-absolute via `GetWindowRect` offset), `--double`, `--right`. `RIGHT*`/double-down-up pairs within `GetDoubleClickTime()` | easy | **P1b** | M |
| **Window move / resize / set-bounds** | `window move\|resize\|set-bounds` | missing (only `BringToForeground` today) | `SetWindowPos(hwnd,…,x,y,w,h,flags)`: set-bounds = one call (`SWP_NOZORDER`), move = `SWP_NOSIZE`, resize = `SWP_NOMOVE`. hwnd from existing target flags + `Win32Windows.ToHwndHex` | easy | **P1b** | M |
| **Window min / max / close / restore** | `window minimize\|maximize\|close` | missing | `ShowWindow(hwnd, SW_MINIMIZE\|SW_MAXIMIZE\|SW_RESTORE)`; close via `WM_CLOSE` PostMessage (graceful, not force — document `app quit --force` for kill) | easy | **P1b** | S |
| **App launch** | `app launch`, `--wait-until-ready`, `--open`, `--no-focus` | missing | `Process.Start(UseShellExecute=true)`; `--wait-until-ready` = `WaitForInputIdle` + poll for top-level window; `--no-focus` = skip `SetForegroundWindow`. UWP via `shell:AppsFolder\<AUMID>` (second form). No bundle-id | easy | **P1b** | M |
| **App quit / force-quit** | `app quit`, `--all --except`, `--force` | missing | Graceful: `WM_CLOSE` to top-level windows of pid then poll. Force: `Process.Kill()`. `--all --except` = filtered loop. Document `WM_CLOSE` can hang on modal save dialog → force fallback + timeout | easy | **P1b** | M |
| **`press` (named keys / sequences)** | `press enter`, `press tab tab`, `--count`/`--hold` | partial (only `hotkey` exists) | New `press` command. `HotkeyInputInjector` already ships the **entire** named-key/function/numpad/OEM VK table + extended-key handling + chord builder (`TryGetNamedKeyVk`), so `press` is ~90% **wiring**, not new key mapping. Emit keydown/keyup per key, `--count`/`--delay`/`--hold`. Foreground-only (`SendInput` global). Needs only foreground keyboard focus — **not** the mouse `MOUSEINPUT` transport — so it can ship ahead of the `[L]` synthetic-mouse lift | easy | **P1a (tail)** | S |
| **`run <script>` command** | `run script.json`, `--output` | partial (`batch` + stdin exist) | `run <scriptPath\|->` reusing batch pipeline; `--output <path>` honoring `ctx.Format`; `--continue-on-error` (real bool, `StopOnError = !flag`). Register `batch`+`run` as one shared method | easy | **P2** | M |
| **Shell completions** | `completions <shell>` | missing | **Static generated script** (option a): enumerate live `RootCommand` tree at gen time → `Register-ArgumentCompleter` block (+bash/zsh/fish), pipeable to `$PROFILE`. NOT dynamic `[complete]` (SCL 2.0.2 pin: each completion spawns the cold exe; PS shim weak). Test asserts every verb appears | moderate | **P2** | M |
| **config command + precedence** | `config init\|show` + layered precedence | partial (`--profile` is a dead no-op) | Phase 1 (P1 [S]): **remove dead `--profile`**. Phase 2 (P2 [L]): flags > env (`PEEKU_TIMEOUT/FORMAT/LOG_LEVEL`) > `%APPDATA%\peeku\config.json` > defaults; `config init\|show\|path` | moderate | **P2** | L |
| **`--plain` output** | greppable plain mode | missing | Add `Plain` to `OutputFormat` (`CliContext.cs:5`) + recursive `--plain`. TAB-separated lines for list-shaped results only (`windows list`, `find`, `see`); non-row falls back to compact JSON | moderate | **P2** | M |
| **Enum binding `--method`/`--direction`/`--includeProperties`/`--log-level`** | closed-set flags as enums | missing (string + hand validators) | Replace `Option<string>`+validator with `Option<ActionMethod>`/`Option<ScrollDirection>`/`Option<UiaPropertiesMode>` (enums exist `Contracts.cs:211/243/129`). Delete `ParseMethod`/`ParseScrollDirection`/`ParseProps`. SCL parses case-insensitively. Pure CLI rewire | easy | **P2** | M |
| **Synthetic text type (real keystrokes)** | `type --text` via CGEvent keystrokes | partial (UIA Value-pattern only) | Add synthetic mode under `--foreground`/`--method input`: `SendInput` `KEYEVENTF_UNICODE` per char (no VK mapping); honor `--delayMs`. Keep Value-pattern default (better — background). **Headline justification: password fields** — many block UIA `ValuePattern.SetValue` for security, so the Value-pattern default genuinely fails there and real keystrokes are the only path. Also covers shortcut-key-only controls and focus-dependent IME/text | easy | P2 | M |
| **drag / swipe** | smooth multi-step CGEvent drags | missing | `SendInput` mouse-down at from-center, interpolated moves over `--steps`, mouse-up at to-center (foreground). swipe = same primitive. Drop `--to-app Trash` (macOS-specific) | moderate | P2 | M |
| **App relaunch** | `app relaunch` | missing | Compose quit+launch: capture `Process.MainModule.FileName` before quit, re-Start. `--wait` = delay | easy | P2 | S |
| **App list** | `app list` | partial (`windows list` is per-window) | `app list` = distinct processes with a top-level visible window via `Process.GetProcesses()` grouped + foreground-pid `active` flag | easy | P2 | S |
| **Window focus (specific window)** | `window focus` | partial (`BringToForeground` exists `Win32Windows.cs:113`, already invoked internally `ActionSelectionResolver.cs:269`, unexposed) | Surface as `window focus` with standard target flags — near-zero new code over the existing `BringToForeground`, and focus-then-input is the foreground primitive. Add `AttachThreadInput`/`AllowSetForegroundWindow` unlock dance (SetForegroundWindow silently fails when caller isn't foreground) | moderate | **P1a (tail)** | S |
| **menu (click item by path, list)** | `menu click --path "File > Export"`, `menu list` | missing | Walk UIA `MenuBar`/`Menu` subtree: find `ControlType.MenuBar`, `ExpandCollapse` each `MenuItem` along path, `Invoke` leaf. `menu list` = snapshot subtree to JSON (+`AcceleratorKey`). Menus virtualize → Expand-then-re-find per level. peeku already detects ExpandCollapse (`UiaClient.ElementGet.cs:318`) | moderate | P2 | L |
| **scroll** | `scroll --direction --amount` | **present** | Done via UIA Scroll pattern (`UiaClient.Actions.cs`). Optional: `MOUSEEVENTF_WHEEL` fallback for elements lacking the pattern (low value) | easy | P3 | S |
| **move (cursor hover)** | `move x,y`/`--to`/`--center` | missing | `SetCursorPos` or `SendInput MOUSEEVENTF_MOVE\|ABSOLUTE`; `--to` resolves rect-center. Skip `--profile human`. Low standalone value (click/drag subsume it) | easy | P3 | S |
| **dialog convenience wrapper** | `dialog click\|input\|file\|dismiss` | partial | Windows dialogs are top-level windows (class `#32770`) — existing find/click/set-value already handle them once targeted. Thin wrapper to auto-locate the active modal is sugar, not new capability | easy | P3 | S |
| **paste (clipboard + restore)** | `paste`, `--restore-delay-ms` | missing | Optional. STA-thread clipboard set → Ctrl+V via hotkey path → restore. Niche; type/set-value cover most | moderate | P3 | M |
| **App hide / unhide** | `app hide\|unhide` | missing | Approximate via `ShowWindow(SW_MINIMIZE)` (no true app-hide on Windows). Low value — minimize covers it | moderate | P3 | S |
| **App switch (`--to`/`--cycle`)** | `app switch --to`/`--cycle` | missing | `--to` = `window focus` on main window. Skip `--cycle` (Alt+Tab synthesis is shell-special-cased, flaky) | moderate | P3 | S |
| **menubar / tray extras** | `menu click-extra`, `menubar list` | missing | Approximate via notification-area UIA toolbars. Fiddly, semantics differ, most tray items have no peer. Low priority | hard | P3 | M |
| **`sleep` / `clean` script primitives** | `sleep ms`, `clean` | missing | `sleep` = `Task.Delay` (useful for pacing flaky UIs in run scripts); `clean` = prune snapshot/handle cache + marker | easy | P3 | S |
| Exit-code map | mostly 0/1 | **present (exceeds)** | P0 landed 0–8 off `PeekuErrorCode`. No action | — | done | — |

### SKIP (macOS-only — no honest Windows analog)

| Capability | Reason to skip |
|---|---|
| **`space` (Mission Control / virtual desktops)** | Windows Virtual Desktops exist but only via undocumented `IVirtualDesktopManagerInternal` COM, version-fragile per build. Skip unless users ask. |
| **`dock`** | Taskbar is the loose analog; pinned-taskbar automation is brittle and pointless when `app launch` (`Process.Start`) launches apps directly. |
| **AppleScript / osascript** | No Windows counterpart. peeku's UIA+Win32+COM substrate **is** the equivalent capability. |
| **`agent` (NL AI loop)** | Deliberate scope boundary — peeku is the tool layer; the NL loop is the MCP consumer's job. |
| **`permissions request-*` (TCC grants)** | Windows has no TCC; UIA/capture work without per-app consent. `doctor`/`doctor --deep` is the "are my capabilities working" analog. (Closest real concern: UIPI/integrity-level — a medium-IL CLI can't drive an elevated app. Worth a `doctor` note, not a command.) |
| **`paste --restore-delay-ms` semantics, `bridge`, Retina scaling, menu-extras** | macOS-specific primitives / TCC broker / display model with no Windows equivalent. |

---

## 4. Selector descendant axis (#2 ergonomics gap)

**Problem (live-tested):** the selector DSL has no descendant combinator. The grammar is `selector := segment ("/" segment)*` (child axis only, per [`learned/selector.md`](./learned/selector.md)). Deep elements force agents to spell the full ancestor path (`window[name~="PowerShell"]/tab/list/tabitem[name="PowerShell"]`). This is the single-highest agent-ergonomics fix and is **not yet a roadmap item — promote to P1.**

**Why it's cheap: one engine, two callers.** `src/peeku.Core/Uia/UiaSelectorEngine.cs` holds `Parse` + generic `Select<TNode>`. Both `UiaSelectors.cs` (snapshot `UiaNode` tree) and `UiaLiveSelectors.cs` (live FlaUI tree) call `UiaSelectorEngine.Parse` then `.Select`. A descendant axis added to the parser + segment model + Select loop is **inherited by snapshot AND live in one change, zero duplication.**

**Design — add `//` descendant combinator:**

1. **Segment model:** add `bool Descendant` to `SelectorSegment` (`UiaSelectorEngine.cs:188`). `/` = child axis (current); `//` = descendant-or-self-of-children. Default `false` keeps every existing selector byte-identical.
2. **Parser** (`SelectorParser.Parse`, line 194): today `expr.Split('/', RemoveEmptyEntries)` **silently swallows `//`** (empty middle token removed) — that's why `//` is a no-op today, *not* an error. Replace with a hand scan that tokenizes on `/` but treats a run of two slashes as a descendant marker on the **following** segment. **Track bracket depth** while scanning so `//` inside a quoted filter value (`[name="a/b"]`) is literal, not an axis. Leading `//foo` = "find foo anywhere under root" (the common case).
3. **Select loop** (line 58): for a `Descendant` segment, replace the direct-children gather with a **bounded DFS** over the subtree of each node in the current frontier, collecting nodes where `SegmentMatches` is true. Reuse the existing `children` delegate. Add a depth/visit cap (~10k visited, in the spirit of snapshot `maxNodes`) + the existing `ct.ThrowIfCancellationRequested()` cadence. De-dupe with a `visited` HashSet (keyed by `RefId` for snapshot, RuntimeId/object for live). Preserve DFS pre-order for deterministic, stable results like the child-axis docs promise.
4. **Limit semantics:** the existing `limit` (Take) still applies at the end.

**Canonical form:** document `//button[name~="Save"]` as the "deep one-shot." Ship **only `//`** — skip a second `**` syntax (one concept, less surface; `//` already delivers name-anywhere).

**Parser edge cases (tests):** trailing `//` → error `"descendant axis requires a following segment"`; `///` → error; `//` inside a quoted filter value → literal (not tokenized); leading single `/` → trimmed as today.

**Docs:** update `learned/selector.md` grammar to `selector := segment ( ("/"|"//") segment )*` + a worked example. **MCP ripple: none** — selector is a single `expr` string; `//` flows through unchanged. Document the operator in the field description.

**Feasibility** moderate (parser rewrite + one DFS branch). **Effort M.** Promote to **P1**.

---

## 5. Prioritized roadmap to parity

Waves by impact × risk. Each tagged **[S/M/L]**, **[breaking]** where the surface changes. Honest about what's NOT worth doing.

### Wave P1a — agent-workflow unblockers (correctness + ergonomics; do before any new surface)

- [ ] **refId daemon-session fix — FIX A** (singleton `DaemonSession`): inject shared session into `JsonRpcConnection`, own it in `Program.cs`, cache `2000→8000`/`2→5min`. Ship the cross-connection regression test. **[M]**
- [ ] **refId daemon-session fix — FIX B** (durable `uia:pid:hash` refs): `StoreHandle` returns durable id, `TryGetByStableKey`, pid-scoped re-walk. **[M]**
- [ ] **Selector descendant axis `//`** in `UiaSelectorEngine` (§4); both callers inherit; update `selector.md`. **[M]**
- [ ] **`daemon start|stop|status|serve` subcommand**: 4 children over existing lifecycle logic; `status` (default) emits machine-readable envelope; remove `--server`/`--daemon`/`--stop` + the `--stop requires --daemon` foot-gun; route stop-not-running→exit 2, unreachable→exit 5. **[M] [breaking]**
- [ ] **`press` command** (P1a tail — cheap keyboard unblocker): reuse `HotkeyInputInjector` VK table (~90% wiring); `--count`/`--delay`/`--hold`. Needs only foreground **keyboard** focus, not the mouse `MOUSEINPUT` transport, so it unblocks Tab/Enter/arrow-driven navigation — a very common agent loop — without waiting on the `[L]` synthetic-mouse lift. Add the `--foreground` focus + restore-prior-foreground behavior alongside it. **[S]**
- [ ] **Surface `window focus`** (P1a tail — near-zero new code): expose the existing `BringToForeground` (`Win32Windows.cs:113`) with standard target flags + the `AttachThreadInput`/`AllowSetForegroundWindow` unlock dance. Pairs with `press` to make focus-then-keyboard navigation work before the full input lift. **[S]**

### Wave P1b — core interaction parity (the breadth peekaboo has, peeku stubs)

- [ ] **`--foreground` flag + synthetic-MOUSE transport** (`SendInput` `MOUSEINPUT`): the shared primitive that unblocks the mouse rows below; reuse `HotkeyInputInjector` structs. Replaces the `UiaClient.Actions.cs:50` stub. Plumb through CLI/MCP/daemon (default background). Ship with foreground focus-steal mitigation (capture + restore prior foreground window). Keyboard-press already landed in P1a; this wave is the mouse `MOUSEINPUT` work. **[L]**
- [ ] **Synthetic click + coords/double/right**: `--coords`/`--globalCoords`/`--double`/`--right`; window-relative via `GetWindowRect`. **[M]**
- [ ] **Window management group**: `window move|resize|set-bounds` (`SetWindowPos`) + `window minimize|maximize|close|restore` (`ShowWindow` / `WM_CLOSE`). (`window focus` already surfaced in P1a tail.) **[M]**
- [ ] **App lifecycle**: `app launch` (`Process.Start`, `WaitForInputIdle`, `--no-focus`, UWP AUMID form) + `app quit` (`WM_CLOSE` graceful / `Process.Kill` force, `--all --except`). **[M]**
- [ ] **`menu` group** (`menu click --path`, `menu list`): UIA MenuBar walk with Expand-then-re-find per virtualized level. High Windows value. **[L]**

### Wave P2 — completeness (contract polish + remaining surface)

- [ ] **`run <script>`** (reuse batch engine; `--output`; `run -`; `--continue-on-error`). **[M]**
- [ ] **Enum binding** for `--method`/`--direction`/`--includeProperties`/`--log-level`; delete hand validators. Lowest-risk; feeds completion. **[M]**
- [ ] **Shell completions** (static generated script, PowerShell-first +bash/zsh/fish; test asserts every verb). **[M]**
- [ ] **`--plain`** TAB-separated lines for list-shaped results. **[M]**
- [ ] **Remove dead `--profile`** (Phase 1 of config). **[S] [breaking]**
- [ ] **config story** (`%APPDATA%\peeku\config.json`, `PEEKU_*` env, precedence, `config init|show|path`). **[L]**
- [ ] **Synthetic text type** under `--foreground`/`--method input` (`KEYEVENTF_UNICODE`); Value-pattern stays default. **[M]**
- [ ] **drag / swipe** (`SendInput` interpolated path); needs the mouse transport first. **[M]**
- [ ] **App relaunch / app list** (compose + group over `windows list`). **[S]**

### Wave P3 — low-value / nice-to-have (ship only if cheap alongside the above)

- [ ] **`move` (cursor hover)** — trivial but click/drag subsume it. **[S]**
- [ ] **`sleep` / `clean`** script primitives. **[S]**
- [ ] **`dialog` convenience wrapper** — generic UIA already covers dialogs; only value is auto-locating the active modal. **[S]**
- [ ] **Synthetic-wheel scroll fallback** — most scrollables expose the UIA pattern already. **[S]**
- [ ] **App hide/unhide, app switch `--to`** — collapse to minimize / window focus. **[S]**

### Explicitly NOT worth doing

- **`menubar`/tray extras** — feasible but fiddly, semantics differ from macOS menu extras, most tray items have no automation peer. Skip unless a concrete user need appears.
- **`app switch --cycle` (Alt+Tab synthesis)** — the switcher is shell-special-cased; synthesis is flaky.
- **`PostMessage(WM_*)` as a default input path** — unreliable on WPF/UWP/Electron. Best-effort fallback only, never default.
- **`PublishTrimmed` / NativeAOT** — trim-unsafe with FlaUI/UIA3/COM/WinRT (per `cli-refinement-plan.md` §6). R2R is the ceiling.

---

## 6. Non-goals

What peeku should **not** copy from peekaboo, and why:

| Non-goal | Reason |
|---|---|
| **NL agent loop (`agent`)** | peeku is the tool layer. The loop lives in the MCP consumer (this harness). Copying it duplicates the consumer and pulls AI/provider deps into a library-first CLI. |
| **`CGEventPostToPid` literal port (true background synthetic input)** | No Windows equivalent. `SendInput` is global; `PostMessage` is unreliable on modern frameworks. The honest Windows model is "UIA patterns = background (better), `--foreground`+`SendInput` = real input." Pretending otherwise would ship a flaky default. |
| **`space` / virtual-desktop control** | Windows Virtual Desktops are undocumented `IVirtualDesktopManagerInternal` COM, version-fragile per build. Not worth the maintenance tax for a niche capability. |
| **`dock` / taskbar automation** | Brittle, and `app launch` via `Process.Start` is the deterministic path agents actually want. |
| **AppleScript bridge** | No counterpart. UIA+Win32+COM **is** peeku's equivalent substrate, not a missing feature. |
| **TCC permission-grant flow (`permissions request-*`)** | Windows has no TCC. UIA/capture need no per-app consent. `doctor` is the capability check. (Document the UIPI/integrity-level caveat in `doctor`, don't build a permissions command.) |
| **`bridge` / Bridge-host TCC broker** | macOS-specific socket broker for TCC. peeku's daemon is a warm-state host, not a permission broker — different purpose, no port. |
| **Flag-name kebab rename** | Locked owner decision — camelCase stays. Not a parity item. |
| **Back-compat aliases for renamed flags/tools** | Pre-1.0, break freely (locked decision #3). Aliases are accreted surface, not value. |
| **`--profile` as a live flag (today)** | A documented no-op that silently swallows input misleads agents. Remove it; reintroduce only as a real config-section selector if the config story lands. |

---

*Authored from five investigation outputs (peekaboo inventory, peeku current state, refId fix design, input/automation parity gaps, open P1/P2 + selector-axis gaps). All file/line references verified against post-P0 source. Roadmap waves align with [`cli-refinement-plan.md`](./cli-refinement-plan.md) §10 and extend it with the refId fix, selector descendant axis, synthetic input, window/app management, and menu interaction.*
