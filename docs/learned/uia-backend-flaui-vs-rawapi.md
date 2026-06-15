# UIA backend: FlaUI.UIA3 vs raw IUIAutomation (Sidekick-style)

> Decision doc. Should peeku drop FlaUI.UIA3 and call the Windows UI Automation
> COM API directly, the way `references/Sidekick` (internal "Hyperloop") does?
> Verdict: **not now.** Backend choice is orthogonal to the peekaboo CLI
> ergonomics we actually want, and migrating would *regress* our stable-refId
> asset. Details below.

## TL;DR

- **Don't migrate now.** FlaUI is not the bottleneck and raw COM is not on the
  peekaboo critical path. The swap is an XL rewrite (~30–45 person-days + soak,
  ~21 files, all inside `src/peeku.Core/Uia/`) that buys deployment/perf — not
  agent ergonomics.
- **The only thing raw COM uniquely buys is NativeAOT single-file deploy.**
  That is Sidekick's actual reason: `System.Windows.Automation`'s managed
  reverse-P/Invoke event callbacks throw `MarshalDirectiveException` under
  NativeAOT, so they hand-roll vtable dispatch. peeku ships R2R self-contained
  today (`Directory.Build.props`, `PublishTrimmed=false`, no NativeAOT). No AOT
  requirement → the rationale collapses.
- **Migrating would regress our strongest asset.** peeku has working
  RuntimeId-based stable refIds (`UiaRefId.cs`). Sidekick — *with* full raw COM —
  never implemented RuntimeId (`UiaTreeBuilder.cs`, `RuntimeId = null` TODO) and
  falls back to a fragile `Control|Id|Name|parentKey` diff key. Raw access is
  necessary-but-not-sufficient; someone still has to build it.
- **Most "raw unlocks" are FlaUI underuse, reachable without leaving FlaUI:**
  CacheRequest bulk fetch, `FindFirst`/`FindAll` + `ConditionBase`,
  `ItemContainer`/`VirtualizedItem.Realize`, structure/property event streams.
  We have **zero** CacheRequest usage today — a we-problem, not a FlaUI ceiling.
- **Sidekick is reference-only, not a copy-source.** It is Microsoft-internal
  proprietary code with no OSS license (`es-metadata.yml` → `org: microsoft`,
  `isProduction: false`; only a vendored camera component carries a license).
  Any raw-COM client must be clean-room re-derived from the public Windows SDK
  (`UIAutomationClient.h`), never pasted from `Hyperloop.Automation`.

## How each works today

**peeku / FlaUI.UIA3 5.0** — `UiaClient` partial class wraps `FlaUI.Core.Signed`
+ `FlaUI.UIA3.Signed`. In-proc path news up `new UIA3Automation()` per call
(`UiaClient.cs:60`, cold COM); the daemon holds one warm `UIA3Automation` on a
single actor thread (COM apartment affinity). Tree walk is `FindAllChildren()`
bounded by Depth/MaxNodes. Pattern dispatch cascades Invoke→Toggle→SelectionItem
(`UiaClient.ActionHelpers.cs:86-133`). Identity = content-hash fingerprint
`uia:{pid}:{sha256(pid+RuntimeId)}` (`UiaRefId.cs:15-17`); refId→element resolve
is an O(tree) DFS recomputing the hash per node.

**Sidekick / raw IUIAutomation** — one `internal static unsafe partial class
UiaCom` (~1830 lines) doing vtable function-pointer dispatch:
`VT(obj) => *(IntPtr**)obj`, then `(delegate* unmanaged[Stdcall]<...>)VT(el)[SLOT]`.
Bootstrap = `CoCreateInstance(CLSID_CUIAutomation)` cached in a static singleton.
Elements/patterns/walkers are bare `IntPtr` — no managed type. Hand-rolled
BSTR/VARIANT marshalling, manual `Release(ref IntPtr)`, COM-callable event sinks
built in unmanaged memory via `[UnmanagedCallersOnly]`. Driver is NativeAOT.

## Architecture comparison

