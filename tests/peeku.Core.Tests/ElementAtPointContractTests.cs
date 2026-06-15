using System.Text.Json;
using peeku;
using Xunit;

namespace peeku.Core.Tests;

/// <summary>
/// Contract + wiring tests for peeku_element_from_point / ElementAtPointAsync.
/// Does NOT exercise live UIA (FromPoint needs a real desktop); covers:
///   - Request/result record shapes compile and initialise correctly.
///   - ToolRegistry contains the tool with correct schema shape.
///   - BatchRunner arm is wired (dispatches to client, not the default NotSupported arm).
///   - IPeekuClient has the method (compile-time + reflection).
/// </summary>
public sealed class ElementAtPointContractTests
{
  // ── 1. Request / result records ──────────────────────────────────────────────

  [Fact]
  public void Request_DefaultProperties_AreCorrect()
  {
    var req = new ElementAtPointRequest(X: 10, Y: 20);
    Assert.Equal(10, req.X);
    Assert.Equal(20, req.Y);
    Assert.Null(req.Target);
    Assert.Equal(UiaPropertiesMode.All, req.IncludeProperties);
  }

  [Fact]
  public void Request_WithAllProperties_RoundTrips()
  {
    var req = new ElementAtPointRequest(
      X: 100,
      Y: 200,
      Target: new Target.Desktop(),
      IncludeProperties: UiaPropertiesMode.Basic);

    Assert.Equal(100, req.X);
    Assert.Equal(200, req.Y);
    Assert.IsType<Target.Desktop>(req.Target);
    Assert.Equal(UiaPropertiesMode.Basic, req.IncludeProperties);
  }

  [Fact]
  public void Result_Ok_CanBeConstructed()
  {
    var meta = Results.Start().Meta();
    var element = new UiaElement(new ElementRef("uia:1234:abcdef01"));
    var ancestors = new[] { new UiaElement(new ElementRef("uia:1234:00000001")) };

    var result = new ElementAtPointResult(
      Ok: true,
      Meta: meta,
      Element: element,
      Ancestors: ancestors);

    Assert.True(result.Ok);
    Assert.Same(element, result.Element);
    Assert.Single(result.Ancestors);
    Assert.Null(result.Error);
  }

  [Fact]
  public void Result_Error_CanBeConstructed()
  {
    var meta = Results.Start().Meta();
    var element = new UiaElement(new ElementRef(""));
    var error = PeekuErrors.Create(PeekuErrorCode.ElementNotFound, "No element found at point.");

    var result = new ElementAtPointResult(
      Ok: false,
      Meta: meta,
      Element: element,
      Ancestors: Array.Empty<UiaElement>(),
      Error: error);

    Assert.False(result.Ok);
    Assert.NotNull(result.Error);
    Assert.Equal(PeekuErrors.Code(PeekuErrorCode.ElementNotFound), result.Error!.Code);
  }

  [Fact]
  public void Result_InheritsResultBase_TraceId()
  {
    var meta = Results.Start("test-trace").Meta();
    var result = new ElementAtPointResult(
      Ok: true,
      Meta: meta,
      Element: new UiaElement(new ElementRef("")),
      Ancestors: Array.Empty<UiaElement>());

    Assert.Equal("test-trace", result.TraceId);
  }

  // ── 2. ToolRegistry ──────────────────────────────────────────────────────────

  [Fact]
  public void ToolRegistry_Contains_ElementFromPoint()
  {
    Assert.True(
      ToolRegistry.TryGet("peeku_element_from_point", out var tool),
      "peeku_element_from_point not found in ToolRegistry.");

    Assert.Equal("peeku_element_from_point", tool.Name);
  }

  [Fact]
  public void ToolRegistry_ElementFromPoint_Schema_HasRequiredXAndY()
  {
    ToolRegistry.TryGet("peeku_element_from_point", out var tool);

    var root = tool.InputSchema.RootElement;
    Assert.True(root.TryGetProperty("properties", out var props));
    Assert.True(props.TryGetProperty("x", out _), "Input schema missing 'x'.");
    Assert.True(props.TryGetProperty("y", out _), "Input schema missing 'y'.");

    Assert.True(root.TryGetProperty("required", out var req));
    var required = req.EnumerateArray().Select(e => e.GetString()).ToHashSet(StringComparer.Ordinal);
    Assert.Contains("x", required);
    Assert.Contains("y", required);
  }

  [Fact]
  public void ToolRegistry_ElementFromPoint_OutputSchema_HasElementAndAncestors()
  {
    ToolRegistry.TryGet("peeku_element_from_point", out var tool);

    var root = tool.OutputSchema.RootElement;
    Assert.True(root.TryGetProperty("properties", out var props));
    Assert.True(props.TryGetProperty("element", out _), "Output schema missing 'element'.");
    Assert.True(props.TryGetProperty("ancestors", out _), "Output schema missing 'ancestors'.");
  }

