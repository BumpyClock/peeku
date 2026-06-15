# Sidekick → peeku: clean-room technique adoptions

> Which techniques from `references/Sidekick` (MS-internal "Hyperloop") are worth
> **clean-room** porting into peeku to improve it and close peekaboo-parity gaps.
> **FlaUI stays the base** — prefer FlaUI-reachable implementations; raw-COM is
> not required anywhere (Tier C is empty).
>
> **Clean-room constraint (stated once):** Sidekick is Microsoft-internal/proprietary,
> no OSS license. Re-derive the *technique* (algorithm shape, heuristic ladder) from
> public Win32/UIA docs + FlaUI's public API. Never read or transcribe Sidekick source.
> Name the public doc each heuristic comes from in code comments; keep the constraint in
> each task's acceptance criteria.
>
> Source: 32-agent workflow (catalog → per-technique assessment → rank → adversarial
> skeptic → final), 2026-06-15. FlaUI-reachability of every Tier A item independently
> **verified by binary reflection of `FlaUI.Core.Signed 5.0`**, not docs-claimed.

## TL;DR — highest-leverage clean-room adoptions

1. **RuntimeId snapshot tree-diff** — pure algorithm over data peeku already emits; the
   `refId` *is* the diff key. Closes the parity doc's #1 gap (action-with-diff). No COM.
2. **Not-found "did you mean" hints** — turns every silent `ElementNotFound` into a
   self-correcting step. Pure string ranking. Best value/effort in the deck.
3. **Pattern tokens + inline state** — one snapshot tells the agent what each element can
   *do* and its current *state*; collapses N follow-up `element get` round-trips.
4. **Hit-test (`FromPoint`) + ancestor chain** — the pixel→element bridge that the
   already-planned P1b coordinate-click depends on. Read-only.

> **Out-of-scope pointer:** the real "faster `see`" lever is peeku's own still-unused FlaUI
> 5.0 `CacheRequest.Activate()` scope — a peeku-native task, not a Sidekick adoption. Measure
> cold-walk with/without a cache scope on a dense app before investing.

## Adoption shortlist (sorted by leverage)

| Technique | Gap it closes | parity | FlaUI-reachable | Effort | Planned? | Verdict |
|---|---|---|---|---|---|---|
| RuntimeId tree-diff (snapshot delta) | action-with-diff (FlaUI half) | strong | yes | S–M | no | **adopt-now** |
| Not-found candidate hints | silent ElementNotFound dead-ends | weak | yes | S | no | **adopt-now** |
| Pattern-token + inline state read | actionability+state legible in bulk | strong | yes | S | no | **adopt-now** |
| Hit-test + ancestor chain | pixel→element bridge (enables P1b coords) | strong | yes | S–M | no | **adopt-now** |
| Rich condition-based wait (~20 predicates) | existence-only wait → state predicates | strong | yes | M | no | later (P2 lead-in) |
| Multi-channel UIA event listener | observe breadth (focus-only stub) | strong | yes | M | no | later (observe slice) |
| MSAA WinEvent listener (backstop) | observe breadth + VM/headless reach | strong | no (user32, not raw COM) | M | no | later (observe slice) |
| StructureChanged action-diff (event half) | action-with-diff (live/silent) | strong | yes | M | no | later (after observe) |
| Overlay/popup scanner (snapshot-time) | off-tree dialogs/flyouts missing from `see` | strong | yes | M | no | later (~P2) |
| Overlay-fallback element resolution | flyout-hosted targets unreachable | weak | yes | M | partial | fold (P1b menu/dialog) |
| Work-area-aware window snap/arrange | multi-monitor-correct geometry | weak | partial (user32) | M | partial | fold (P1b window) |
| Drag-with-verification | "did the drag do anything?" | weak | yes | M | partial | fold (P2 drag) |
| Foreground-aware adaptive input | input evidence + headless reach | weak | no (user32) | S–M | partial | fold (P1b --foreground) |
| Input-health preflight + F24 probe | "will keys land?" silent failure | weak | n/a (user32) | S | partial | fold (doctor) |
| Key Tips / access-key exposure | access-key discovery + a11y | weak | yes | S read / M verb | partial | fold (free read now) |
| Dialog auto-locate/classify/dismiss | surprise-modal stall | weak | yes | M | yes | fold (P3) |
| File-dialog id+fallback chains | localized Open/Save robustness | weak | yes | S | yes | fold (P3) |
| Snapshot baseline/compare | regression baselining | weak | yes | S–M | no | later (validation) |
| RTL layout validation | i18n validation | weak | yes | M | no | later (validation) |
| Touch-target size check (WCAG) | a11y validation | none | yes | S | no | later (validation) |
| Cursor-shape wait-until-ready | pixel-free busy gate | weak | no (user32) | S | no | later / skip-ish |
| Taskbar/tray/shell automation | shell-surface targeting | none | partial | S–M/L | yes (SKIP) | fold/skip |
| Per-pattern value DSL | (none — parsing only) | none | irrelevant | — | no | **skip** |

