Below is a **concrete spec** that you can hand to an engineer and implement with low ambiguity:

* A **public C# API** (library-first)
* A **CLI surface** (every feature is testable without writing code)
* An **MCP tool set** that mirrors the CLI **1:1**, with **inputSchema + outputSchema** (per MCP spec)

I’ll also call out the **maintained libraries** that make each part easier.

---

## 0) Design constraints and key dependencies

### Screen capture (window / monitor)

* Use **Windows.Graphics.Capture** (WGC). It supports capturing windows/displays; for service-style capture you’ll likely want `Direct3D11CaptureFramePool.CreateFreeThreaded` so frames arrive on a worker thread (no dispatcher dependency). ([Microsoft Learn][1])
* Reference implementation: **Win32CaptureSample** shows WGC from a Win32 app; it notes **min Windows 10 build 17134** to run, and Windows 11 SDK to compile. ([GitHub][2])
* Interop/projection: **C#/WinRT (CsWinRT)** makes WinRT APIs accessible in C# naturally. ([GitHub][3])

### UI automation (tree + patterns + events)

* Use **FlaUI**: maintained wrapper over Microsoft UI Automation supporting Win32/WinForms/WPF/Store apps etc. ([GitHub][4])

### CLI plumbing

* **System.CommandLine** (official repo) for robust parsing/help/completions. ([GitHub][5])
  (Alternative: Spectre.Console.Cli is also actively maintained; keep in mind it’s more opinionated. ([GitHub][6]))

### Logging

* **Serilog** for structured JSON logs and diagnostics. ([GitHub][7])

### MCP server

* MCP **Tools** use `name/title/description/inputSchema` and optionally `outputSchema`; results can include `structuredContent` and content blocks (text/image/resource links). ([Model Context Protocol][8])
* Official **MCP C# SDK** supports exposing methods as tools and auto-generating input schema from method parameters (via `McpServerToolAttribute`). ([Model Context Protocol][9])

---

## 1) Public API proposal (WinPeek.Core)

### Packaging and namespaces

* `WinPeek.Core` (NuGet) — public library
* `WinPeek.Cli` (exe)
* `WinPeek.Mcp` (exe hosting MCP server)

### Core principles

1. **One canonical “tool API”** in code: CLI and MCP are both adapters over the same `IWinPeekClient`.
2. All operations are **async**, accept **CancellationToken**, and return **structured results** with trace IDs.
3. Prefer **UIA patterns** for actions; use input injection as a fallback (later phase).

---

### Public API (C#) — canonical interface

```csharp
namespace WinPeek;

public interface IWinPeekClient
{
    // Health / diagnostics
    Task<DoctorResult> DoctorAsync(DoctorRequest req, CancellationToken ct = default);

    // Window discovery / selection
    Task<WindowListResult> WindowsListAsync(WindowsListRequest req, CancellationToken ct = default);
    Task<FocusedWindowResult> WindowsFocusedAsync(CancellationToken ct = default);

    // Capture
    Task<CaptureImageResult> CaptureImageAsync(CaptureImageRequest req, CancellationToken ct = default);

    // UIA snapshots / querying
    Task<UiaSnapshotResult> UiaSnapshotAsync(UiaSnapshotRequest req, CancellationToken ct = default);
    Task<SeeResult> SeeAsync(SeeRequest req, CancellationToken ct = default); // capture + snapshot
    Task<FindResult> FindAsync(FindRequest req, CancellationToken ct = default);
    Task<ElementGetResult> ElementGetAsync(ElementGetRequest req, CancellationToken ct = default);

    // Actions
    Task<ActionResult> ClickAsync(ClickRequest req, CancellationToken ct = default);
    Task<ActionResult> InvokeAsync(InvokeRequest req, CancellationToken ct = default);
    Task<ActionResult> SetValueAsync(SetValueRequest req, CancellationToken ct = default);
    Task<ActionResult> TypeAsync(TypeRequest req, CancellationToken ct = default);
    Task<ActionResult> ScrollAsync(ScrollRequest req, CancellationToken ct = default);
    Task<ActionResult> HotkeyAsync(HotkeyRequest req, CancellationToken ct = default);

    // Observation / sync
    IAsyncEnumerable<ObservationEvent> ObserveAsync(ObserveRequest req, CancellationToken ct = default);
    Task<WaitResult> WaitAsync(WaitRequest req, CancellationToken ct = default);

    // Batch
    Task<BatchResult> BatchAsync(BatchRequest req, CancellationToken ct = default);
}
```