| Dimension | peeku / FlaUI | Sidekick / raw COM |
|---|---|---|
| Dependency footprint | 2 NuGet pkgs (FlaUI.Core/UIA3 Signed 5.0), trim-unsafe | Zero UIA deps; `ole32`/`oleaut32` P/Invoke only |
| Coupling | FlaUI referenced by 21 source files; cache is `HandleIdCache<AutomationElement>`; `ActionSelectionResolver` takes FlaUI types but is `internal` (intra-assembly, not a public leak) | `UiaCom` is the only COM layer; everything above is `IntPtr`+DTOs. `internal static`, coupled to `Hyperloop.Contracts` — no packageable reuse |
| AOT / trim | NativeAOT deferred; ships R2R self-contained | NativeAOT single-file — the entire point |
| COM threading | RCWs auto-managed; daemon = 1 warm instance + 1 actor thread | Static singleton + cached walker; STA/`CoInitializeEx` implicit (latent bug) |
| Testability | FlaUI-free core (`UiaSelectorEngine`, `UiaWait`, Contracts) unit-tests without a desktop | Vtable layer is integration-only; brittle hand-maintained slot tables |
| Maintenance | Pattern wrappers, condition factories, RuntimeId, events, GC'd COM lifetime all free | Manual AddRef/Release (miss=leak, extra=use-after-free), BSTR/VARIANT by hand, slot ints from the SDK header |

**Net:** peeku trades a heavy trim-unsafe dependency for *not* hand-writing
~1830 lines of error-prone COM marshalling.

## Capability gap

**Genuinely raw-only (but latent / no demand in our command surface):**

- Custom/unregistered provider patterns via `GetCurrentPattern` by IID. Neither tool exercises custom patterns.

> **Correction (2026-06-15, verified by binary reflection of `FlaUI.Core.Signed 5.0`):** two items
> previously listed here as "raw-only" are **false** — FlaUI 5.0 reaches both:
> - **IUIAutomation5 NotificationEvents** — `AutomationElement.RegisterNotificationEvent` + the
>   `UIA3NotificationEventHandler` ship in FlaUI 5.0. Not raw-only.
> - **Structure-watching (incl. before/after silent-mutation diffs)** — `RegisterStructureChangedEvent`
>   ships and peeku **already uses it** (`UiaLiveWait.cs:181`, settle-drain). The silent-mutation
>   before/after diff is an algorithm on top, not a raw-COM capability.
>
> See `docs/learned/sidekick-cleanroom-adoptions.md` for the full FlaUI-reachability matrix.

**Reachable inside FlaUI today (just unused):**

- **CacheRequest / bulk prefetch** — biggest perf lever; zero usage today. FlaUI
  5.0's model is an *ambient activation scope*, NOT a cache-aware Find overload:
  `cr.Properties.Add(...); cr.AutomationElementMode = ...; using (cr.Activate()) { element.FindAllChildren(); read props; }`.
  Under the scope, FindAll + property/pattern reads return cached values,
  collapsing 5–13 per-element COM round-trips into one. Win size depends on
  tuning the property/pattern set and `AutomationElementMode` (Full vs None).
- `FindFirst`/`FindAll` + `ConditionBase` for cheap targeted lookup. Speeds the
  *selector* path. (refId resolution still needs the per-node hash re-walk —
  inherent to the content-hash identity model, not a FlaUI limit.)
- Structure/Property event streams — we already *register*
  StructureChanged+PropertyChanged+FocusChanged (`UiaLiveWait.cs:181-189`); we
  only *surface* FocusChanged. Plumbing, not a backend gap.
- `ItemContainer`/`VirtualizedItem.Realize` for virtualized lists — FlaUI 5.0
  wraps both. Real hole, FlaUI-fixable.

**What FlaUI gives free that raw forces you to rebuild** (Sidekick rebuilt all of
it): pattern wrappers, condition factories, property coercion, GC'd COM lifetime,
managed event registration, vtable slot tables, **and RuntimeId** — which
Sidekick punted. You'd rebuild all of this to land roughly where you already are.

## Migration effort

