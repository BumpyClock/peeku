using System.Runtime.CompilerServices;
using System.Text.Json;
using peeku;
using Xunit;

namespace peeku.Core.Tests;

/// <summary>
/// Drift-detector: asserts that ToolRegistry.All, BatchRunner's dispatch switch,
/// and IPeekuClient's async methods stay in sync.
///
/// A RED test here means real drift exists — do not weaken the test to make it green.
/// Fix the drift instead.
/// </summary>
public sealed class ToolParityTests
{
  // Tools routed outside the BatchRunner switch; excluded from both checks.
  private static readonly HashSet<string> Specials =
    new(StringComparer.Ordinal) { "peeku_doctor", "peeku_batch" };

  // ────────────────────────────────────────────────────────────────────────────
  // (a) Executor parity
  //
  // For each non-special tool, dispatch a minimal-valid BatchRequest through
  // BatchRunner. The fake client throws on every call. A matching switch arm
  // will invoke the client → catch (Exception) → Error.Code = "Internal".
  // The default/missing arm returns Error.Code = "NotSupported".
  // Assert: no tool lands on the "NotSupported" default arm.
  // ────────────────────────────────────────────────────────────────────────────

  [Fact]
  public async Task Executor_Parity_Every_NonSpecial_Tool_Has_A_BatchRunner_Arm()
  {
    var nonSpecials = ToolRegistry.All
      .Where(t => !Specials.Contains(t.Name))
      .ToList();

    var notSupported = new List<string>();

    foreach (var tool in nonSpecials)
    {
      var argsJson = MinimalArgsJson(tool.Name);
      var args = JsonDocument.Parse(argsJson).RootElement;

      var op = new BatchOp(tool.Name, args);
      var req = new BatchRequest(
        Ops: [op],
        StopOnError: false);

      var result = await BatchRunner.RunAsync(
        new ThrowingFakeClient(),
        req,
        CancellationToken.None);

      // BatchRunner.RunAsync wraps each step; inspect the step result.
      Assert.Single(result.Results);
      var step = result.Results[0];

      if (step.Error?.Code == PeekuErrors.Code(PeekuErrorCode.NotSupported))
      {
        notSupported.Add(tool.Name);
      }
    }

    Assert.True(
      notSupported.Count == 0,
      $"These tools hit the BatchRunner default arm (missing switch case) — executor drift detected: [{string.Join(", ", notSupported)}]");
  }

  // ────────────────────────────────────────────────────────────────────────────
  // (b) Interface parity (reflection)
  //
  // Maps each non-special tool name snake_case → PascalCase + "Async" and
  // checks the resulting set equals the public async methods on IPeekuClient
  // minus DoctorAsync and BatchAsync.
  //
  // Known name-mapping exceptions are listed explicitly so future renames
  // that break the naive algorithm are caught here rather than silently
  // producing a wrong method name.
  // ────────────────────────────────────────────────────────────────────────────

  [Fact]
  public void Interface_Parity_Every_NonSpecial_Tool_Has_A_Matching_IPeekuClient_Method()
  {
    // Methods that are excluded because they're covered by Specials.
    var excludedMethods = new HashSet<string>(StringComparer.Ordinal)
    {
      nameof(IPeekuClient.DoctorAsync),
      nameof(IPeekuClient.BatchAsync),
    };

    // Explicit name-mapping exceptions: tool name → expected method name.
    // These override the naive snake_case→PascalCase algorithm.
    var nameExceptions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
      ["peeku_windows_list"]    = "WindowsListAsync",
      ["peeku_windows_focused"] = "WindowsFocusedAsync",
      // Tool name keeps the windows_ group prefix, but the method is WindowFocusAsync (singular):
      // it focuses ONE window, not the system "focused window" query that WindowsFocusedAsync returns.
      ["peeku_windows_focus"]   = "WindowFocusAsync",
      ["peeku_uia_snapshot"]    = "UiaSnapshotAsync",
      ["peeku_element_get"]     = "ElementGetAsync",
      ["peeku_set_value"]       = "SetValueAsync",
      ["peeku_capture_image"]   = "CaptureImageAsync",
    };

