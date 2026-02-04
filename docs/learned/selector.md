# Selector DSL (v0)

Date: 2026-02-04

## Current supported subset

File: `src/peeku.Core/Uia/UiaSelectors.cs`

Expr: `/`-separated segments.

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

- Deterministic order: UIA tree order; left-to-right segments; first segment checks node self + children; later segments check children only.
- Output: list of `ElementRef` (capped by `limit`).

Example:

- `window[name~="Notepad"]/edit`

