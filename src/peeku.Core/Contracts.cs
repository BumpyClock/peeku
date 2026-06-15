using System.Text.Json;

namespace peeku;

public interface IPeekuClient
{
  Task<DoctorResult> DoctorAsync(DoctorRequest req, CancellationToken ct = default);

  Task<WindowListResult> WindowsListAsync(WindowsListRequest req, CancellationToken ct = default);
  Task<FocusedWindowResult> WindowsFocusedAsync(CancellationToken ct = default);
  Task<FocusedWindowResult> WindowFocusAsync(WindowFocusRequest req, CancellationToken ct = default);

  Task<CaptureImageResult> CaptureImageAsync(CaptureImageRequest req, CancellationToken ct = default);

  Task<UiaSnapshotResult> UiaSnapshotAsync(UiaSnapshotRequest req, CancellationToken ct = default);
  Task<SeeResult> SeeAsync(SeeRequest req, CancellationToken ct = default);
  Task<FindResult> FindAsync(FindRequest req, CancellationToken ct = default);
  Task<ElementGetResult> ElementGetAsync(ElementGetRequest req, CancellationToken ct = default);

  Task<ActionResult> ClickAsync(ClickRequest req, CancellationToken ct = default);
  Task<ActionResult> InvokeAsync(InvokeRequest req, CancellationToken ct = default);
  Task<ActionResult> SetValueAsync(SetValueRequest req, CancellationToken ct = default);
  Task<ActionResult> TypeAsync(TypeRequest req, CancellationToken ct = default);
  Task<ActionResult> ScrollAsync(ScrollRequest req, CancellationToken ct = default);
  Task<ActionResult> HotkeyAsync(HotkeyRequest req, CancellationToken ct = default);
  Task<ActionResult> PressAsync(PressRequest req, CancellationToken ct = default);

  IAsyncEnumerable<ObservationEvent> ObserveAsync(ObserveRequest req, CancellationToken ct = default);
  Task<WaitResult> WaitAsync(WaitRequest req, CancellationToken ct = default);

  Task<BatchResult> BatchAsync(BatchRequest req, CancellationToken ct = default);

  Task<DiffResult> DiffAsync(DiffRequest req, CancellationToken ct = default);
  Task<ElementAtPointResult> ElementAtPointAsync(ElementAtPointRequest req, CancellationToken ct = default);
}

public abstract record Target
{
  public sealed record Desktop : Target;
  public sealed record FocusedWindow : Target;
  public sealed record Screen(int ScreenIndex) : Target;
  public sealed record WindowByHwnd(string HwndHex) : Target;
  public sealed record WindowByQuery(WindowQuery Query) : Target;

  public static Target Focused() => new FocusedWindow();
}

public record WindowQuery(
  string? TitleContains = null,
  string? ProcessName = null,
  int? ProcessId = null);

public record Selector(
  string Expr,
  bool PreferCachedSnapshot = true);

public record ElementRef(
  string RefId,
  string? SnapshotId = null);

public record Rect(
  double X,
  double Y,
  double Width,
  double Height);

public record ResultMeta(
  string TraceId,
  DateTimeOffset Timestamp,
  int DurationMs,
  string? Warning = null);

public record PeekuError(
  string Code,
  string Message,
  object? Details = null);

public abstract record ResultBase(
  bool Ok,
  ResultMeta Meta,
  PeekuError? Error = null)
{
  public string TraceId => Meta.TraceId;
}

public record DoctorRequest(bool Deep = false);

public record DoctorCheck(string Name, bool Ok, string? Details = null);

public record DoctorResult(
  bool Ok,
  ResultMeta Meta,
  IReadOnlyList<DoctorCheck> Checks,
  PeekuError? Error = null) : ResultBase(Ok, Meta, Error);

public record WindowsListRequest(
  string? TitleContains = null,
  string? ProcessName = null,
  int Limit = 50);

public record WindowInfo(
  string HwndHex,
  int ProcessId,
  string? Title = null,
  string? ProcessName = null);

public record WindowListResult(
  bool Ok,
  ResultMeta Meta,
  IReadOnlyList<WindowInfo> Windows,
  PeekuError? Error = null) : ResultBase(Ok, Meta, Error);

public record FocusedWindowResult(
  bool Ok,
  ResultMeta Meta,
  WindowInfo? Window,
  PeekuError? Error = null) : ResultBase(Ok, Meta, Error);

public record WindowFocusRequest(Target Target);

public record CaptureImageRequest(
  Target Target,
  string? OutPath = null,
  bool IncludeBase64 = false);

public record CaptureImageResult(
  bool Ok,
  ResultMeta Meta,
  string ImagePath,
  string MimeType,
  int Width,
  int Height,
  string? Base64Png = null,
  PeekuError? Error = null) : ResultBase(Ok, Meta, Error);

public enum UiaPropertiesMode
{
  Basic = 0,
  All = 1,
}

public record UiaSnapshotRequest(
  Target Target,
  int Depth = 6,
  int MaxNodes = 5000,
  UiaPropertiesMode IncludeProperties = UiaPropertiesMode.Basic);

public record UiaNode(
  ElementRef Element,
  string? Name = null,
  string? ControlType = null,
  IReadOnlyList<UiaNode>? Children = null);

public record UiaElement(
  ElementRef Element,
  Rect? Rect = null,
  string? Name = null,
  string? ControlType = null,
  string? AutomationId = null,
  string? ClassName = null,
  IReadOnlyList<string>? Actions = null,
  IReadOnlyDictionary<string, object?>? State = null);

public record UiaSnapshotResult(
  bool Ok,
  ResultMeta Meta,
  string SnapshotId,
  UiaNode Root,
  IReadOnlyList<UiaElement> Elements,
  PeekuError? Error = null) : ResultBase(Ok, Meta, Error);

