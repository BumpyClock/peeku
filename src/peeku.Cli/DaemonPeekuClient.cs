using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using peeku;

namespace peeku.Cli;

/// <summary>
/// IPeekuClient implementation that forwards calls to a daemon via JSON-RPC batch.
/// Example: <code>var client = new DaemonPeekuClient(rpcClient);</code>
/// </summary>
internal sealed class DaemonPeekuClient : IPeekuClient
{
  private readonly IDaemonJsonRpcClient _rpc;
  private readonly JsonSerializerOptions _jsonOptions;

  internal DaemonPeekuClient(IDaemonJsonRpcClient rpc)
  {
    _rpc = rpc ?? throw new ArgumentNullException(nameof(rpc));
    _jsonOptions = new JsonSerializerOptions
    {
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
      PropertyNameCaseInsensitive = true,
      DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
      WriteIndented = false,
    };
  }

  public Task<DoctorResult> DoctorAsync(DoctorRequest req, CancellationToken ct = default)
  {
    var args = new Dictionary<string, object?>
    {
      ["deep"] = req.Deep,
    };

    return CallResultAsync(
      "peeku_doctor",
      args,
      (meta, error) => new DoctorResult(false, meta, Array.Empty<DoctorCheck>(), error),
      ct);
  }

  public Task<WindowListResult> WindowsListAsync(WindowsListRequest req, CancellationToken ct = default)
  {
    var args = new Dictionary<string, object?>
    {
      ["limit"] = req.Limit,
    };

    if (!string.IsNullOrWhiteSpace(req.TitleContains))
    {
      args["titleContains"] = req.TitleContains;
    }

    if (!string.IsNullOrWhiteSpace(req.ProcessName))
    {
      args["processName"] = req.ProcessName;
    }

    return CallResultAsync(
      "peeku_windows_list",
      args,
      (meta, error) => new WindowListResult(false, meta, Array.Empty<WindowInfo>(), error),
      ct);
  }

  public Task<FocusedWindowResult> WindowsFocusedAsync(CancellationToken ct = default)
    => CallResultAsync(
      "peeku_windows_focused",
      new Dictionary<string, object?>(),
      (meta, error) => new FocusedWindowResult(false, meta, null, error),
      ct);

  public Task<FocusedWindowResult> WindowFocusAsync(WindowFocusRequest req, CancellationToken ct = default)
  {
    var args = new Dictionary<string, object?>
    {
      ["target"] = BuildTarget(req.Target),
    };

    return CallResultAsync(
      "peeku_windows_focus",
      args,
      (meta, error) => new FocusedWindowResult(false, meta, null, error),
      ct);
  }

  public Task<CaptureImageResult> CaptureImageAsync(CaptureImageRequest req, CancellationToken ct = default)
  {
    var args = new Dictionary<string, object?>
    {
      ["target"] = BuildTarget(req.Target),
      ["includeBase64"] = req.IncludeBase64,
    };

    if (!string.IsNullOrWhiteSpace(req.OutPath))
    {
      args["outPath"] = req.OutPath;
    }

    return CallResultAsync(
      "peeku_capture_image",
      args,
      (meta, error) => new CaptureImageResult(false, meta, "", "", 0, 0, null, error),
      ct);
  }

  public Task<UiaSnapshotResult> UiaSnapshotAsync(UiaSnapshotRequest req, CancellationToken ct = default)
  {
    var args = new Dictionary<string, object?>
    {
      ["target"] = BuildTarget(req.Target),
      ["depth"] = req.Depth,
      ["maxNodes"] = req.MaxNodes,
      ["includeProperties"] = UiaModeString(req.IncludeProperties),
    };

    return CallResultAsync(
      "peeku_uia_snapshot",
      args,
      (meta, error) => new UiaSnapshotResult(false, meta, "", new UiaNode(new ElementRef("")), Array.Empty<UiaElement>(), error),
      ct);
  }

  public Task<SeeResult> SeeAsync(SeeRequest req, CancellationToken ct = default)
  {
    var args = new Dictionary<string, object?>
    {
      ["target"] = BuildTarget(req.Target),
      ["depth"] = req.Depth,
      ["maxNodes"] = req.MaxNodes,
      ["includeBase64"] = req.IncludeBase64,
      ["includeProperties"] = UiaModeString(req.IncludeProperties),
    };

    return CallResultAsync(
      "peeku_see",
      args,
      (meta, error) => new SeeResult(false, meta, new CaptureImageResult(false, meta, "", "", 0, 0, null, error), "", Array.Empty<UiaElement>(), error),
      ct);
  }

