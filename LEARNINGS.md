# LEARNINGS

- 2026-02-04: Beads initialized in repo; issue graph from `prd.md` tracked in `.beads/issues.jsonl`.
- 2026-02-04: Split PRD work into atomic bead tasks (Core/ToolDescriptors/UIA/tools/CLI/MCP) for multi-agent execution.
- 2026-02-04: UIA actions: shared selection resolver + method router; split `UiaClient` into partial files to keep <~500 LOC.
- 2026-02-04: WGC capture: single-frame PNG path works; D3D11CreateDevice + `CreateDirect3D11DeviceFromDXGIDevice` + `Direct3D11CaptureFramePool.CreateFreeThreaded`.
- 2026-02-04: Wait: selector polling implemented via `UiaWait` (snapshot + `UiaSelectors`), unit tests use fake snapshots.
- 2026-02-05: `--live` selector mode: live UIA eval via `UiaSelectorEngine` + debounced event-driven `wait` (`UiaLiveWait`); CLI/docs updated.
- 2026-02-05: Daemon JSON-RPC base + named-pipe server added with unit tests for JSONL parsing/dispatch.
- 2026-02-05: CLI daemon auto-connect uses LocalAppData marker + JSON-RPC client calling `peeku.batch`.
- 2026-02-05: Daemon sessions now use per-connection actor threads with serialized dispatch and tests.
- 2026-02-05: Daemon handle cache emits `h:` refs; daemon actions resolve via handle cache fast-path.