public record SeeRequest(
  Target Target,
  int Depth = 6,
  int MaxNodes = 5000,
  bool IncludeBase64 = false,
  UiaPropertiesMode IncludeProperties = UiaPropertiesMode.Basic);

public record SeeResult(
  bool Ok,
  ResultMeta Meta,
  CaptureImageResult Image,
  string SnapshotId,
  IReadOnlyList<UiaElement> Elements,
  PeekuError? Error = null) : ResultBase(Ok, Meta, Error);

public record FindRequest(
  Selector Selector,
  Target? Target = null,
  int Limit = 20);

public record FindMatch(
  ElementRef Element,
  Rect Rect,
  double Score,
  string? Name = null,
  string? ControlType = null);

public record FindResult(
  bool Ok,
  ResultMeta Meta,
  IReadOnlyList<FindMatch> Matches,
  PeekuError? Error = null) : ResultBase(Ok, Meta, Error);

public record ElementGetRequest(
  ElementRef? Element = null,
  Selector? Selector = null,
  Target? Target = null,
  UiaPropertiesMode IncludeProperties = UiaPropertiesMode.All);

public record ElementGetResult(
  bool Ok,
  ResultMeta Meta,
  UiaElement Element,
  IReadOnlyDictionary<string, object?> Properties,
  IReadOnlyList<string> Patterns,
  Rect? Rect = null,
  PeekuError? Error = null) : ResultBase(Ok, Meta, Error);

public enum ActionMethod
{
  Auto = 0,
  Uia = 1,
  Input = 2,
}

public record ClickRequest(
  ElementRef? Element = null,
  Selector? Selector = null,
  Target? Target = null,
  ActionMethod Method = ActionMethod.Auto);

public record InvokeRequest(
  ElementRef? Element = null,
  Selector? Selector = null,
  Target? Target = null);

public record SetValueRequest(
  ElementRef? Element = null,
  Selector? Selector = null,
  Target? Target = null,
  string Value = "");

public record TypeRequest(
  ElementRef? Element = null,
  Selector? Selector = null,
  Target? Target = null,
  string Text = "",
  bool Append = true,
  int? DelayMs = null);

public enum ScrollDirection
{
  Vertical = 0,
  Horizontal = 1,
}

public record ScrollRequest(
  ElementRef? Element = null,
  Selector? Selector = null,
  Target? Target = null,
  int? Delta = null,
  int? Lines = null,
  ScrollDirection Direction = ScrollDirection.Vertical);

public record HotkeyRequest(string Keys);

public record PressRequest(
  IReadOnlyList<string> Keys,
  int Count = 1,
  int? DelayMs = null,
  int? HoldMs = null,
  Target? Target = null);

public record ActionResult(
  bool Ok,
  ResultMeta Meta,
  ActionMethod? MethodUsed = null,
  PeekuError? Error = null,
  JsonElement? Evidence = null) : ResultBase(Ok, Meta, Error);

public enum ObserveEventSet
{
  Structure = 0,
  Property = 1,
  Focus = 2,
  All = 3,
}

public record ObserveRequest(
  Target Target,
  ObserveEventSet Events = ObserveEventSet.All,
  TimeSpan? Duration = null,
  int MaxEvents = 200);

public record ObservationEvent(
  DateTimeOffset Timestamp,
  string EventType,
  IReadOnlyDictionary<string, object?>? Data = null);

public record WaitRequest(
  Selector Selector,
  Target Target,
  TimeSpan Timeout);

public record WaitResult(
  bool Ok,
  ResultMeta Meta,
  bool Found,
  ElementRef? Element = null,
  PeekuError? Error = null) : ResultBase(Ok, Meta, Error);

public record BatchRequest(
  IReadOnlyList<BatchOp> Ops,
  bool StopOnError = true);

public record BatchOp(
  string Tool,
  JsonElement Args);

public record BatchStepResult(
  string Tool,
  bool Ok,
  int DurationMs,
  object? Result = null,
  PeekuError? Error = null);

public record BatchResult(
  bool Ok,
  ResultMeta Meta,
  IReadOnlyList<BatchStepResult> Results,
  PeekuError? Error = null) : ResultBase(Ok, Meta, Error);

public record DiffRequest(
  Target TargetBefore,
  Target TargetAfter,
  int Depth = 6,
  int MaxNodes = 5000,
  UiaPropertiesMode IncludeProperties = UiaPropertiesMode.Basic);

public record UiaDeltaRoot(
  UiaNode Subtree,
  IReadOnlyList<UiaElement> Ancestors);

public record UiaTreeDelta(
  IReadOnlyList<UiaDeltaRoot> Added,
  IReadOnlyList<UiaDeltaRoot> Removed,
  bool Truncated);

public record DiffResult(
  bool Ok,
  ResultMeta Meta,
  string? SnapshotIdBefore,
  string? SnapshotIdAfter,
  UiaTreeDelta? Delta = null,
  PeekuError? Error = null) : ResultBase(Ok, Meta, Error);

// Target is reserved and currently unused: FromPoint resolves by absolute screen pixel,
// so there is no subtree to scope. Kept for schema symmetry / forward-compat.
public record ElementAtPointRequest(
  int X,
  int Y,
  Target? Target = null,
  UiaPropertiesMode IncludeProperties = UiaPropertiesMode.All);

public record ElementAtPointResult(
  bool Ok,
  ResultMeta Meta,
  UiaElement Element,
  IReadOnlyList<UiaElement> Ancestors,
  PeekuError? Error = null) : ResultBase(Ok, Meta, Error);