    // Collect the public async methods on IPeekuClient.
    var interfaceAsyncMethods = typeof(IPeekuClient)
      .GetMethods()
      .Where(m => m.IsPublic && !excludedMethods.Contains(m.Name))
      .Where(m =>
        typeof(System.Threading.Tasks.Task).IsAssignableFrom(m.ReturnType) ||
        (m.ReturnType.IsGenericType &&
          m.ReturnType.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>)))
      .Select(m => m.Name)
      .ToHashSet(StringComparer.Ordinal);

    // Map tool names to expected method names.
    var nonSpecialTools = ToolRegistry.All
      .Where(t => !Specials.Contains(t.Name))
      .ToList();

    var toolToMethod = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var tool in nonSpecialTools)
    {
      if (nameExceptions.TryGetValue(tool.Name, out var mapped))
      {
        toolToMethod[tool.Name] = mapped;
      }
      else
      {
        toolToMethod[tool.Name] = SnakeToPascalAsync(tool.Name);
      }
    }

    var expectedMethods = toolToMethod.Values.ToHashSet(StringComparer.Ordinal);

    var toolsWithoutMethod = toolToMethod
      .Where(kv => !interfaceAsyncMethods.Contains(kv.Value))
      .Select(kv => $"{kv.Key} → {kv.Value}")
      .ToList();

    var methodsWithoutTool = interfaceAsyncMethods
      .Where(m => !expectedMethods.Contains(m))
      .ToList();

    var messages = new List<string>();
    if (toolsWithoutMethod.Count > 0)
      messages.Add($"Tools with no matching IPeekuClient method: [{string.Join(", ", toolsWithoutMethod)}]");
    if (methodsWithoutTool.Count > 0)
      messages.Add($"IPeekuClient methods with no matching tool: [{string.Join(", ", methodsWithoutTool)}]");

    Assert.True(
      messages.Count == 0,
      "Interface drift detected — " + string.Join("; ", messages));
  }

  // ────────────────────────────────────────────────────────────────────────────
  // Helpers
  // ────────────────────────────────────────────────────────────────────────────

  /// <summary>
  /// Returns a minimal JSON args object that passes BatchRunner's guard checks
  /// for each tool (enough to reach the client call, not enough to succeed).
  /// </summary>
  private static string MinimalArgsJson(string toolName) => toolName switch
  {
    // No required args.
    "peeku_windows_list"    => "{}",
    "peeku_windows_focused" => "{}",
    "peeku_windows_focus"   => """{"target":"focused"}""",
    "peeku_hotkey"          => """{"keys":"ctrl+c"}""",
    "peeku_press"           => """{"keys":["enter"]}""",

    // Require target (string shorthand "desktop" always valid).
    "peeku_capture_image"   => """{"target":"desktop"}""",
    "peeku_uia_snapshot"    => """{"target":"desktop"}""",
    "peeku_see"             => """{"target":"desktop"}""",
    "peeku_observe"         => """{"target":"desktop"}""",

    // Require selector (string shorthand).
    "peeku_find"            => """{"selector":"window"}""",

    // Require exactly one of elementRef or selector — provide selector.
    "peeku_element_get"     => """{"selector":"window"}""",
    "peeku_click"           => """{"selector":"window"}""",
    "peeku_invoke"          => """{"selector":"window"}""",
    "peeku_set_value"       => """{"selector":"window"}""",
    "peeku_type"            => """{"selector":"window"}""",
    "peeku_scroll"          => """{"selector":"window"}""",

    // Require both selector and target.
    "peeku_wait"            => """{"selector":"window","target":"desktop"}""",

    _ => "{}",
  };

  /// <summary>
  /// Naive snake_case → PascalCase + "Async" for a peeku_* tool name.
  /// Strips the leading "peeku_" prefix, then PascalCases each segment.
  /// </summary>
  private static string SnakeToPascalAsync(string toolName)
  {
    // Strip leading "peeku_"
    var withoutPrefix = toolName.StartsWith("peeku_", StringComparison.Ordinal)
      ? toolName["peeku_".Length..]
      : toolName;

    var parts = withoutPrefix.Split('_');
    var pascal = string.Concat(parts.Select(p =>
      p.Length == 0 ? p : char.ToUpperInvariant(p[0]) + p[1..]));

    return pascal + "Async";
  }
}

// ────────────────────────────────────────────────────────────────────────────
// Fake IPeekuClient that throws NotImplementedException on every call.
// When a BatchRunner arm correctly dispatches to it, the exception propagates
// to BatchRunner's catch(Exception) and becomes Error.Code = "Internal".
// Only the default/missing arm produces Error.Code = "NotSupported".
// ────────────────────────────────────────────────────────────────────────────