This interface is intentionally **tool-shaped**: every method maps to one CLI command and one MCP tool.

---

### Shared request/response types (stable contract)

#### Targets (window/screen/focused/desktop)

```csharp
public abstract record Target
{
    public sealed record Desktop() : Target;
    public sealed record FocusedWindow() : Target;
    public sealed record Screen(int ScreenIndex) : Target;
    public sealed record WindowByHwnd(string HwndHex) : Target; // "0x0003059A"
    public sealed record WindowByQuery(WindowQuery Query) : Target;
}

public record WindowQuery(
    string? TitleContains = null,
    string? ProcessName = null,
    int? ProcessId = null);
```

#### Selector (your “path-based locator”)

```csharp
public record Selector(
    string Expr,               // e.g. window[title~="Notepad"]/edit
    bool PreferCachedSnapshot = true);
```

#### Element reference (portable across CLI/MCP)

```csharp
public record ElementRef(
    string RefId,              // stable-ish id (processId+runtimeId hash)
    string? SnapshotId = null  // if derived from snapshot (optional)
);
```

#### Common result envelope (for logs + debugging)

```csharp
public record ResultMeta(
    string TraceId,
    DateTimeOffset Timestamp,
    int DurationMs,
    string? Warning = null);

public record WinPeekError(
    string Code,               // e.g. "ElementNotFound"
    string Message,
    object? Details = null);
```

Every response includes:

* `meta`
* `ok`
* optional `error`
* tool-specific payload

---

### Screen capture specifics (implementation notes)

* Use WGC; prefer `CreateFreeThreaded` to avoid needing a UI thread/dispatcher for capture frame arrival. ([Microsoft Learn][1])
* Expect OS constraints: Win32CaptureSample notes min Windows build 17134 to run. ([GitHub][2])
* Use CsWinRT for WinRT projections in C#. ([GitHub][3])

---

### UI automation specifics (implementation notes)

* Use FlaUI as your UIA abstraction layer and to reduce COM friction. ([GitHub][4])
* For Observation: implement event subscriptions via UIA and expose as `IAsyncEnumerable<ObservationEvent>`.

---

## 2) CLI command surface (WinPeek.Cli)

### Global behavior

**Executable:** `winpeek`

**Global options (apply to all commands)**

* `--format json|pretty` (default: `pretty` for humans; `json` for scripting)
* `--timeout 00:00:10`
* `--log-level trace|debug|info|warn|error`
* `--log-file <path>` (optional)
* `--trace-id <id>` (optional, else generated)
* `--profile <name>` (loads defaults from config)

> Implementation note: System.CommandLine provides robust CLI parsing support. ([GitHub][5])
> Logging: Serilog is a good default for structured logs. ([GitHub][7])

---

### Command index (mirrors API & MCP 1:1)

#### 1) Diagnostics

**`winpeek doctor`**

* Purpose: verify OS/build, capture availability, UIA availability, permissions
* Output: `DoctorResult`

Example:

```bash
winpeek doctor --format json
```

#### 2) Window discovery

**`winpeek windows list`**

* Options:

  * `--title-contains <text>`
  * `--process-name <name>`
  * `--limit <n>` (default 50)
* Output: list of windows with `hwnd`, `title`, `processId`, `processName`

**`winpeek windows focused`**

* Output: focused window info

#### 3) Capture

**`winpeek capture image`**

* Options:

  * `--target desktop|focused|screen:<n>|hwnd:<hex>|query:<json>`
  * `--out <file.png>` (optional; if omitted, writes to temp and returns path)
  * `--include-base64 true|false` (default false; for MCP parity)
* Output: path + metadata, optional base64

#### 4) UIA snapshot & “see”

**`winpeek uia snapshot`**

* Options:

  * `--target ...`
  * `--depth <n>` (default 6)
  * `--max-nodes <n>` (default 5000)
  * `--include-properties basic|all`
* Output: snapshotId + root node + flattened elements

**`winpeek see`** *(capture + UIA snapshot in one call)*

* Options: `--target`, `--depth`, `--max-nodes`, `--include-base64`, `--include-properties`
* Output: screenshot + snapshot + element list

