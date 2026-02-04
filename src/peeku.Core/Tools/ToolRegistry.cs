using System.Collections.ObjectModel;
using System.Text.Json;

namespace peeku;

public static class ToolRegistry
{
  public static IReadOnlyList<ToolDescriptor> All => _all;

  private static readonly ReadOnlyCollection<ToolDescriptor> _all = new(BuildAll());
  private static readonly Dictionary<string, ToolDescriptor> _byName = _all.ToDictionary(x => x.Name, StringComparer.Ordinal);

  public static bool TryGet(string name, out ToolDescriptor tool)
    => _byName.TryGetValue(name, out tool!);

  public static ToolDescriptor Get(string name)
    => _byName.TryGetValue(name, out var tool)
      ? tool
      : throw new KeyNotFoundException($"Unknown tool: '{name}'.");

  private static List<ToolDescriptor> BuildAll()
  {
    return
    [
      Create("peeku_doctor", "Doctor", "Self-checks (optionally deep: capture + UIA).", DoctorInputSchemaJson, DoctorOutputSchemaJson),
      Create("peeku_windows_list", "Windows List", "List windows with optional filters.", WindowsListInputSchemaJson, WindowsListOutputSchemaJson),
      Create("peeku_windows_focused", "Windows Focused", "Get focused window.", WindowsFocusedInputSchemaJson, WindowsFocusedOutputSchemaJson),
      Create("peeku_capture_image", "Capture Image", "Capture a screenshot/image of a target.", CaptureImageInputSchemaJson, CaptureImageOutputSchemaJson),
      Create("peeku_uia_snapshot", "UIA Snapshot", "Capture a UI Automation snapshot for a target.", UiaSnapshotInputSchemaJson, UiaSnapshotOutputSchemaJson),
      Create("peeku_see", "See", "Capture image + UIA snapshot.", SeeInputSchemaJson, SeeOutputSchemaJson),
      Create("peeku_find", "Find", "Find elements by selector.", FindInputSchemaJson, FindOutputSchemaJson),
      Create("peeku_element_get", "Element Get", "Get element details/properties.", ElementGetInputSchemaJson, ElementGetOutputSchemaJson),
      Create("peeku_click", "Click", "Click an element.", ClickInputSchemaJson, ClickOutputSchemaJson),
      Create("peeku_invoke", "Invoke", "Invoke an element.", InvokeInputSchemaJson, ClickOutputSchemaJson),
      Create("peeku_set_value", "Set Value", "Set element value.", SetValueInputSchemaJson, SetValueOutputSchemaJson),
      Create("peeku_type", "Type", "Type text into an element.", TypeInputSchemaJson, MinimalActionOutputSchemaJson),
      Create("peeku_scroll", "Scroll", "Scroll on an element/target.", ScrollInputSchemaJson, MinimalActionOutputSchemaJson),
      Create("peeku_hotkey", "Hotkey", "Send a hotkey chord.", HotkeyInputSchemaJson, MinimalActionOutputSchemaJson),
      Create("peeku_observe", "Observe", "Observe UIA events (bounded).", ObserveInputSchemaJson, ObserveOutputSchemaJson),
      Create("peeku_wait", "Wait", "Wait until selector matches.", WaitInputSchemaJson, WaitOutputSchemaJson),
      Create("peeku_batch", "Batch", "Execute a batch of tool calls.", BatchInputSchemaJson, BatchOutputSchemaJson),
    ];
  }

  private static ToolDescriptor Create(
    string name,
    string title,
    string description,
    string inputSchemaJson,
    string outputSchemaJson)
  {
    var inputSchema = ParseSchema(name, "inputSchema", inputSchemaJson);
    var outputSchema = ParseSchema(name, "outputSchema", outputSchemaJson);
    return new(
      name,
      title,
      description,
      inputSchema,
      outputSchema,
      static (_, _) => Task.FromException<object>(new NotImplementedException("Tool handler not implemented.")));
  }

  private static JsonDocument ParseSchema(string toolName, string schemaName, string json)
  {
    try
    {
      return JsonDocument.Parse(json);
    }
    catch (Exception ex)
    {
      throw new InvalidOperationException($"Invalid {schemaName} for tool '{toolName}'.", ex);
    }
  }

  private const string DoctorInputSchemaJson = """{"type":"object","properties":{"deep":{"type":"boolean","default":false,"description":"Run slower checks (capture + UIA)"}}}""";
  private const string DoctorOutputSchemaJson = """{"type":"object","properties":{"ok":{"type":"boolean"},"traceId":{"type":"string"},"checks":{"type":"array","items":{"type":"object","properties":{"name":{"type":"string"},"ok":{"type":"boolean"},"details":{"type":"string"}},"required":["name","ok"]}}},"required":["ok","traceId","checks"]}""";