  public Task<FindResult> FindAsync(FindRequest req, CancellationToken ct = default)
  {
    var selectorArgs = new Dictionary<string, object?> { ["expr"] = req.Selector.Expr };
    if (!req.Selector.PreferCachedSnapshot)
    {
      selectorArgs["preferCachedSnapshot"] = false;
    }

    var args = new Dictionary<string, object?>
    {
      ["selector"] = selectorArgs,
      ["limit"] = req.Limit,
    };

    if (req.Target is not null)
    {
      args["target"] = BuildTarget(req.Target);
    }

    return CallResultAsync(
      "peeku_find",
      args,
      (meta, error) => new FindResult(false, meta, Array.Empty<FindMatch>(), error),
      ct);
  }

  public Task<ElementGetResult> ElementGetAsync(ElementGetRequest req, CancellationToken ct = default)
  {
    var args = new Dictionary<string, object?>
    {
      ["includeProperties"] = UiaModeString(req.IncludeProperties),
    };

    if (req.Element is not null)
    {
      var elementRef = new Dictionary<string, object?>
      {
        ["refId"] = req.Element.RefId,
      };

      if (!string.IsNullOrWhiteSpace(req.Element.SnapshotId))
      {
        elementRef["snapshotId"] = req.Element.SnapshotId;
      }

      args["elementRef"] = elementRef;
    }

    if (req.Selector is not null)
    {
      var selectorArgs = new Dictionary<string, object?> { ["expr"] = req.Selector.Expr };
      if (!req.Selector.PreferCachedSnapshot)
      {
        selectorArgs["preferCachedSnapshot"] = false;
      }

      args["selector"] = selectorArgs;
    }

    if (req.Target is not null)
    {
      args["target"] = BuildTarget(req.Target);
    }

    return CallResultAsync(
      "peeku_element_get",
      args,
      (meta, error) => new ElementGetResult(false, meta, new UiaElement(new ElementRef("")), new Dictionary<string, object?>(), Array.Empty<string>(), null, error),
      ct);
  }

  public Task<ActionResult> ClickAsync(ClickRequest req, CancellationToken ct = default)
  {
    var args = BuildSelectionArgs(req.Element, req.Selector, req.Target);
    args["method"] = ActionMethodString(req.Method);
    args["foreground"] = req.Foreground;
    return CallResultAsync(
      "peeku_click",
      args,
      (meta, error) => new ActionResult(false, meta, null, error),
      ct);
  }

  public Task<ActionResult> InvokeAsync(InvokeRequest req, CancellationToken ct = default)
  {
    var args = BuildSelectionArgs(req.Element, req.Selector, req.Target);
    return CallResultAsync(
      "peeku_invoke",
      args,
      (meta, error) => new ActionResult(false, meta, null, error),
      ct);
  }

  public Task<ActionResult> SetValueAsync(SetValueRequest req, CancellationToken ct = default)
  {
    var args = BuildSelectionArgs(req.Element, req.Selector, req.Target);
    args["value"] = req.Value;
    return CallResultAsync(
      "peeku_set_value",
      args,
      (meta, error) => new ActionResult(false, meta, null, error),
      ct);
  }

  public Task<ActionResult> TypeAsync(TypeRequest req, CancellationToken ct = default)
  {
    var args = BuildSelectionArgs(req.Element, req.Selector, req.Target);
    args["text"] = req.Text;
    args["append"] = req.Append;
    if (req.DelayMs.HasValue)
    {
      args["delayMs"] = req.DelayMs.Value;
    }

    args["method"] = ActionMethodString(req.Method);
    args["foreground"] = req.Foreground;

    return CallResultAsync(
      "peeku_type",
      args,
      (meta, error) => new ActionResult(false, meta, null, error),
      ct);
  }

  public Task<ActionResult> ScrollAsync(ScrollRequest req, CancellationToken ct = default)
  {
    var args = BuildSelectionArgs(req.Element, req.Selector, req.Target);
    args["direction"] = ScrollDirectionString(req.Direction);
    if (req.Delta.HasValue)
    {
      args["delta"] = req.Delta.Value;
    }

    if (req.Lines.HasValue)
    {
      args["lines"] = req.Lines.Value;
    }

    return CallResultAsync(
      "peeku_scroll",
      args,
      (meta, error) => new ActionResult(false, meta, null, error),
      ct);
  }