file sealed class ThrowingFakeClient : IPeekuClient
{
  public Task<DoctorResult> DoctorAsync(DoctorRequest req, CancellationToken ct = default)
    => Task.FromException<DoctorResult>(new NotImplementedException(nameof(DoctorAsync)));

  public Task<WindowListResult> WindowsListAsync(WindowsListRequest req, CancellationToken ct = default)
    => Task.FromException<WindowListResult>(new NotImplementedException(nameof(WindowsListAsync)));

  public Task<FocusedWindowResult> WindowsFocusedAsync(CancellationToken ct = default)
    => Task.FromException<FocusedWindowResult>(new NotImplementedException(nameof(WindowsFocusedAsync)));

  public Task<FocusedWindowResult> WindowFocusAsync(WindowFocusRequest req, CancellationToken ct = default)
    => Task.FromException<FocusedWindowResult>(new NotImplementedException(nameof(WindowFocusAsync)));

  public Task<CaptureImageResult> CaptureImageAsync(CaptureImageRequest req, CancellationToken ct = default)
    => Task.FromException<CaptureImageResult>(new NotImplementedException(nameof(CaptureImageAsync)));

  public Task<UiaSnapshotResult> UiaSnapshotAsync(UiaSnapshotRequest req, CancellationToken ct = default)
    => Task.FromException<UiaSnapshotResult>(new NotImplementedException(nameof(UiaSnapshotAsync)));

  public Task<SeeResult> SeeAsync(SeeRequest req, CancellationToken ct = default)
    => Task.FromException<SeeResult>(new NotImplementedException(nameof(SeeAsync)));

  public Task<FindResult> FindAsync(FindRequest req, CancellationToken ct = default)
    => Task.FromException<FindResult>(new NotImplementedException(nameof(FindAsync)));

  public Task<ElementGetResult> ElementGetAsync(ElementGetRequest req, CancellationToken ct = default)
    => Task.FromException<ElementGetResult>(new NotImplementedException(nameof(ElementGetAsync)));

  public Task<ActionResult> ClickAsync(ClickRequest req, CancellationToken ct = default)
    => Task.FromException<ActionResult>(new NotImplementedException(nameof(ClickAsync)));

  public Task<ActionResult> InvokeAsync(InvokeRequest req, CancellationToken ct = default)
    => Task.FromException<ActionResult>(new NotImplementedException(nameof(InvokeAsync)));

  public Task<ActionResult> SetValueAsync(SetValueRequest req, CancellationToken ct = default)
    => Task.FromException<ActionResult>(new NotImplementedException(nameof(SetValueAsync)));

  public Task<ActionResult> TypeAsync(TypeRequest req, CancellationToken ct = default)
    => Task.FromException<ActionResult>(new NotImplementedException(nameof(TypeAsync)));

  public Task<ActionResult> ScrollAsync(ScrollRequest req, CancellationToken ct = default)
    => Task.FromException<ActionResult>(new NotImplementedException(nameof(ScrollAsync)));

  public Task<ActionResult> HotkeyAsync(HotkeyRequest req, CancellationToken ct = default)
    => Task.FromException<ActionResult>(new NotImplementedException(nameof(HotkeyAsync)));

  public Task<ActionResult> PressAsync(PressRequest req, CancellationToken ct = default)
    => Task.FromException<ActionResult>(new NotImplementedException(nameof(PressAsync)));

  public IAsyncEnumerable<ObservationEvent> ObserveAsync(ObserveRequest req, CancellationToken ct = default)
    => ThrowAsyncEnumerable(ct);

  public Task<WaitResult> WaitAsync(WaitRequest req, CancellationToken ct = default)
    => Task.FromException<WaitResult>(new NotImplementedException(nameof(WaitAsync)));

  public Task<BatchResult> BatchAsync(BatchRequest req, CancellationToken ct = default)
    => Task.FromException<BatchResult>(new NotImplementedException(nameof(BatchAsync)));

#pragma warning disable CS1998 // async method lacks await — intentional (throws before any yield)
  private static async IAsyncEnumerable<ObservationEvent> ThrowAsyncEnumerable(
    [EnumeratorCancellation] CancellationToken ct = default)
  {
    throw new NotImplementedException("ThrowingFakeClient.ObserveAsync intentionally throws.");
    yield break; // unreachable; required for iterator state-machine generation
  }
#pragma warning restore CS1998
}