  private const string WindowsListInputSchemaJson = """{"type":"object","properties":{"titleContains":{"type":"string"},"processName":{"type":"string"},"limit":{"type":"integer","default":50}}}""";
  private const string WindowsListOutputSchemaJson = """{"type":"object","properties":{"ok":{"type":"boolean"},"traceId":{"type":"string"},"windows":{"type":"array","items":{"type":"object","properties":{"hwndHex":{"type":"string"},"title":{"type":"string"},"processId":{"type":"integer"},"processName":{"type":"string"}},"required":["hwndHex","processId"]}}},"required":["ok","traceId","windows"]}""";

  private const string WindowsFocusedInputSchemaJson = """{"type":"object","properties":{}}""";
  private const string WindowsFocusedOutputSchemaJson = """{"type":"object","properties":{"ok":{"type":"boolean"},"traceId":{"type":"string"},"window":{"type":"object","properties":{"hwndHex":{"type":"string"},"title":{"type":"string"},"processId":{"type":"integer"},"processName":{"type":"string"}},"required":["hwndHex","processId"]}},"required":["ok","traceId","window"]}""";

  private const string CaptureImageInputSchemaJson = """{"type":"object","properties":{"target":{"description":"What to capture","type":"object"},"outPath":{"type":"string","description":"Optional output path on disk"},"includeBase64":{"type":"boolean","default":false},"imageFormat":{"type":"string","enum":["png"],"default":"png"}},"required":["target"]}""";
  private const string CaptureImageOutputSchemaJson = """{"type":"object","properties":{"ok":{"type":"boolean"},"traceId":{"type":"string"},"imagePath":{"type":"string"},"mimeType":{"type":"string","default":"image/png"},"base64":{"type":"string","description":"Present only if includeBase64=true"},"width":{"type":"integer"},"height":{"type":"integer"}},"required":["ok","traceId","imagePath","mimeType","width","height"]}""";

  private const string UiaSnapshotInputSchemaJson = """{"type":"object","properties":{"target":{"type":"object"},"depth":{"type":"integer","default":6},"maxNodes":{"type":"integer","default":5000},"includeProperties":{"type":"string","enum":["basic","all"],"default":"basic"}},"required":["target"]}""";
  private const string UiaSnapshotOutputSchemaJson = """{"type":"object","properties":{"ok":{"type":"boolean"},"traceId":{"type":"string"},"snapshotId":{"type":"string"},"root":{"type":"object"},"elements":{"type":"array","items":{"type":"object"}}},"required":["ok","traceId","snapshotId","root","elements"]}""";

  private const string SeeInputSchemaJson = """{"type":"object","properties":{"target":{"type":"object"},"depth":{"type":"integer","default":6},"maxNodes":{"type":"integer","default":5000},"includeProperties":{"type":"string","enum":["basic","all"],"default":"basic"},"includeBase64":{"type":"boolean","default":false}},"required":["target"]}""";
  private const string SeeOutputSchemaJson = """{"type":"object","properties":{"ok":{"type":"boolean"},"traceId":{"type":"string"},"image":{"type":"object"},"snapshotId":{"type":"string"},"elements":{"type":"array","items":{"type":"object"}}},"required":["ok","traceId","image","snapshotId","elements"]}""";

  private const string FindInputSchemaJson = """{"type":"object","properties":{"selector":{"type":"object"},"target":{"type":"object","description":"Optional; defaults to focused window"},"limit":{"type":"integer","default":20}},"required":["selector"]}""";
  private const string FindOutputSchemaJson = """{"type":"object","properties":{"ok":{"type":"boolean"},"traceId":{"type":"string"},"matches":{"type":"array","items":{"type":"object","properties":{"element":{"type":"object"},"name":{"type":"string"},"automationId":{"type":"string"},"controlType":{"type":"string"},"rect":{"type":"object"},"score":{"type":"number"}},"required":["element","rect","score"]}}},"required":["ok","traceId","matches"]}""";

  private const string ElementGetInputSchemaJson = """{"type":"object","properties":{"elementRef":{"type":"object"},"selector":{"type":"object"},"target":{"type":"object"},"includeProperties":{"type":"string","enum":["basic","all"],"default":"all"}}}""";
  private const string ElementGetOutputSchemaJson = """{"type":"object","properties":{"ok":{"type":"boolean"},"traceId":{"type":"string"},"element":{"type":"object"},"properties":{"type":"object"},"patterns":{"type":"array","items":{"type":"string"}},"rect":{"type":"object"}},"required":["ok","traceId","element","properties","patterns","rect"]}""";

