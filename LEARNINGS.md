# LEARNINGS

- 2026-02-04: Beads initialized in repo; issue graph from `prd.md` tracked in `.beads/issues.jsonl`.
- 2026-02-04: Split PRD work into atomic bead tasks (Core/ToolDescriptors/UIA/tools/CLI/MCP) for multi-agent execution.
- 2026-02-04: UIA actions: shared selection resolver + method router; split `UiaClient` into partial files to keep <~500 LOC.
- 2026-02-04: WGC capture: single-frame PNG path works; D3D11CreateDevice + `CreateDirect3D11DeviceFromDXGIDevice` + `Direct3D11CaptureFramePool.CreateFreeThreaded`.
- 2026-02-04: Wait: selector polling implemented via `UiaWait` (snapshot + `UiaSelectors`), unit tests use fake snapshots.
