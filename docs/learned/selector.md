# Selector DSL (v1)

Date: 2026-02-04

## Read when

- Using `peeku find` / `peeku element get` / `peeku wait`
- Writing MCP `selector` arguments

## Supported (current implementation)

File: `src/peeku.Core/Uia/UiaSelectorEngine.cs`

Expr: `/`-separated segments. No leading `/`. Whitespace trimmed.

Default matching is computed from a **UIA snapshot**. For live UIA evaluation, use `--live` (supported on `find`, `element get`, action `--selector`, and `wait`).

## Grammar (BNF-ish)

```
selector        := segment ( ( "/" | "//" ) segment )*
segment         := [ controlType ] filter*
controlType     := "*" | IDENT
filter          := "[" key op value "]"
key             := IDENT
op              := "=" | "~="
value           := STRING | IDENT
STRING          := "..." | '...'
IDENT           := 1+ chars excluding '/', '[', ']', whitespace
```

Notes:
- `controlType` omitted ⇒ treated as `*` (any).
- Multiple filters in a segment are ANDed.
- `=` is case-insensitive equals.
- `~=` is case-insensitive substring contains.
- Unquoted values allowed (no spaces). Quoted values may include spaces.
- `/` inside a quoted filter value (e.g. `[name="a/b"]`) is literal — bracket depth is tracked.

## Axes

### Child axis `/`

Default. Each segment matches direct children of the nodes matched by the previous segment.

- `window/pane/edit` — Window → direct-child Pane → direct-child Edit.

### Descendant axis `//`

The segment following `//` is matched anywhere in the subtree of each node in the current frontier (bounded DFS, pre-order, up to 10 000 visited nodes).

- Leading `//foo` — find `foo` anywhere under root (direct children and all descendants).
- `a//b` — find `a` by child axis, then find `b` anywhere under each matched `a`.

**Worked example:** find a Save button anywhere in a window:

```
//button[name~="Save"]
```

MCP ripple: none. The selector is a single `expr` string; `//` is parsed inside the engine — callers (`UiaSelectors`, `UiaLiveSelectors`) are unchanged.

### Segment

- `controlType` (optional; default `*`)
- zero+ filters in brackets: `[key=value]` or `[key~="value"]`

### Keys

- `name` / `title` (same)
- `controlType`
- `automationId`
- `class` / `className`

### Ops

- `=` case-insensitive equals
- `~=` case-insensitive contains

## Matching

- Deterministic order: UIA tree order; left-to-right segments.
- Child axis: segment 0 checks `root` + its children; segment N>0 checks direct children only.
- Descendant axis: DFS over children of each frontier node; root itself is not tested.
- Output: list of `ElementRef` (capped by `limit`).

## Examples

- `window[name~="Notepad"]/edit`
- `//button[name~="Save"]` — deep one-shot: Save button anywhere under root
- `window[name~="PowerShell"]//tabitem[name="PowerShell"]` — tab item under a named window

## More examples

- Focused window root:
  - `window`

- Windows Terminal tab strip (control type is often `Tab`):
  - `window[name~="PowerShell"]/tab`

- Tab item by name (Windows Terminal snapshot often: `tab/list/tabitem`):
  - `window[name~="PowerShell"]/tab/list/tabitem[name="PowerShell"]`
  - or with descendant axis: `window[name~="PowerShell"]//tabitem[name="PowerShell"]`

- AutomationId match:
  - `window/*[automationId="CloseButton"]`

## Failure modes (common)

- Empty selector: `Selector expr required.`
- Trailing `//` with no following segment: `Descendant axis requires a following segment.`
- Triple slash `///`: `Invalid selector: '///' is not a valid axis.`
- Unclosed bracket: `Unclosed filter bracket in selector segment: '...'.`
- Bad filter syntax: `Invalid filter: '...'. Expected key=value or key~="value".`
