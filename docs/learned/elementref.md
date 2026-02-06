# ElementRef stability (v1)

## Read when

- Using `--ref` across commands (snapshot → click/invoke/etc)
- Designing agent flows that need durable selectors

## Terms

- `snapshotId`: id for a single `uia snapshot` result; for debugging / correlation only.
- `refId`: best-effort stable identifier for a UIA element. Format: `uia:<pid>:<hash>`.
- `h:<id>`: daemon handle ref. Fast-path lookup in daemon session cache (TTL + LRU); intended for short-lived follow-up actions.

Note:
- Live selector mode (`--live`) does not produce a `snapshotId`.

## How `refId` is derived

File: `src/peeku.Core/Uia/UiaRefId.cs`

Preferred:
- `processId` + UIA `RuntimeId[]` → SHA256 → first 16 hex chars.

Fallback (when RuntimeId missing):
- Hash of: `processId | controlType | automationId | className | name | boundingRect`.

## Stability rules (practical)

- Stable-ish while the app process is alive and the UI element still exists.
- Not stable across app restarts (processId changes).
- Can change when the UI subtree is rebuilt (virtualization, tab reparenting, navigation).
- Some elements have no `nativeWindowHandle` (HWND) even if visible; that’s normal.
- `h:<id>` handles expire (TTL) and can be evicted (LRU); never treat them as durable identifiers.

## Recommended usage

- Prefer `--selector` for anything long-lived / replayable.
- Use `--ref` only within a short, single flow:
  - `uia snapshot` → pick element refId → `click/invoke/set-value/type/scroll`
- For daemon-backed fast loops, `h:<id>` is preferred for immediate next action; fall back to selector when handle misses.

## What to do when `refId` stops resolving

- Re-snapshot (`uia snapshot`) and re-select (same selector).
- If selector no longer matches:
  - increase snapshot depth
  - loosen selector filters (`~=` instead of `=`)
  - target by `--hwnd` or query to avoid focus drift