#### 5) Find and inspect elements

**`winpeek find`**

* Args:

  * `--selector "<expr>"`
  * `--target ...` (optional; defaults to focused window)
  * `--limit <n>` (default 20)
* Output: list of matches with `elementRef`, `rect`, key attrs

**`winpeek element get`**

* Args: `--ref <refId>` or `--selector <expr>`
* Output: full detail for one element (patterns, attributes, computed properties)

#### 6) Actions

**`winpeek click`**

* Args: `--ref <refId>` OR `--selector <expr>` (+ optional `--target`)
* Options:

  * `--method auto|uia|input` (default `auto`)
* Output: action result

**`winpeek invoke`**

* Same selection args; uses UIA Invoke pattern when available

**`winpeek set-value`**

* Args: `--ref/--selector` + `--value "<text>"`

**`winpeek type`**

* Args: `--ref/--selector` + `--text "<text>"`
* Options:

  * `--append true|false` (default true)
  * `--delay-ms <n>` (optional, for flaky apps)

**`winpeek scroll`**

* Args: `--ref/--selector` + `--delta -120|120` OR `--lines <n>`
* Options: `--direction vertical|horizontal` (default vertical)

**`winpeek hotkey`**

* Args: `--keys "CTRL+SHIFT+S"`

#### 7) Observe and wait

**`winpeek observe`**

* Args:

  * `--target ...`
  * `--events structure|property|focus|all`
  * `--duration 00:00:10` (default 10s)
  * `--max-events <n>` (default 200)
* Output: JSONL or JSON array of events

**`winpeek wait`**

* Args:

  * `--selector "<expr>"`
  * `--target ...`
  * `--timeout 00:00:10`
* Output: found elementRef + optional final snapshot

#### 8) Batch

**`winpeek batch`**

* Args:

  * `--in ops.json` (array of operations)
  * `--stop-on-error true|false`
* Output: list of step results + timings

---

## 3) MCP tools (mirrors CLI 1:1)

### MCP schema rules we will follow

* Each tool must have `name`, optional `title`, `description`, and `inputSchema` (JSON Schema). ([Model Context Protocol][8])
* Tools **may** provide `outputSchema`; if provided, the server **must** return `structuredContent` that conforms to it, and should also include a text block containing serialized JSON for backwards compatibility. ([Model Context Protocol][8])
* Tool results can include multiple content items, including `"type": "image"` with base64 PNG data. ([Model Context Protocol][8])

### Tool naming convention

To avoid collisions across servers, prefix everything with `winpeek_`, and mirror the CLI “path” with underscores:

| CLI                       | MCP tool name             |
| ------------------------- | ------------------------- |
| `winpeek doctor`          | `winpeek_doctor`          |
| `winpeek windows list`    | `winpeek_windows_list`    |
| `winpeek windows focused` | `winpeek_windows_focused` |
| `winpeek capture image`   | `winpeek_capture_image`   |
| `winpeek uia snapshot`    | `winpeek_uia_snapshot`    |
| `winpeek see`             | `winpeek_see`             |
| `winpeek find`            | `winpeek_find`            |
| `winpeek element get`     | `winpeek_element_get`     |
| `winpeek click`           | `winpeek_click`           |
| `winpeek invoke`          | `winpeek_invoke`          |
| `winpeek set-value`       | `winpeek_set_value`       |
| `winpeek type`            | `winpeek_type`            |
| `winpeek scroll`          | `winpeek_scroll`          |
| `winpeek hotkey`          | `winpeek_hotkey`          |
| `winpeek observe`         | `winpeek_observe`         |
| `winpeek wait`            | `winpeek_wait`            |
| `winpeek batch`           | `winpeek_batch`           |

> Implementation note: the MCP C# SDK supports exposing methods as tools and generating `inputSchema` from parameters. ([Model Context Protocol][9])

---

## 4) MCP tool schemas (inputSchema + outputSchema)

Below are **concrete JSON Schema** definitions for each tool. (All are “type: object” with explicit required fields.)

> Tip: In v1, keep schemas **flat and explicit** (avoid fancy `$ref`) so clients validate reliably.

### Shared schema snippets (used repeatedly)

**Target**