## Tier A — adopt now (verified FlaUI-reachable, not planned)

### A1. RuntimeId snapshot tree-diff — S–M, 4–6 days
Highest leverage. peeku already emits the substrate: flat `Elements[]` keyed by `refId`
+ the `Root: UiaNode` tree. The **refId is already the RuntimeId/composite diff key**
(`UiaRefId.cs:10-48`) — the diff needs **zero UIA calls**.

- New FlaUI-free static `UiaTreeDiff` in `src/peeku.Core/Uia/` (desktop-free, unit-testable
  like `UiaSelectorEngine`). Input: two `UiaSnapshotResult`. Key by `ElementRef.RefId`.
  Build `parentKey` map by walking `Root`. `added = afterKeys − beforeKeys`, `removed = before − after`.
- **CollapseToRoots:** keep key `k` only if its parent isn't also in the same changed set →
  minimal topmost subtree roots; re-attach a depth-trimmed `UiaNode` + ancestor summary.
- `Contracts.cs`: `UiaTreeDelta(Added, Removed)` + `UiaDeltaRoot(Subtree, Ancestors)`.
- **Ship as a standalone `diff` verb over TWO Targets first** (snapshot both now, diff —
  zero new infra). **Correction:** a two-*snapshotId* diff mode is **not buildable** —
  `DaemonPeekuClient.Snapshot.cs` stamps `SnapshotId` as a result *label* with no server-side
  store. Defer/separately budget snapshotId-keyed diff as its own caching task.
- Budget the full verb fan-out honestly (~6–8 surfaces: CLI, `IPeekuClient`, daemon,
  `BatchRunner` arm, `ToolRegistry` descriptor+schemas, MCP dispatcher, `Contracts`, parity tests).
- Residual: carry a `truncated` flag (MaxNodes can make a real subtree look removed);
  RuntimeId-missing nodes use the rect composite key → a moved-unchanged control reads as
  removed+added (document). Enforce identical Depth/MaxNodes/Target across before/after.
- Defer `--diff-on-action` (into `ActionResult.Evidence`, the free-form `JsonElement?` slot)
  until action orchestration is wired — that's B4's job (live event-driven half).

### A2. Not-found "did you mean" candidate hints — S, 1.5–2.5 days
Best value/effort. Snapshot path needs **zero new API** — `snapshot.Elements` is already flat.