Already raw Win32, **zero migration:** input injection (`HotkeyInputInjector.cs`,
`KeyPress.cs` — full `SendInput`/`INPUT`/`KEYBDINPUT`), window mgmt
(`Win32Windows.cs`, `WindowFocus.cs` — `user32` `EnumWindows`/foreground-unlock
dance), screen capture (WGC+Vortice), `ToolRegistry`, `ActionMethodRouter`,
`Contracts.cs`. The HWND→root boundary stays clean — only `automation.FromHandle`
becomes raw `ElementFromHandle`.

Estimates assume **clean-room re-derivation from `UIAutomationClient.h`**, not
pasting from Sidekick (proprietary). That is why pattern/marshalling/vtable work
carries no "copy savings."

| Subsystem | Effort | Person-days | Risk | Note |
|---|---|---|---|---|
| `IUiaBackend` seam + COM bootstrap + SafeHandle RAII | L | 6–8 | med | Re-derive `VT()`/bootstrap/marshalling. Make `CoInitializeEx` explicit (don't inherit Sidekick's implicit STA bug). |
| Selector engine accessors | S–M | 2–3 | low | `UiaSelectorEngine<TNode>`, `UiaSelectors`, `UiaWait` already FlaUI-free — only `UiaLiveSelectors` leaf accessors change. |
| Snapshot / element_get / tree walk | L | 5–7 | med | Re-derive property reads + `IsXxxPatternAvailable` fast-path. Must add CacheRequest or snapshot regresses vs FlaUI. |
| refId / handle cache | M | 3–4 | **high** | `HandleIdCache<T>` is generic — survives. Must implement `GetRuntimeId` (SAFEARRAY) from scratch — Sidekick left it a TODO. Cache must AddRef-on-store/Release-on-evict — a 5-min-TTL cache of raw `IntPtr`s is a use-after-free/leak class FlaUI's GC eliminates for free today. |
| Action / pattern dispatch | M | 3–4 | med | Standard-pattern dispatch mechanically similar to Sidekick's `PatternHandlers.cs` but must be re-derived. Retype `ActionSelectionResolver`. |
| Wait (snapshot-poll) | S–M | 2–3 | med | `UiaWait` unchanged; ship polling-only first. |
| Event sinks (observe + live-wait) | L | 5–7 | **high** | Hardest. Hand-built CCWs in unmanaged memory; apartment/lifetime/swallowed-exception failure modes. You'd have to *beat* Sidekick (its watcher uses process-global static + a lossy 256-slot ring buffer). Defer past v1; keep polling fallback. |
| **Total** | **XL** | **~30–45 + 5–8 soak** | | One experienced Windows-interop dev. Optimistic given the clean-room constraint. |

**Sequencing behind `IUiaBackend`:** (1) seam + raw skeleton, FlaUI stays as
fallback; (2) read path + `GetRuntimeId` + CacheRequest; (3) identity/cache — gate
switchover on a parity test diffing raw vs FlaUI refIds; (4) action path; (5)
events last/deferrable. **Standing cost:** running both backends behind a flag
across in-proc *and* daemon paths doubles the parity-test surface for the whole
migration window. Remove FlaUI only after soak.

## Does this advance peekaboo ergonomics?

**No — separate backend from contract.** peekaboo's ergonomics live in the
contract/presentation layer, which in peeku is `peeku.Cli` + `Contracts.cs` —
all FlaUI-free. Swapping `UiaClient`'s internals changes zero CLI lines as long
as `IPeekuClient` holds.

Pure CLI/contract work (no raw API needed) — **this is where the parity wins are:**

| peekaboo-parity win | Effort | Needs backend swap? |
|---|---|---|
| Short role-prefixed element IDs (B1/T2) over `see` | S, 2–3d | No — role→prefix table over FlaUI `ControlType` + position sort. Highest payoff/effort. |
| Multi-strategy targeting on one flag (id \| fuzzy query \| coords) | M, 3–5d | No — unify `CliSelection`; precedence is inconsistent today (type=text, click=selector). |
| Snapshot-most-recent auto-resolution | M, 3–5d | No — daemon already has the warm cache; wiring. |
| Fix Timeout→Canceled (exit 8 vs 4) + add SNAPSHOT_STALE/ELEMENT_NOT_FOUND | S, 1–2d | No — documented bug (`ExitCodes.cs:45-49`); contract only. |
| Background coordinate click/type path | M, 4–6d | Mostly Win32 — daemon returns "Input click not supported yet"; SendInput infra exists. Caveat: a coord click needs element→point/hit-test, which today comes from UIA `BoundingRectangle`, so a small real dependency on the UIA layer remains. |