```json
{
  "type": "object",
  "properties": {
    "kind": { "type": "string", "enum": ["desktop","focused_window","screen","window_hwnd","window_query"] },
    "screenIndex": { "type": "integer" },
    "hwndHex": { "type": "string", "description": "Window handle, e.g. 0x0003059A" },
    "query": {
      "type": "object",
      "properties": {
        "titleContains": { "type": "string" },
        "processName": { "type": "string" },
        "processId": { "type": "integer" }
      }
    }
  },
  "required": ["kind"]
}
```

**Selector**

```json
{
  "type": "object",
  "properties": {
    "expr": { "type": "string", "description": "Path selector expression" },
    "preferCachedSnapshot": { "type": "boolean", "default": true }
  },
  "required": ["expr"]
}
```

**ElementRef**

```json
{
  "type": "object",
  "properties": {
    "refId": { "type": "string" },
    "snapshotId": { "type": "string" }
  },
  "required": ["refId"]
}
```

**Rect**

```json
{
  "type": "object",
  "properties": {
    "x": { "type": "number" },
    "y": { "type": "number" },
    "width": { "type": "number" },
    "height": { "type": "number" }
  },
  "required": ["x","y","width","height"]
}
```

---

### Tool: `winpeek_doctor`

**inputSchema**

```json
{
  "type": "object",
  "properties": {
    "deep": { "type": "boolean", "default": false, "description": "Run slower checks (capture + UIA)" }
  }
}
```

**outputSchema**

```json
{
  "type": "object",
  "properties": {
    "ok": { "type": "boolean" },
    "traceId": { "type": "string" },
    "checks": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "name": { "type": "string" },
          "ok": { "type": "boolean" },
          "details": { "type": "string" }
        },
        "required": ["name","ok"]
      }
    }
  },
  "required": ["ok","traceId","checks"]
}
```

---

### Tool: `winpeek_windows_list`

**inputSchema**

```json
{
  "type": "object",
  "properties": {
    "titleContains": { "type": "string" },
    "processName": { "type": "string" },
    "limit": { "type": "integer", "default": 50 }
  }
}
```

**outputSchema**

```json
{
  "type": "object",
  "properties": {
    "ok": { "type": "boolean" },
    "traceId": { "type": "string" },
    "windows": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "hwndHex": { "type": "string" },
          "title": { "type": "string" },
          "processId": { "type": "integer" },
          "processName": { "type": "string" }
        },
        "required": ["hwndHex","processId"]
      }
    }
  },
  "required": ["ok","traceId","windows"]
}
```

---

### Tool: `winpeek_windows_focused`

**inputSchema**

```json
{ "type": "object", "properties": {} }
```

**outputSchema**

```json
{
  "type": "object",
  "properties": {
    "ok": { "type": "boolean" },
    "traceId": { "type": "string" },
    "window": {
      "type": "object",
      "properties": {
        "hwndHex": { "type": "string" },
        "title": { "type": "string" },
        "processId": { "type": "integer" },
        "processName": { "type": "string" }
      },
      "required": ["hwndHex","processId"]
    }
  },
  "required": ["ok","traceId","window"]
}
```

---

### Tool: `winpeek_capture_image`

This returns **either** a `resource_link` to a file and/or an MCP `"image"` content block (base64). MCP supports image blocks. ([Model Context Protocol][8])

**inputSchema**

```json
{
  "type": "object",
  "properties": {
    "target": { "description": "What to capture", "type": "object" },
    "outPath": { "type": "string", "description": "Optional output path on disk" },
    "includeBase64": { "type": "boolean", "default": false },
    "imageFormat": { "type": "string", "enum": ["png"], "default": "png" }
  },
  "required": ["target"]
}
```

**outputSchema**

```json
{
  "type": "object",
  "properties": {
    "ok": { "type": "boolean" },
    "traceId": { "type": "string" },
    "imagePath": { "type": "string" },
    "mimeType": { "type": "string", "default": "image/png" },
    "base64": { "type": "string", "description": "Present only if includeBase64=true" },
    "width": { "type": "integer" },
    "height": { "type": "integer" }
  },
  "required": ["ok","traceId","imagePath","mimeType","width","height"]
}
```

> Implementation reminder: capture is WGC-based; `CreateFreeThreaded` improves service-style capture. ([Microsoft Learn][1])
> Min OS build guidance: Win32CaptureSample lists 17134. ([GitHub][2])