  private const string ClickInputSchemaJson = """{"type":"object","properties":{"elementRef":{"type":"object"},"selector":{"type":"object"},"target":{"type":"object"},"method":{"type":"string","enum":["auto","uia","input"],"default":"auto"}}}""";
  private const string ClickOutputSchemaJson = """{"type":"object","properties":{"ok":{"type":"boolean"},"traceId":{"type":"string"},"methodUsed":{"type":"string"},"clickedElement":{"type":"object"}},"required":["ok","traceId","methodUsed"]}""";

  private const string InvokeInputSchemaJson = """{"type":"object","properties":{"elementRef":{"type":"object"},"selector":{"type":"object"},"target":{"type":"object"}}}""";

  private const string SetValueInputSchemaJson = """{"type":"object","properties":{"elementRef":{"type":"object"},"selector":{"type":"object"},"target":{"type":"object"},"value":{"type":"string"}},"required":["value"]}""";
  private const string SetValueOutputSchemaJson = """{"type":"object","properties":{"ok":{"type":"boolean"},"traceId":{"type":"string"},"setOnElement":{"type":"object"}},"required":["ok","traceId"]}""";

  private const string TypeInputSchemaJson = """{"type":"object","properties":{"elementRef":{"type":"object"},"selector":{"type":"object"},"target":{"type":"object"},"text":{"type":"string"},"append":{"type":"boolean","default":true},"delayMs":{"type":"integer","default":0}},"required":["text"]}""";
  private const string MinimalActionOutputSchemaJson = """{"type":"object","properties":{"ok":{"type":"boolean"},"traceId":{"type":"string"}},"required":["ok","traceId"]}""";

  private const string ScrollInputSchemaJson = """{"type":"object","properties":{"elementRef":{"type":"object"},"selector":{"type":"object"},"target":{"type":"object"},"direction":{"type":"string","enum":["vertical","horizontal"],"default":"vertical"},"delta":{"type":"integer","description":"Wheel delta, e.g. 120/-120"}},"required":["delta"]}""";

  private const string HotkeyInputSchemaJson = """{"type":"object","properties":{"keys":{"type":"string","description":"e.g. CTRL+SHIFT+S"}},"required":["keys"]}""";

  private const string ObserveInputSchemaJson = """{"type":"object","properties":{"target":{"type":"object"},"events":{"type":"array","items":{"type":"string","enum":["structure","property","focus"]},"default":["structure","property","focus"]},"durationMs":{"type":"integer","default":10000},"maxEvents":{"type":"integer","default":200}},"required":["target"]}""";
  private const string ObserveOutputSchemaJson = """{"type":"object","properties":{"ok":{"type":"boolean"},"traceId":{"type":"string"},"events":{"type":"array","items":{"type":"object","properties":{"timestamp":{"type":"string"},"eventType":{"type":"string"},"element":{"type":"object"},"changedProperty":{"type":"string"},"oldValue":{},"newValue":{}},"required":["timestamp","eventType"]}}},"required":["ok","traceId","events"]}""";

  private const string WaitInputSchemaJson = """{"type":"object","properties":{"selector":{"type":"object"},"target":{"type":"object"},"timeoutMs":{"type":"integer","default":10000},"pollMs":{"type":"integer","default":200},"returnSnapshot":{"type":"boolean","default":false}},"required":["selector","target"]}""";
  private const string WaitOutputSchemaJson = """{"type":"object","properties":{"ok":{"type":"boolean"},"traceId":{"type":"string"},"found":{"type":"boolean"},"elementRef":{"type":"object"},"snapshotId":{"type":"string"},"elements":{"type":"array","items":{"type":"object"}}},"required":["ok","traceId","found"]}""";

  private const string BatchInputSchemaJson = """{"type":"object","properties":{"ops":{"type":"array","items":{"type":"object","properties":{"tool":{"type":"string"},"args":{"type":"object"}},"required":["tool","args"]}},"stopOnError":{"type":"boolean","default":true}},"required":["ops"]}""";
  private const string BatchOutputSchemaJson = """{"type":"object","properties":{"ok":{"type":"boolean"},"traceId":{"type":"string"},"results":{"type":"array","items":{"type":"object","properties":{"tool":{"type":"string"},"ok":{"type":"boolean"},"durationMs":{"type":"integer"},"result":{},"error":{"type":"object"}},"required":["tool","ok","durationMs"]}}},"required":["ok","traceId","results"]}""";
}