  public Task<ActionResult> HotkeyAsync(HotkeyRequest req, CancellationToken ct = default)
  {
    var args = new Dictionary<string, object?>
    {
      ["keys"] = req.Keys,
    };

    return CallResultAsync(
      "peeku_hotkey",
      args,
      (meta, error) => new ActionResult(false, meta, null, error),
      ct);
  }

  public Task<ActionResult> PressAsync(PressRequest req, CancellationToken ct = default)
  {
    var args = new Dictionary<string, object?>
    {
      ["keys"] = req.Keys,
      ["count"] = req.Count,
    };

    if (req.DelayMs.HasValue)
    {
      args["delayMs"] = req.DelayMs.Value;
    }

    if (req.HoldMs.HasValue)
    {
      args["holdMs"] = req.HoldMs.Value;
    }

    if (req.Target is not null)
    {
      args["target"] = BuildTarget(req.Target);
    }

    return CallResultAsync(
      "peeku_press",
      args,
      (meta, error) => new ActionResult(false, meta, null, error),
      ct);
  }

  public async IAsyncEnumerable<ObservationEvent> ObserveAsync(ObserveRequest req, [EnumeratorCancellation] CancellationToken ct = default)
  {
    var events = await CallEventsAsync(req, ct).ConfigureAwait(false);
    foreach (var ev in events)
    {
      ct.ThrowIfCancellationRequested();
      yield return ev;
    }
  }

  public Task<WaitResult> WaitAsync(WaitRequest req, CancellationToken ct = default)
  {
    var selectorArgs = new Dictionary<string, object?> { ["expr"] = req.Selector.Expr };
    if (!req.Selector.PreferCachedSnapshot)
    {
      selectorArgs["preferCachedSnapshot"] = false;
    }

    var args = new Dictionary<string, object?>
    {
      ["selector"] = selectorArgs,
      ["target"] = BuildTarget(req.Target),
      ["timeoutMs"] = (int)Math.Max(0, Math.Min(int.MaxValue, req.Timeout.TotalMilliseconds)),
    };

    if (req.Condition != WaitCondition.Exists)
    {
      args["condition"] = WaitConditionToString(req.Condition);
    }

    if (req.ExpectedValue is not null)
    {
      args["value"] = req.ExpectedValue;
    }

    return CallResultAsync(
      "peeku_wait",
      args,
      (meta, error) => new WaitResult(false, meta, false, null, error),
      ct);
  }

  private static string WaitConditionToString(WaitCondition condition) => condition switch
  {
    WaitCondition.Exists        => "exists",
    WaitCondition.NotExists     => "notExists",
    WaitCondition.Enabled       => "enabled",
    WaitCondition.Disabled      => "disabled",
    WaitCondition.Visible       => "visible",
    WaitCondition.Hidden        => "hidden",
    WaitCondition.Focused       => "focused",
    WaitCondition.ToggleOn      => "toggleOn",
    WaitCondition.ToggleOff     => "toggleOff",
    WaitCondition.Expanded      => "expanded",
    WaitCondition.Collapsed     => "collapsed",
    WaitCondition.Selected      => "selected",
    WaitCondition.NotSelected   => "notSelected",
    WaitCondition.ValueEquals   => "valueEquals",
    WaitCondition.ValueContains => "valueContains",
    WaitCondition.NameEquals    => "nameEquals",
    WaitCondition.NameContains  => "nameContains",
    _ => "exists",
  };

  public Task<BatchResult> BatchAsync(BatchRequest req, CancellationToken ct = default)
    => _rpc.CallAsync<BatchResult>("peeku.batch", req, ct);

  public Task<DiffResult> DiffAsync(DiffRequest req, CancellationToken ct = default)
  {
    var args = new Dictionary<string, object?>
    {
      ["targetBefore"] = BuildTarget(req.TargetBefore),
      ["targetAfter"] = BuildTarget(req.TargetAfter),
      ["depth"] = req.Depth,
      ["maxNodes"] = req.MaxNodes,
      ["includeProperties"] = UiaModeString(req.IncludeProperties),
    };

    return CallResultAsync(
      "peeku_diff",
      args,
      (meta, error) => new DiffResult(false, meta, null, null, null, error),
      ct);
  }