---

### Tool: `winpeek_uia_snapshot`

**inputSchema**

```json
{
  "type": "object",
  "properties": {
    "target": { "type": "object" },
    "depth": { "type": "integer", "default": 6 },
    "maxNodes": { "type": "integer", "default": 5000 },
    "includeProperties": { "type": "string", "enum": ["basic","all"], "default": "basic" }
  },
  "required": ["target"]
}
```

**outputSchema**

```json
{
  "type": "object",
  "properties": {
    "ok": { "type": "boolean" },
    "traceId": { "type": "string" },
    "snapshotId": { "type": "string" },
    "root": { "type": "object" },
    "elements": { "type": "array", "items": { "type": "object" } }
  },
  "required": ["ok","traceId","snapshotId","root","elements"]
}
```

> Implementation: FlaUI provides a maintained wrapper over UIA across app types. ([GitHub][4])

---

### Tool: `winpeek_see` (capture + UIA snapshot)

**inputSchema**

```json
{
  "type": "object",
  "properties": {
    "target": { "type": "object" },
    "depth": { "type": "integer", "default": 6 },
    "maxNodes": { "type": "integer", "default": 5000 },
    "includeProperties": { "type": "string", "enum": ["basic","all"], "default": "basic" },
    "includeBase64": { "type": "boolean", "default": false }
  },
  "required": ["target"]
}
```

**outputSchema**

```json
{
  "type": "object",
  "properties": {
    "ok": { "type": "boolean" },
    "traceId": { "type": "string" },
    "image": {
      "type": "object",
      "properties": {
        "imagePath": { "type": "string" },
        "mimeType": { "type": "string" },
        "base64": { "type": "string" },
        "width": { "type": "integer" },
        "height": { "type": "integer" }
      },
      "required": ["imagePath","mimeType","width","height"]
    },
    "snapshotId": { "type": "string" },
    "elements": { "type": "array", "items": { "type": "object" } }
  },
  "required": ["ok","traceId","image","snapshotId","elements"]
}
```

---

### Tool: `winpeek_find`

**inputSchema**

```json
{
  "type": "object",
  "properties": {
    "selector": { "type": "object" },
    "target": { "type": "object", "description": "Optional; defaults to focused window" },
    "limit": { "type": "integer", "default": 20 }
  },
  "required": ["selector"]
}
```

**outputSchema**

```json
{
  "type": "object",
  "properties": {
    "ok": { "type": "boolean" },
    "traceId": { "type": "string" },
    "matches": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "element": { "type": "object" },
          "name": { "type": "string" },
          "automationId": { "type": "string" },
          "controlType": { "type": "string" },
          "rect": { "type": "object" },
          "score": { "type": "number" }
        },
        "required": ["element","rect","score"]
      }
    }
  },
  "required": ["ok","traceId","matches"]
}
```

---

### Tool: `winpeek_element_get`

**inputSchema**

```json
{
  "type": "object",
  "properties": {
    "elementRef": { "type": "object" },
    "selector": { "type": "object" },
    "target": { "type": "object" },
    "includeProperties": { "type": "string", "enum": ["basic","all"], "default": "all" }
  }
}
```

Rules:

* Require exactly one of (`elementRef`, `selector`).
* If `selector` is used and `target` omitted, default to focused window.

**outputSchema**

```json
{
  "type": "object",
  "properties": {
    "ok": { "type": "boolean" },
    "traceId": { "type": "string" },
    "element": { "type": "object" },
    "properties": { "type": "object" },
    "patterns": { "type": "array", "items": { "type": "string" } },
    "rect": { "type": "object" }
  },
  "required": ["ok","traceId","element","properties","patterns","rect"]
}
```

---

### Tool: `winpeek_click`

**inputSchema**

```json
{
  "type": "object",
  "properties": {
    "elementRef": { "type": "object" },
    "selector": { "type": "object" },
    "target": { "type": "object" },
    "method": { "type": "string", "enum": ["auto","uia","input"], "default": "auto" }
  }
}
```

**outputSchema**

```json
{
  "type": "object",
  "properties": {
    "ok": { "type": "boolean" },
    "traceId": { "type": "string" },
    "methodUsed": { "type": "string" },
    "clickedElement": { "type": "object" }
  },
  "required": ["ok","traceId","methodUsed"]
}
```

---