**Needs raw API:** essentially none of the *ergonomic* wins. Raw buys only perf
(`see` cheaper — but FlaUI CacheRequest captures most of it) and NativeAOT
deploy. peekaboo agents explicitly **don't** want raw element handles — they want
short stable IDs scoped to a snapshot, a uniform envelope, fuzzy/coord fallbacks,
and background input. peeku already has the uniform `ok/meta/error` envelope and
`--app`/`--pid` aliases — a genuine lead over peekaboo (itself inconsistent: flat
`SeeResult` vs `UnifiedToolOutput`).

## Recommendation

**Ergonomics first. Backend swap only if NativeAOT single-file deploy becomes a
hard requirement.**

1. Ship the FlaUI-independent CLI/contract parity wins now — short role-prefixed
   IDs, unified multi-strategy targeting, snapshot-most-recent, Timeout→Canceled
   fix + stale/not-found codes, background Win32 coordinate-click. None touch FlaUI.
2. If `see` is too slow after that, add FlaUI 5.0 CacheRequest (currently unused)
   via the `Activate()` scope before considering raw COM. Tune the property/pattern
   set + `AutomationElementMode`. Optionally `FindFirst`/`FindAll`+`ConditionBase`.
3. Defer the raw-COM rewrite to its own track, justified ONLY by
   NativeAOT/single-file/trim. Revisit conditions: (a) a concrete deployment
   requirement FlaUI's trim-unsafe footprint blocks; (b) you've tightened the
   `ActionSelectionResolver`/`HandleIdCache<AutomationElement>` coupling behind an
   internal element abstraction; (c) you've budgeted to re-implement RuntimeId from
   scratch; (d) you've accepted clean-room re-derivation from the public SDK.
4. If a genuinely raw-only capability is ever needed (NotificationEvents,
   silent-mutation diffing, custom patterns), build a narrow raw-COM **sidecar**
   behind the existing FlaUI-agnostic `UiaSelectorEngine`/contract boundary — the
   same targeted-escape-hatch role Sidekick's Win32 input layer plays. Don't swap
   the whole backend for latent capabilities.

## Confidence & open questions

Verified against live code (high confidence): R2R + self-contained +
`PublishTrimmed=false`, no NativeAOT; working RuntimeId stable refIds; zero
CacheRequest in `src`; FlaUI cache API is the `Activate()` ambient scope (no
cache-aware Find overload); `ActionSelectionResolver` is `internal`
(intra-assembly coupling, not a public leak); Timeout→Canceled exit-code bug;
Sidekick `RuntimeId = null` TODO; input + window layers already raw Win32.

Still needs verification:
- **Sidekick reuse/IP policy.** No OSS license; UIA code is `internal` +
  `Hyperloop.Contracts`-coupled → no packageable reuse, only re-derivation.
  Confirm team IP policy permits even clean-room re-derivation that references
  the proprietary mechanism. Hard prerequisite before any raw-COM track.
- **Daemon COM threading under a raw backend.** Single-actor model covers FlaUI
  apartment affinity; a raw backend must make `CoInitializeEx` explicit and prove
  cross-thread element-pointer + event-sink-callback correctness. Needs a spike.
- **CacheRequest perf delta unquantified.** Measure a tuned `Activate()` scope
  before deciding raw COM is needed for `see` latency.
- **Background coordinate-click decoupling.** Whether element→point/hit-test can
  be sourced without UIA `BoundingRectangle` (e.g. pure Win32 hit-testing) is
  unconfirmed.

---
_Source: 13-agent comparison workflow over `peeku`, `references/Sidekick`,
`references/peekaboo` (2026-06-15). Adversarially reviewed; all load-bearing
facts re-verified against live code._