  public Task<ElementAtPointResult> ElementAtPointAsync(ElementAtPointRequest req, CancellationToken ct = default)
  {
    var args = new Dictionary<string, object?>
    {
      ["x"] = req.X,
      ["y"] = req.Y,
      ["includeProperties"] = UiaModeString(req.IncludeProperties),
    };

    if (req.Target is not null)
    {
      args["target"] = BuildTarget(req.Target);
    }

    return CallResultAsync(
      "peeku_element_from_point",
      args,
      (meta, error) => new ElementAtPointResult(false, meta, new UiaElement(new ElementRef("")), Array.Empty<UiaElement>(), error),
      ct);
  }

  public Task<WindowActionResult> WindowMoveAsync(WindowMoveRequest req, CancellationToken ct = default)
  {
    var args = new Dictionary<string, object?>
    {
      ["target"] = BuildTarget(req.Target),
      ["x"] = req.X,
      ["y"] = req.Y,
    };
    return CallResultAsync("peeku_window_move", args, (meta, error) => new WindowActionResult(false, meta, error), ct);
  }

  public Task<WindowActionResult> WindowResizeAsync(WindowResizeRequest req, CancellationToken ct = default)
  {
    var args = new Dictionary<string, object?>
    {
      ["target"] = BuildTarget(req.Target),
      ["width"] = req.Width,
      ["height"] = req.Height,
    };
    return CallResultAsync("peeku_window_resize", args, (meta, error) => new WindowActionResult(false, meta, error), ct);
  }

  public Task<WindowActionResult> WindowSetBoundsAsync(WindowBoundsRequest req, CancellationToken ct = default)
  {
    var args = new Dictionary<string, object?>
    {
      ["target"] = BuildTarget(req.Target),
      ["x"] = req.X,
      ["y"] = req.Y,
      ["width"] = req.Width,
      ["height"] = req.Height,
    };
    return CallResultAsync("peeku_window_set_bounds", args, (meta, error) => new WindowActionResult(false, meta, error), ct);
  }

  public Task<WindowActionResult> WindowMinimizeAsync(WindowStateRequest req, CancellationToken ct = default)
  {
    var args = new Dictionary<string, object?> { ["target"] = BuildTarget(req.Target) };
    return CallResultAsync("peeku_window_minimize", args, (meta, error) => new WindowActionResult(false, meta, error), ct);
  }

  public Task<WindowActionResult> WindowMaximizeAsync(WindowStateRequest req, CancellationToken ct = default)
  {
    var args = new Dictionary<string, object?> { ["target"] = BuildTarget(req.Target) };
    return CallResultAsync("peeku_window_maximize", args, (meta, error) => new WindowActionResult(false, meta, error), ct);
  }

  public Task<WindowActionResult> WindowRestoreAsync(WindowStateRequest req, CancellationToken ct = default)
  {
    var args = new Dictionary<string, object?> { ["target"] = BuildTarget(req.Target) };
    return CallResultAsync("peeku_window_restore", args, (meta, error) => new WindowActionResult(false, meta, error), ct);
  }

  public Task<WindowActionResult> WindowCloseAsync(WindowCloseRequest req, CancellationToken ct = default)
  {
    var args = new Dictionary<string, object?>
    {
      ["target"] = BuildTarget(req.Target),
      ["waitMs"] = req.WaitMs,
    };
    return CallResultAsync("peeku_window_close", args, (meta, error) => new WindowActionResult(false, meta, error), ct);
  }

  public Task<AppLaunchResult> AppLaunchAsync(AppLaunchRequest req, CancellationToken ct = default)
  {
    var args = new Dictionary<string, object?>
    {
      ["target"] = req.Target,
      ["waitUntilReady"] = req.WaitUntilReady,
      ["waitMs"] = req.WaitMs,
      ["noFocus"] = req.NoFocus,
    };
    if (req.Args is not null)
    {
      args["args"] = req.Args;
    }

    return CallResultAsync("peeku_app_launch", args, (meta, error) => new AppLaunchResult(false, meta, Error: error), ct);
  }

  public Task<AppQuitResult> AppQuitAsync(AppQuitRequest req, CancellationToken ct = default)
  {
    var args = new Dictionary<string, object?>
    {
      ["force"] = req.Force,
      ["all"] = req.All,
      ["waitMs"] = req.WaitMs,
    };
    if (req.ProcessId is not null)
    {
      args["processId"] = req.ProcessId.Value;
    }

    if (req.ProcessName is not null)
    {
      args["processName"] = req.ProcessName;
    }

    if (req.Except is not null && req.Except.Count > 0)
    {
      args["except"] = req.Except;
    }

    return CallResultAsync("peeku_app_quit", args, (meta, error) => new AppQuitResult(false, meta, Error: error), ct);
  }