### Tool: `winpeek_invoke`

**inputSchema**

```json
{
  "type": "object",
  "properties": {
    "elementRef": { "type": "object" },
    "selector": { "type": "object" },
    "target": { "type": "object" }
  }
}
```

**outputSchema** = same as `winpeek_click` (but `methodUsed` likely `"uia.invoke"`)

---

### Tool: `winpeek_set_value`

**inputSchema**

```json
{
  "type": "object",
  "properties": {
    "elementRef": { "type": "object" },
    "selector": { "type": "object" },
    "target": { "type": "object" },
    "value": { "type": "string" }
  },
  "required": ["value"]
}
```

**outputSchema**

```json
{
  "type": "object",
  "properties": {
    "ok": { "type": "boolean" },
    "traceId": { "type": "string" },
    "setOnElement": { "type": "object" }
  },
  "required": ["ok","traceId"]
}
```

---

### Tool: `winpeek_type`

**inputSchema**

```json
{
  "type": "object",
  "properties": {
    "elementRef": { "type": "object" },
    "selector": { "type": "object" },
    "target": { "type": "object" },
    "text": { "type": "string" },
    "append": { "type": "boolean", "default": true },
    "delayMs": { "type": "integer", "default": 0 }
  },
  "required": ["text"]
}
```

**outputSchema**

```json
{
  "type": "object",
  "properties": {
    "ok": { "type": "boolean" },
    "traceId": { "type": "string" }
  },
  "required": ["ok","traceId"]
}
```

---

### Tool: `winpeek_scroll`

**inputSchema**

```json
{
  "type": "object",
  "properties": {
    "elementRef": { "type": "object" },
    "selector": { "type": "object" },
    "target": { "type": "object" },
    "direction": { "type": "string", "enum": ["vertical","horizontal"], "default": "vertical" },
    "delta": { "type": "integer", "description": "Wheel delta, e.g. 120/-120" }
  },
  "required": ["delta"]
}
```

**outputSchema** same minimal action output.

---

### Tool: `winpeek_hotkey`

**inputSchema**

```json
{
  "type": "object",
  "properties": {
    "keys": { "type": "string", "description": "e.g. CTRL+SHIFT+S" }
  },
  "required": ["keys"]
}
```

**outputSchema** same minimal action output.

---

### Tool: `winpeek_observe` (bounded long-poll)

Because MCP tools are request/response, v1 should be a **bounded observation** tool:

* it observes for `durationMs` or until `maxEvents` reached, then returns them.
* CLI can stream continuously by calling this repeatedly.

**inputSchema**

```json
{
  "type": "object",
  "properties": {
    "target": { "type": "object" },
    "events": {
      "type": "array",
      "items": { "type": "string", "enum": ["structure","property","focus"] },
      "default": ["structure","property","focus"]
    },
    "durationMs": { "type": "integer", "default": 10000 },
    "maxEvents": { "type": "integer", "default": 200 }
  },
  "required": ["target"]
}
```

**outputSchema**

```json
{
  "type": "object",
  "properties": {
    "ok": { "type": "boolean" },
    "traceId": { "type": "string" },
    "events": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "timestamp": { "type": "string" },
          "eventType": { "type": "string" },
          "element": { "type": "object" },
          "changedProperty": { "type": "string" },
          "oldValue": {},
          "newValue": {}
        },
        "required": ["timestamp","eventType"]
      }
    }
  },
  "required": ["ok","traceId","events"]
}
```

---

### Tool: `winpeek_wait`

**inputSchema**

```json
{
  "type": "object",
  "properties": {
    "selector": { "type": "object" },
    "target": { "type": "object" },
    "timeoutMs": { "type": "integer", "default": 10000 },
    "pollMs": { "type": "integer", "default": 200 },
    "returnSnapshot": { "type": "boolean", "default": false }
  },
  "required": ["selector","target"]
}
```

**outputSchema**

```json
{
  "type": "object",
  "properties": {
    "ok": { "type": "boolean" },
    "traceId": { "type": "string" },
    "found": { "type": "boolean" },
    "element": { "type": "object" },
    "snapshotId": { "type": "string" }
  },
  "required": ["ok","traceId","found"]
}
```

---

### Tool: `winpeek_batch`

**inputSchema**