  // ── 3. BatchRunner arm ───────────────────────────────────────────────────────

  [Fact]
  public async Task BatchRunner_ElementFromPoint_HasArm_NotNotSupported()
  {
    var argsJson = """{"x":100,"y":200}""";
    var args = JsonDocument.Parse(argsJson).RootElement;
    var op = new BatchOp("peeku_element_from_point", args);
    var req = new BatchRequest(Ops: [op], StopOnError: false);

    var result = await BatchRunner.RunAsync(
      new ThrowingFakeClientForHitTest(),
      req,
      CancellationToken.None);

    Assert.Single(result.Results);
    var step = result.Results[0];

    Assert.True(
      step.Error?.Code != PeekuErrors.Code(PeekuErrorCode.NotSupported),
      "peeku_element_from_point hit the BatchRunner default arm (NotSupported) — arm is missing.");
  }

  // ── 4. IPeekuClient has the method (compile-time check via reflection) ───────

  [Fact]
  public void IPeekuClient_Has_ElementAtPointAsync()
  {
    var method = typeof(IPeekuClient).GetMethod(nameof(IPeekuClient.ElementAtPointAsync));
    Assert.NotNull(method);
    Assert.Equal(
      typeof(Task<ElementAtPointResult>),
      method!.ReturnType);
  }
}

// Throws NotImplementedException → BatchRunner wraps as Internal, not NotSupported.
file sealed class ThrowingFakeClientForHitTest : IPeekuClient
{
  public Task<DoctorResult> DoctorAsync(DoctorRequest req, CancellationToken ct = default)
    => Throw<DoctorResult>();
  public Task<WindowListResult> WindowsListAsync(WindowsListRequest req, CancellationToken ct = default)
    => Throw<WindowListResult>();
  public Task<FocusedWindowResult> WindowsFocusedAsync(CancellationToken ct = default)
    => Throw<FocusedWindowResult>();
  public Task<CaptureImageResult> CaptureImageAsync(CaptureImageRequest req, CancellationToken ct = default)
    => Throw<CaptureImageResult>();
  public Task<UiaSnapshotResult> UiaSnapshotAsync(UiaSnapshotRequest req, CancellationToken ct = default)
    => Throw<UiaSnapshotResult>();
  public Task<SeeResult> SeeAsync(SeeRequest req, CancellationToken ct = default)
    => Throw<SeeResult>();
  public Task<FindResult> FindAsync(FindRequest req, CancellationToken ct = default)
    => Throw<FindResult>();
  public Task<ElementGetResult> ElementGetAsync(ElementGetRequest req, CancellationToken ct = default)
    => Throw<ElementGetResult>();
  public Task<ActionResult> ClickAsync(ClickRequest req, CancellationToken ct = default)
    => Throw<ActionResult>();
  public Task<ActionResult> InvokeAsync(InvokeRequest req, CancellationToken ct = default)
    => Throw<ActionResult>();
  public Task<ActionResult> SetValueAsync(SetValueRequest req, CancellationToken ct = default)
    => Throw<ActionResult>();
  public Task<ActionResult> TypeAsync(TypeRequest req, CancellationToken ct = default)
    => Throw<ActionResult>();
  public Task<ActionResult> ScrollAsync(ScrollRequest req, CancellationToken ct = default)
    => Throw<ActionResult>();
  public Task<ActionResult> HotkeyAsync(HotkeyRequest req, CancellationToken ct = default)
    => Throw<ActionResult>();
  public IAsyncEnumerable<ObservationEvent> ObserveAsync(ObserveRequest req, CancellationToken ct = default)
    => throw new NotImplementedException();
  public Task<WaitResult> WaitAsync(WaitRequest req, CancellationToken ct = default)
    => Throw<WaitResult>();
  public Task<BatchResult> BatchAsync(BatchRequest req, CancellationToken ct = default)
    => Throw<BatchResult>();
  public Task<FocusedWindowResult> WindowFocusAsync(WindowFocusRequest req, CancellationToken ct = default)
    => Throw<FocusedWindowResult>();
  public Task<ActionResult> PressAsync(PressRequest req, CancellationToken ct = default)
    => Throw<ActionResult>();
  public Task<DiffResult> DiffAsync(DiffRequest req, CancellationToken ct = default)
    => Throw<DiffResult>();
  public Task<ElementAtPointResult> ElementAtPointAsync(ElementAtPointRequest req, CancellationToken ct = default)
    => Throw<ElementAtPointResult>();

  private static Task<T> Throw<T>()
    => Task.FromException<T>(new NotImplementedException());
}