  public Task<AppRelaunchResult> AppRelaunchAsync(AppRelaunchRequest req, CancellationToken ct = default)
  {
    var args = new Dictionary<string, object?>
    {
      ["waitUntilReady"] = req.WaitUntilReady,
      ["waitMs"] = req.WaitMs,
      ["noFocus"] = req.NoFocus,
    };
    if (req.ProcessId is not null)
    {
      args["processId"] = req.ProcessId.Value;
    }

    if (req.ProcessName is not null)
    {
      args["processName"] = req.ProcessName;
    }

    return CallResultAsync("peeku_app_relaunch", args, (meta, error) => new AppRelaunchResult(false, meta, Error: error), ct);
  }

  public Task<AppListResult> AppListAsync(AppListRequest req, CancellationToken ct = default)
  {
    var args = new Dictionary<string, object?>
    {
      ["limit"] = req.Limit,
    };

    return CallResultAsync("peeku_app_list", args, (meta, error) => new AppListResult(false, meta, Apps: Array.Empty<AppInfo>(), Error: error), ct);
  }

  private async Task<T> CallResultAsync<T>(
    string tool,
    object args,
    Func<ResultMeta, PeekuError, T> failureFactory,
    CancellationToken ct)
    where T : ResultBase
  {
    try
    {
      var batch = await CallBatchAsync(tool, args, ct).ConfigureAwait(false);
      return ExtractResult(tool, batch, failureFactory);
    }
    catch (Exception ex)
    {
      var meta = CreateFailureMeta();
      var error = PeekuErrors.Create(
        PeekuErrorCode.Internal,
        "Daemon call failed",
        new { tool, exception = ex.GetType().FullName, ex.Message, ex.HResult });
      return failureFactory(meta, error);
    }
  }

  private async Task<BatchResult> CallBatchAsync(string tool, object args, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(tool))
    {
      throw new ArgumentException("Tool is required", nameof(tool));
    }

    if (args is null)
    {
      throw new ArgumentNullException(nameof(args));
    }