```json
{
  "type": "object",
  "properties": {
    "stopOnError": { "type": "boolean", "default": true },
    "ops": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "tool": { "type": "string" },
          "args": { "type": "object" }
        },
        "required": ["tool","args"]
      }
    }
  },
  "required": ["ops"]
}
```

**outputSchema**

```json
{
  "type": "object",
  "properties": {
    "ok": { "type": "boolean" },
    "traceId": { "type": "string" },
    "results": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "tool": { "type": "string" },
          "ok": { "type": "boolean" },
          "durationMs": { "type": "integer" },
          "error": { "type": "object" },
          "data": { "type": "object" }
        },
        "required": ["tool","ok","durationMs"]
      }
    }
  },
  "required": ["ok","traceId","results"]
}
```

---

## 5) How MCP responses should be structured (so clients love you)

Per MCP Tools spec:

* Return structured results in `structuredContent` (when you provide an output schema).
* Also include a `"type": "text"` content item containing the JSON string for compatibility. ([Model Context Protocol][8])

Example pattern for most tools:

```json
{
  "content": [
    { "type": "text", "text": "{\"ok\":true,\"traceId\":\"...\", ...}" }
  ],
  "structuredContent": { "ok": true, "traceId": "...", ... },
  "isError": false
}
```

For capture tools, optionally include `"type": "image"` content blocks (base64 png). MCP supports image content items. ([Model Context Protocol][8])

---

## 6) “Single source of truth” implementation strategy (prevents drift)

To guarantee CLI ↔ MCP 1:1 mirroring, implement **ToolDescriptors** in `WinPeek.Core`:

```csharp
public record ToolDescriptor(
    string Name,           // winpeek_capture_image
    string Title,
    string Description,
    JsonDocument InputSchema,
    JsonDocument OutputSchema,
    Func<JsonElement, CancellationToken, Task<object>> Handler);
```

Then:

* CLI: generates commands + options from `ToolDescriptor` metadata (or just maps manually but validates parity in tests).
* MCP: registers tools with the MCP SDK from the same descriptors.

Even if you use `McpServerToolAttribute` to generate schemas automatically, keep a “schema snapshot” test that asserts:

* CLI option set == MCP inputSchema properties
* required flags match
* tool names match

---

## 7) What you’ll have at the end of the next phases (testable artifacts)

If you follow the phase plan you already approved, you’ll get:

* Phase 1: `winpeek capture image` works on real machines (WGC-based). ([GitHub][2])
* Phase 2–4: `winpeek uia snapshot`, `find`, `click`, `set-value` works using FlaUI across Win32/WPF/etc. ([GitHub][4])
* Phase 7: `winpeek mcp serve` exposes the exact same operations as MCP tools, with schemas per MCP spec. ([Model Context Protocol][8])

---

If you want the next step, I can turn this into:

1. a **versioned “Selector DSL v1” grammar** (BNF + examples + error messages), and
2. a **concrete “element ref stability” strategy** (how `refId` is derived, when it expires, and how to re-resolve).

[1]: https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.direct3d11captureframepool.createfreethreaded?view=winrt-26100&utm_source=chatgpt.com "Direct3D11CaptureFramePool.CreateFreeThreaded Method"
[2]: https://github.com/robmikh/Win32CaptureSample?utm_source=chatgpt.com "robmikh/Win32CaptureSample: A simple sample using ..."
[3]: https://github.com/microsoft/CsWinRT?utm_source=chatgpt.com "microsoft/CsWinRT: C# language projection for the ..."
[4]: https://github.com/FlaUI/FlaUI?utm_source=chatgpt.com "FlaUI/FlaUI: UI automation library for .Net"
[5]: https://github.com/dotnet/command-line-api?utm_source=chatgpt.com "dotnet/command-line-api"
[6]: https://github.com/spectreconsole/spectre.cli?utm_source=chatgpt.com "spectreconsole/spectre.console.cli"
[7]: https://github.com/serilog/serilog?utm_source=chatgpt.com "serilog/serilog: Simple .NET logging with fully-structured ..."
[8]: https://modelcontextprotocol.io/specification/2025-06-18/server/tools "Tools - Model Context Protocol"
[9]: https://modelcontextprotocol.github.io/csharp-sdk/api/ModelContextProtocol.Server.McpServerToolAttribute.html "Class McpServerToolAttribute | MCP C# SDK "
