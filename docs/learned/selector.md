# Selector DSL (v1)

Date: 2026-02-04

## Read when

- Using `peeku find` / `peeku element get` / `peeku wait`
- Writing MCP `selector` arguments

## Supported (current implementation)

File: `src/peeku.Core/Uia/UiaSelectors.cs`

Expr: `/`-separated segments. No leading `/`. Whitespace trimmed.

Matches are computed from a **UIA snapshot** (not live UIA). For a live lookup, use selector → snapshot → resolve `refId` → actions.

## Grammar (BNF-ish)

```
selector        := segment ( "/" segment )*
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

Segment:

- `controlType` (optional; default `*`)
- zero+ filters in brackets: `[key=value]` or `[key~="value"]`

Keys:

- `name` / `title` (same)
- `controlType`
- `automationId`
- `class` / `className`

Ops:

- `=` case-insensitive equals
- `~=` case-insensitive contains

Matching:

- Deterministic order: UIA tree order; left-to-right segments.
- Segment 0 checks `root` + its children.
- Segment N>0 checks children only.
- Output: list of `ElementRef` (capped by `limit`).

Example:

- `window[name~="Notepad"]/edit`

## More examples

- Focused window root:
  - `window`

- Windows Terminal tab strip (control type is often `Tab`):
  - `window[name~="PowerShell"]/tab`

- Tab item by name (Windows Terminal snapshot often: `tab/list/tabitem`):
  - `window[name~="PowerShell"]/tab/list/tabitem[name="PowerShell"]`

- AutomationId match:
  - `window/*[automationId="CloseButton"]`

## Failure modes (common)

- Empty selector: `Selector expr required.`
- Unclosed bracket: `Unclosed filter bracket in selector segment: '...'.`
- Bad filter syntax: `Invalid filter: '...'. Expected key=value or key~="value".`