    var argsElement = JsonSerializer.SerializeToElement(args, _jsonOptions);
    var op = new BatchOp(tool.Trim(), argsElement);
    var req = new BatchRequest(new[] { op }, true);
    return await _rpc.CallAsync<BatchResult>("peeku.batch", req, ct).ConfigureAwait(false);
  }

  private T ExtractResult<T>(
    string tool,
    BatchResult batch,
    Func<ResultMeta, PeekuError, T> failureFactory)
    where T : ResultBase
  {
    if (batch.Results.Count == 0)
    {
      var error = PeekuErrors.Create(PeekuErrorCode.Internal, "Daemon batch returned no results", new { tool });
      return failureFactory(batch.Meta, error);
    }

    var step = batch.Results[0];
    if (step.Result is null)
    {
      var error = PeekuErrors.Create(PeekuErrorCode.Internal, "Daemon batch returned empty result", new { tool });
      return failureFactory(batch.Meta, error);
    }

    try
    {
      var result = ParseResult<T>(step.Result);
      return result;
    }
    catch (Exception ex)
    {
      var error = PeekuErrors.Create(
        PeekuErrorCode.Internal,
        "Daemon batch result parse failed",
        new { tool, exception = ex.GetType().FullName, ex.Message, ex.HResult });
      return failureFactory(batch.Meta, error);
    }
  }

  private T ParseResult<T>(object payload) where T : ResultBase
  {
    if (payload is T typed)
    {
      return typed;
    }

    if (payload is JsonElement element)
    {
      var parsed = element.Deserialize<T>(_jsonOptions);
      if (parsed is null)
      {
        throw new InvalidOperationException("Daemon result payload empty");
      }

      return parsed;
    }

    if (payload is JsonDocument doc)
    {
      var parsed = doc.RootElement.Deserialize<T>(_jsonOptions);
      if (parsed is null)
      {
        throw new InvalidOperationException("Daemon result payload empty");
      }

      return parsed;
    }

    throw new InvalidOperationException("Daemon result payload unsupported type");
  }

  private async Task<IReadOnlyList<ObservationEvent>> CallEventsAsync(ObserveRequest req, CancellationToken ct)
  {
    var args = new Dictionary<string, object?>
    {
      ["target"] = BuildTarget(req.Target),
      ["events"] = ObserveEventTokens.Encode(req.Events),
      ["maxEvents"] = req.MaxEvents,
    };

    if (req.Duration.HasValue)
    {
      args["durationMs"] = (int)Math.Max(0, Math.Min(int.MaxValue, req.Duration.Value.TotalMilliseconds));
    }

    var batch = await CallBatchAsync("peeku_observe", args, ct).ConfigureAwait(false);
    if (batch.Results.Count == 0)
    {
      throw new InvalidOperationException("Daemon observe returned no results");
    }

    var step = batch.Results[0];
    if (step.Result is null)
    {
      throw new InvalidOperationException("Daemon observe returned empty result");
    }

    return ParseEvents(step.Result);
  }

  private IReadOnlyList<ObservationEvent> ParseEvents(object payload)
  {
    if (payload is List<ObservationEvent> list)
    {
      return list;
    }

    if (payload is ObservationEvent[] array)
    {
      return array;
    }

    if (payload is JsonElement element)
    {
      var parsed = element.Deserialize<List<ObservationEvent>>(_jsonOptions);
      if (parsed is null)
      {
        throw new InvalidOperationException("Daemon observe payload empty");
      }

      return parsed;
    }

    if (payload is JsonDocument doc)
    {
      var parsed = doc.RootElement.Deserialize<List<ObservationEvent>>(_jsonOptions);
      if (parsed is null)
      {
        throw new InvalidOperationException("Daemon observe payload empty");
      }

      return parsed;
    }

    throw new InvalidOperationException("Daemon observe payload unsupported type");
  }

  private static Dictionary<string, object?> BuildSelectionArgs(ElementRef? element, Selector? selector, Target? target)
  {
    var args = new Dictionary<string, object?>();

    if (element is not null)
    {
      var elementRef = new Dictionary<string, object?>
      {
        ["refId"] = element.RefId,
      };

      if (!string.IsNullOrWhiteSpace(element.SnapshotId))
      {
        elementRef["snapshotId"] = element.SnapshotId;
      }

      args["elementRef"] = elementRef;
    }

    if (selector is not null)
    {
      var selectorArgs = new Dictionary<string, object?> { ["expr"] = selector.Expr };
      if (!selector.PreferCachedSnapshot)
      {
        selectorArgs["preferCachedSnapshot"] = false;
      }

      args["selector"] = selectorArgs;
    }

    if (target is not null)
    {
      args["target"] = BuildTarget(target);
    }

    return args;
  }

  private static object BuildTarget(Target target)
    => target switch
    {
      Target.Desktop => new Dictionary<string, object?> { ["kind"] = "desktop" },
      Target.FocusedWindow => new Dictionary<string, object?> { ["kind"] = "focused" },
      Target.Screen screen => new Dictionary<string, object?> { ["kind"] = "screen", ["screenIndex"] = screen.ScreenIndex },
      Target.WindowByHwnd hwnd => new Dictionary<string, object?> { ["kind"] = "hwnd", ["hwndHex"] = hwnd.HwndHex },
      Target.WindowByQuery query => BuildQueryTarget(query),
      _ => new Dictionary<string, object?> { ["kind"] = "focused" },
    };

  private static string UiaModeString(UiaPropertiesMode mode)
    => mode == UiaPropertiesMode.All ? "all" : "basic";

  private static string ActionMethodString(ActionMethod method)
    => method switch
    {
      ActionMethod.Uia => "uia",
      ActionMethod.Input => "input",
      _ => "auto",
    };

  private static string ScrollDirectionString(ScrollDirection direction)
    => direction == ScrollDirection.Horizontal ? "horizontal" : "vertical";

  private static ResultMeta CreateFailureMeta()
  {
    var traceId = CliContextAccessor.Current.TraceId ?? "";
    return Results.Start(traceId).Meta();
  }

  private static object BuildQueryTarget(Target.WindowByQuery query)
  {
    var details = new Dictionary<string, object?>();
    if (!string.IsNullOrWhiteSpace(query.Query.TitleContains))
    {
      details["titleContains"] = query.Query.TitleContains;
    }

    if (!string.IsNullOrWhiteSpace(query.Query.ProcessName))
    {
      details["processName"] = query.Query.ProcessName;
    }

    if (query.Query.ProcessId.HasValue)
    {
      details["processId"] = query.Query.ProcessId.Value;
    }

    return new Dictionary<string, object?>
    {
      ["kind"] = "query",
      ["query"] = details,
    };
  }
}