- `CandidateHints.Suggest(elements, selector, max:3)` in `peeku.Core/Uia`. Extract intent
  (last selector segment's name/automationId, or positional QUERY); keep non-empty Name/AutomationId;
  rank by substring → named-over-nameless → shortest-name tiebreak; project top-3.
- Wire the snapshot-path miss first (trivial), then fold live-path + per-action `ElementNotFound`
  branches in the same PR (bounded `FindAllDescendants`, cap ~2000 visited / 20 collected).
- **No Contracts break** — `candidates[]` rides the existing `PeekuError.details`. Leave `find`'s
  zero-match-is-not-an-error contract untouched; hints belong on action/resolve error paths only.

### A3. Pattern tokens + inline state read — S, 2–3 days
Append-only; reuses accessors peeku already calls. `ReadProperties` reads no Toggle/Expand/
Range/Selection state today (verified net-new).

- `UiaActionTokens.cs`: supported patterns → terse verbs using **peeku's own CLI verb vocabulary**
  (invoke/toggle/value/expand/pick/scroll/read/grid/range) + always-append focus/click/hover.
- `UiaPatternState.cs`: **guarded by `IsSupported`** (load-bearing perf rule — never pay a
  ValuePattern/ToggleState COM read on unsupported elements across a 5000-node walk); wrap each
  in the existing `Safe()` helper; emit only present states.
- `Contracts.cs`: extend `UiaElement` with optional `Actions` and `State`. Wire into the single
  `ReadElement` choke point; **gate behind `IncludeProperties==All`** so Basic snapshots stay lean.
  Update `ToolRegistry` schemas in lockstep or parity tests flag drift.

### A4. Hit-test (`FromPoint`) + ancestor chain — S–M, 2–3 days
Read-only, and the **enabler for the already-planned P1b coordinate-click** (the uia-backend
doc left element↔point unsolved). `FromPoint`/`Parent` verified present in FlaUI 5.0.

- New partial `UiaClient.HitTest.cs`: `automation.FromPoint(new Point(x,y))` → guard null →
  ElementNotFound. Ancestor walk via `.Parent` (cap ~40), reverse → root-first. Reuse
  `ReadElement` so the hit node returns the same shape as `element get`.
- `Contracts.cs`: `ElementAtPointRequest(X, Y, Target?, IncludeProperties)` + result. Wire
  `peeku_element_from_point` / CLI `element at-point --x --y`.
- `FromPoint` takes physical px matching UIA BoundingRectangle — **no DPI debt**.

## Tier B — adopt later / new slices

- **Rich condition-based wait (~20 predicates), M** — kills `Thread.Sleep` flakiness; every
  predicate maps to a pattern read peeku already wrote. `WaitCondition` enum + `WaitPredicates.cs`;
  route condition-bearing waits through the **live** path. P2 lead-in, right after P1b.
- **Observe-breadth slice (one PR), M** — peeku's observe is a focus-only stub (`UiaObserve.cs:46`).
  Co-land: multi-channel UIA listener (`RegisterNotificationEvent` etc.) + `ObserveEventSet`
  `[Flags]` + `--events array` + **WinEvent backstop** (`SetWinEventHook`, dedicated thread +
  `WM_QUIT` teardown) + TextChanged delta (Property leg). Then **StructureChanged action-diff**
  (B4) folds A1's delta shape into `Evidence`. Fix this doc's earlier stale lines when this lands.
- **Overlay/popup scanner (snapshot-time), M** — hardens `see`: an agent opening a Save/Content
  dialog today gets a snapshot *missing the dialog*. Build the `EnumOverlayCandidates` Win32
  collector **once**, share with P3 dialog + B4 window-diff. ~P2, before P3 dialog.

## Folds (net-new slice only — don't re-propose the planned item)

Build **three shared seams once**: `ResolveRoots(target)` (overlay-fallback, under P1b menu/dialog),
`Win32Arrange` (raw move/resize + rcWork snap presets, into P1b window-mgmt), `EnumOverlayCandidates`
(shared by overlay scanner + P3 dialog + window-diff).

- **Drag-with-verification** → P2 drag; build the `FromPoint` before/after verify loop from day one
  (~0.5d). Extract `TryElementNameAtPoint` now — generalizes to confirming a synthetic *click* landed.
  Gate verify as best-effort advisory (DPI/UIPI false-negatives), never authoritative.
- **Foreground-aware adaptive input** → P1b `--foreground`; the count-evidence half
  (injected-vs-expected on `Evidence`) ships first as an S task; hard-error on `injected==0`.
- **Input-health preflight + F24 probe** → `doctor` as `input.*` checks (no new verb); gate the
  F24 probe behind a flag so plain doctor stays side-effect-free.
- **Key Tips / access-key** → Slice 1 (free now): add `AccessKey`/`AcceleratorKey` `Safe()` reads
  under `All` mode (feeds the planned `menu list`). Slice 2 (later): active Alt-reveal `keytips`
  verb behind P1b `--foreground`.
- **Dialog auto-locate / file-dialog** → already P3. Adopt the escalating dismiss-ladder
  (Escape→WM_CLOSE→UIA Invoke→coord) + WerFault crash classifier + locale-independent file-dialog
  fallback chain (control-type → AutomationId 1148/1 → ValuePattern → Ctrl+A+Unicode-type → Enter).
  Make `dismiss`/`respond` explicit, never an implicit side effect of `list`.
- **Validation wave** (after P1b): one shared `validate`/`check` group — touch-target size (cheapest
  beachhead) → RTL → snapshot baseline (key on **refId**, not Sidekick's positional key).

## Tier C — genuinely raw-COM-only: **empty**

No card needs raw `IUIAutomation`. The non-FlaUI items (`SetWinEventHook`, `AttachThreadInput`+
`SendInput` count, `GetCursorInfo`, `GetMonitorInfo`/`SetWindowPos`) are plain **user32** — the same
layer peeku's hotkey/press/window code already uses, a different thing from raw COM. The one latent
raw-only idea the prior doc gestured at (a NativeAOT raw event-*sink* / ring buffer) is a Sidekick
*implementation artifact*, not a capability requirement — managed `RegisterStructureChangedEvent` +
a bounded `Channel` already deliver the same signal in shipped peeku code. **Don't open a raw-COM track.**

## Skip

- **Per-pattern value DSL** — pure CLI-grammar; closes zero parity capabilities and **inverts two
  locked directions** (one-tool-per-pattern guarded by `ToolParityTests`; the cli-plan move *to* enum
  binding). The honest nugget (driving patterns peeku detects but never actuates) belongs as **typed
  peeku-native verbs**, not a value-string union.
- **Taskbar/tray/shell + pin/unpin** — already SKIP in the parity plan; peekaboo's dock/menu-extra
  analogs are themselves on peeku's macOS-only SKIP list → net parity advance: none. If demand appears,
  fold only a `FindWindow("Shell_TrayWnd")` → `Target.ShellTray` resolver into P1b window work.
- **Snapshot condition-reuse + generic file-diff micro-mechanics** — the real cold-walk lever is the
  unused `CacheRequest`. Filter-with-descendant-retention pruning: defer until `see` payload size is a
  *measured* bottleneck; skip Sidekick's `string.Create` control-char sanitization (System.Text.Json
  already escapes).

## Sequencing — interleaved with P1b/P2

1. **Now (ahead of / parallel with P1b):** A2 hints + A3 pattern-state (cheap, additive) → A1 tree-diff
   `diff` verb → A4 hit-test (land *just before* P1b coordinate-click). Key-Tips Slice 1 rides any
   property-read touch.
2. **Into P1b (fold):** build the 3 shared seams; foreground count-evidence + input-health into doctor.
3. **Early P2:** B1 rich wait → overlay scanner.
4. **Observe-breadth slice (one PR, after P1b):** multi-channel + WinEvent + `[Flags]` + TextChanged →
   then B4 action-diff into `Evidence`.
5. **Later:** P3 dialog/file-dialog + keytips verb; validation wave; drag-verify (in P2 drag).

Tier A (~10–14 days incl. verb fan-out) front-loads the highest parity-per-effort wins entirely
outside the P1b critical path, with A4 actively *unblocking* it.

## Confidence & open questions

- **High confidence:** FlaUI-reachability of every Tier A item (`FromPoint`/`Parent`, pattern-state
  accessors, `RegisterNotificationEvent`, settle-drain at `UiaLiveWait.cs:181`) — verified by binary
  reflection of `FlaUI.Core.Signed 5.0`. Tier C correctly empty; folding discipline clean.
- **A1 input-mode correction verified:** `SnapshotId` is a result label with no server-side store →
  two-snapshotId diff needs new caching infra; ship two-Targets first. Open: is a snapshot-cache ever
  worth building, or do agents only want action-scoped before/after diffs? Lean latter (`Evidence` serves it).
- **CacheRequest perf delta unmeasured** — profile cold-walk with/without a cache scope on a dense app
  (Office/browser) before investing. Peeku-native, outside this report's scope.
- **peekaboo dialog/menu specifics inferred, not read** — confirm peekaboo's actual dialog verb
  semantics + menu virtualization before building P3 dialog / P1b menu, so the adopted *shape* matches.
- **Clean-room is a process risk, not technical** — implementers must not open Sidekick source for
  "reference." Name the public doc per heuristic; keep the constraint in acceptance criteria.
