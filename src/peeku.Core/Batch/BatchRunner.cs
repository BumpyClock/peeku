using System.Diagnostics;
using System.Text.Json;

namespace peeku;

internal static class BatchRunner
{
  internal static async Task<BatchResult> RunAsync(IPeekuClient client, BatchRequest req, CancellationToken ct)
  {
    var scope = Results.Start();

    try
    {
      ct.ThrowIfCancellationRequested();

      if (client is null)
      {
        throw new ArgumentNullException(nameof(client));
      }

      if (req is null)
      {
        return new BatchResult(
          Ok: false,
          Meta: scope.Meta(),
          Results: Array.Empty<BatchStepResult>(),
          Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Request is required."));
      }

      if (req.Ops is null || req.Ops.Count == 0)
      {
        return new BatchResult(
          Ok: true,
          Meta: scope.Meta(),
          Results: Array.Empty<BatchStepResult>());
      }

      var results = new List<BatchStepResult>(capacity: Math.Clamp(req.Ops.Count, 0, 256));

      for (var i = 0; i < req.Ops.Count; i++)
      {
        ct.ThrowIfCancellationRequested();

        var op = req.Ops[i];
        if (string.IsNullOrWhiteSpace(op.Tool))
        {
          results.Add(new BatchStepResult(
            Tool: "",
            Ok: false,
            DurationMs: 0,
            Result: null,
            Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Batch op tool is required.", new { index = i })));

          if (req.StopOnError)
          {
            break;
          }

          continue;
        }

        var sw = Stopwatch.StartNew();
        var (ok, result, error) = await DispatchAsync(client, op.Tool.Trim(), op.Args, ct).ConfigureAwait(false);
        sw.Stop();

        results.Add(new BatchStepResult(
          Tool: op.Tool.Trim(),
          Ok: ok,
          DurationMs: (int)Math.Clamp(sw.ElapsedMilliseconds, 0, int.MaxValue),
          Result: result,
          Error: ok ? null : error));

        if (!ok && req.StopOnError)
        {
          break;
        }
      }

      var allOk = results.All(r => r.Ok);
      return new BatchResult(
        Ok: allOk,
        Meta: scope.Meta(),
        Results: results,
        Error: allOk ? null : PeekuErrors.Create(PeekuErrorCode.Internal, "One or more batch steps failed."));
    }
    catch (OperationCanceledException)
    {
      return new BatchResult(
        Ok: false,
        Meta: scope.Meta(),
        Results: Array.Empty<BatchStepResult>(),
        Error: PeekuErrors.Create(PeekuErrorCode.Canceled, "Operation cancelled."));
    }
    catch (Exception ex)
    {
      return new BatchResult(
        Ok: false,
        Meta: scope.Meta(),
        Results: Array.Empty<BatchStepResult>(),
        Error: PeekuErrors.Create(
          PeekuErrorCode.Internal,
          "Batch failed.",
          new { exception = ex.GetType().FullName, ex.Message, ex.HResult }));
    }
  }

  private static async Task<(bool Ok, object? Result, PeekuError? Error)> DispatchAsync(
    IPeekuClient client,
    string tool,
    JsonElement args,
    CancellationToken ct)
  {
    try
    {
      switch (tool)
      {
        case "peeku_windows_list":
        {
          var req = new WindowsListRequest(
            TitleContains: ReadString(args, "titleContains"),
            ProcessName: ReadString(args, "processName"),
            Limit: ReadInt(args, "limit") ?? 50);

          var res = await client.WindowsListAsync(req, ct).ConfigureAwait(false);
          return (res.Ok, res, res.Error);
        }

        case "peeku_windows_focused":
        {
          var res = await client.WindowsFocusedAsync(ct).ConfigureAwait(false);
          return (res.Ok, res, res.Error);
        }

        case "peeku_capture_image":
        {
          if (!TryReadTarget(args, out var target, out var targetError))
          {
            return (false, null, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, targetError ?? "Invalid target."));
          }

          var req = new CaptureImageRequest(
            Target: target,
            OutPath: ReadString(args, "out") ?? ReadString(args, "outPath"),
            IncludeBase64: ReadBool(args, "includeBase64") ?? false);

          var res = await client.CaptureImageAsync(req, ct).ConfigureAwait(false);
          return (res.Ok, res, res.Error);
        }

        case "peeku_uia_snapshot":
        {
          if (!TryReadTarget(args, out var target, out var targetError))
          {
            return (false, null, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, targetError ?? "Invalid target."));
          }

          var req = new UiaSnapshotRequest(
            Target: target,
            Depth: ReadInt(args, "depth") ?? 6,
            MaxNodes: ReadInt(args, "maxNodes") ?? 5000,
            IncludeProperties: ReadUiaPropertiesMode(args, "includeProperties") ?? UiaPropertiesMode.Basic);

          var res = await client.UiaSnapshotAsync(req, ct).ConfigureAwait(false);
          return (res.Ok, res, res.Error);
        }

        case "peeku_see":
        {
          if (!TryReadTarget(args, out var target, out var targetError))
          {
            return (false, null, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, targetError ?? "Invalid target."));
          }

          var req = new SeeRequest(
            Target: target,
            Depth: ReadInt(args, "depth") ?? 6,
            MaxNodes: ReadInt(args, "maxNodes") ?? 5000,
            IncludeBase64: ReadBool(args, "includeBase64") ?? false,
            IncludeProperties: ReadUiaPropertiesMode(args, "includeProperties") ?? UiaPropertiesMode.Basic);

          var res = await client.SeeAsync(req, ct).ConfigureAwait(false);
          return (res.Ok, res, res.Error);
        }

        case "peeku_find":
        {
          if (!TryReadSelector(args, out var selector, out var selectorError))
          {
            return (false, null, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, selectorError ?? "Invalid selector."));
          }

          var target = TryReadTarget(args, out var t, out _) ? t : null;
          var req = new FindRequest(
            Selector: selector,
            Target: target,
            Limit: ReadInt(args, "limit") ?? 20);

          var res = await client.FindAsync(req, ct).ConfigureAwait(false);
          return (res.Ok, res, res.Error);
        }

        case "peeku_element_get":
        {
          var hasElement = TryReadElementRef(args, "elementRef", out var element, out _);
          var hasSelector = TryReadSelector(args, out var selector, out _);

          if (hasElement == hasSelector)
          {
            return (false, null, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Provide exactly one of elementRef or selector."));
          }

          var target = TryReadTarget(args, out var t, out _) ? t : null;
          var req = new ElementGetRequest(
            Element: hasElement ? element : null,
            Selector: hasSelector ? selector : null,
            Target: target,
            IncludeProperties: ReadUiaPropertiesMode(args, "includeProperties") ?? UiaPropertiesMode.All);

          var res = await client.ElementGetAsync(req, ct).ConfigureAwait(false);
          return (res.Ok, res, res.Error);
        }

        case "peeku_click":
        {
          var (selOk, element, selector, target, error) = ReadSelection(args);
          if (!selOk)
          {
            return (false, null, error);
          }

          var method = ReadActionMethod(args, "method") ?? ActionMethod.Auto;
          var req = new ClickRequest(element, selector, target, method);
          var res = await client.ClickAsync(req, ct).ConfigureAwait(false);
          return (res.Ok, res, res.Error);
        }

        case "peeku_invoke":
        {
          var (selOk, element, selector, target, error) = ReadSelection(args);
          if (!selOk)
          {
            return (false, null, error);
          }

          var req = new InvokeRequest(element, selector, target);
          var res = await client.InvokeAsync(req, ct).ConfigureAwait(false);
          return (res.Ok, res, res.Error);
        }

        case "peeku_set_value":
        {
          var value = ReadString(args, "value") ?? "";
          var (selOk, element, selector, target, error) = ReadSelection(args);
          if (!selOk)
          {
            return (false, null, error);
          }

          var req = new SetValueRequest(element, selector, target, value);
          var res = await client.SetValueAsync(req, ct).ConfigureAwait(false);
          return (res.Ok, res, res.Error);
        }

        case "peeku_type":
        {
          var text = ReadString(args, "text") ?? "";
          var append = ReadBool(args, "append") ?? true;
          var delayMs = ReadInt(args, "delayMs");
          var (selOk, element, selector, target, error) = ReadSelection(args);
          if (!selOk)
          {
            return (false, null, error);
          }

          var req = new TypeRequest(element, selector, target, text, append, delayMs);
          var res = await client.TypeAsync(req, ct).ConfigureAwait(false);
          return (res.Ok, res, res.Error);
        }

        case "peeku_scroll":
        {
          var direction = ReadScrollDirection(args, "direction") ?? ScrollDirection.Vertical;
          var delta = ReadInt(args, "delta");
          var lines = ReadInt(args, "lines");
          var (selOk, element, selector, target, error) = ReadSelection(args);
          if (!selOk)
          {
            return (false, null, error);
          }

          var req = new ScrollRequest(element, selector, target, delta, lines, direction);
          var res = await client.ScrollAsync(req, ct).ConfigureAwait(false);
          return (res.Ok, res, res.Error);
        }

        case "peeku_hotkey":
        {
          var keys = ReadString(args, "keys") ?? "";
          var res = await client.HotkeyAsync(new HotkeyRequest(keys), ct).ConfigureAwait(false);
          return (res.Ok, res, res.Error);
        }

        case "peeku_wait":
        {
          if (!TryReadSelector(args, out var selector, out var selectorError))
          {
            return (false, null, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, selectorError ?? "Invalid selector."));
          }

          if (!TryReadTarget(args, out var target, out var targetError))
          {
            return (false, null, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, targetError ?? "Invalid target."));
          }

          var timeoutMs = ReadInt(args, "timeoutMs") ?? 10_000;
          var req = new WaitRequest(selector, target, TimeSpan.FromMilliseconds(Math.Max(0, timeoutMs)));
          var res = await client.WaitAsync(req, ct).ConfigureAwait(false);
          return (res.Ok, res, res.Error);
        }

        case "peeku_observe":
        {
          if (!TryReadTarget(args, out var target, out var targetError))
          {
            return (false, null, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, targetError ?? "Invalid target."));
          }

          var durationMs = ReadInt(args, "durationMs") ?? 10_000;
          var maxEvents = ReadInt(args, "maxEvents") ?? 200;
          var eventsSet = ReadObserveEventSet(args, "events") ?? ObserveEventSet.All;

          var req = new ObserveRequest(
            Target: target,
            Events: eventsSet,
            Duration: TimeSpan.FromMilliseconds(Math.Max(0, durationMs)),
            MaxEvents: Math.Max(0, maxEvents));

          var list = new List<ObservationEvent>(capacity: Math.Clamp(maxEvents, 0, 512));
          await foreach (var ev in client.ObserveAsync(req, ct).ConfigureAwait(false))
          {
            list.Add(ev);
            if (list.Count >= maxEvents)
            {
              break;
            }
          }

          return (true, list, null);
        }

        default:
          return (false, null, PeekuErrors.Create(PeekuErrorCode.NotSupported, "Unknown tool.", new { tool }));
      }
    }
    catch (OperationCanceledException)
    {
      return (false, null, PeekuErrors.Create(PeekuErrorCode.Canceled, "Operation cancelled."));
    }
    catch (Exception ex)
    {
      return (false, null, PeekuErrors.Create(PeekuErrorCode.Internal, "Batch step failed.", new { tool, exception = ex.GetType().FullName, ex.Message, ex.HResult }));
    }
  }

  private static (bool Ok, ElementRef? Element, Selector? Selector, Target? Target, PeekuError? Error) ReadSelection(JsonElement args)
  {
    var hasElement = TryReadElementRef(args, "elementRef", out var element, out _);
    var hasSelector = TryReadSelector(args, out var selector, out _);

    if (hasElement == hasSelector)
    {
      return (
        Ok: false,
        Element: null,
        Selector: null,
        Target: null,
        Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Provide exactly one of elementRef or selector."));
    }

    var target = TryReadTarget(args, out var t, out _) ? t : null;
    return (true, hasElement ? element : null, hasSelector ? selector : null, target, null);
  }

  private static string? ReadString(JsonElement obj, string name)
  {
    if (obj.ValueKind != JsonValueKind.Object)
    {
      return null;
    }

    foreach (var p in obj.EnumerateObject())
    {
      if (!string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      if (p.Value.ValueKind == JsonValueKind.String)
      {
        return p.Value.GetString();
      }

      return null;
    }

    return null;
  }

  private static int? ReadInt(JsonElement obj, string name)
  {
    if (obj.ValueKind != JsonValueKind.Object)
    {
      return null;
    }

    foreach (var p in obj.EnumerateObject())
    {
      if (!string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out var v))
      {
        return v;
      }

      return null;
    }

    return null;
  }

  private static bool? ReadBool(JsonElement obj, string name)
  {
    if (obj.ValueKind != JsonValueKind.Object)
    {
      return null;
    }

    foreach (var p in obj.EnumerateObject())
    {
      if (!string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      if (p.Value.ValueKind == JsonValueKind.True) return true;
      if (p.Value.ValueKind == JsonValueKind.False) return false;
      return null;
    }

    return null;
  }

  private static bool TryReadSelector(JsonElement args, out Selector selector, out string? error)
  {
    selector = new Selector("");
    error = null;

    if (args.ValueKind != JsonValueKind.Object)
    {
      error = "Args must be an object.";
      return false;
    }

    if (!TryGet(args, "selector", out var selEl))
    {
      error = "Selector is required.";
      return false;
    }

    if (selEl.ValueKind == JsonValueKind.String)
    {
      var expr = selEl.GetString() ?? "";
      if (string.IsNullOrWhiteSpace(expr))
      {
        error = "Selector expr is required.";
        return false;
      }

      selector = new Selector(expr);
      return true;
    }

    if (selEl.ValueKind != JsonValueKind.Object || !TryGet(selEl, "expr", out var exprEl) || exprEl.ValueKind != JsonValueKind.String)
    {
      error = "Selector must be an object with expr.";
      return false;
    }

    var expr2 = exprEl.GetString() ?? "";
    if (string.IsNullOrWhiteSpace(expr2))
    {
      error = "Selector expr is required.";
      return false;
    }

    selector = new Selector(expr2);
    return true;
  }

  private static bool TryReadElementRef(JsonElement args, string prop, out ElementRef element, out string? error)
  {
    element = new ElementRef("");
    error = null;

    if (args.ValueKind != JsonValueKind.Object)
    {
      error = "Args must be an object.";
      return false;
    }

    if (!TryGet(args, prop, out var el))
    {
      error = "ElementRef is required.";
      return false;
    }

    if (el.ValueKind == JsonValueKind.String)
    {
      var refId = el.GetString() ?? "";
      if (string.IsNullOrWhiteSpace(refId))
      {
        error = "ElementRef refId is required.";
        return false;
      }

      element = new ElementRef(refId);
      return true;
    }

    if (el.ValueKind != JsonValueKind.Object || !TryGet(el, "refId", out var refIdEl) || refIdEl.ValueKind != JsonValueKind.String)
    {
      error = "ElementRef must be an object with refId.";
      return false;
    }

    var refId2 = refIdEl.GetString() ?? "";
    if (string.IsNullOrWhiteSpace(refId2))
    {
      error = "ElementRef refId is required.";
      return false;
    }

    element = new ElementRef(refId2);
    return true;
  }

  private static bool TryReadTarget(JsonElement args, out Target target, out string? error)
  {
    target = new Target.FocusedWindow();
    error = null;

    if (args.ValueKind != JsonValueKind.Object)
    {
      error = "Args must be an object.";
      return false;
    }

    if (!TryGet(args, "target", out var t))
    {
      error = "Target is required.";
      return false;
    }

    if (t.ValueKind == JsonValueKind.String)
    {
      return TryTargetFromKind(t.GetString() ?? "", out target, out error);
    }

    if (t.ValueKind != JsonValueKind.Object)
    {
      error = "Target must be an object.";
      return false;
    }

    if (TryGet(t, "kind", out var kindEl) && kindEl.ValueKind == JsonValueKind.String)
    {
      var kind = kindEl.GetString() ?? "";
      if (!TryTargetFromKind(kind, out target, out error, t))
      {
        return false;
      }

      return true;
    }

    // { desktop: {} } etc
    foreach (var p in t.EnumerateObject())
    {
      var key = p.Name.Trim();
      if (TryTargetFromKind(key, out target, out error, p.Value))
      {
        return true;
      }
    }

    error = "Unknown target shape.";
    return false;
  }

  private static bool TryTargetFromKind(string kindRaw, out Target target, out string? error, JsonElement? obj = null)
  {
    target = new Target.FocusedWindow();
    error = null;

    var kind = (kindRaw ?? "").Trim().ToLowerInvariant();
    switch (kind)
    {
      case "desktop":
        target = new Target.Desktop();
        return true;

      case "focused":
      case "focusedwindow":
      case "focused_window":
        target = new Target.FocusedWindow();
        return true;

      case "screen":
      case "display":
        if (obj is null || obj.Value.ValueKind != JsonValueKind.Object)
        {
          error = "Screen target requires screenIndex.";
          return false;
        }

        var idx = ReadInt(obj.Value, "screenIndex") ?? ReadInt(obj.Value, "index") ?? 0;
        target = new Target.Screen(idx);
        return true;

      case "hwnd":
      case "windowbyhwnd":
        if (obj is null || obj.Value.ValueKind != JsonValueKind.Object)
        {
          error = "Hwnd target requires hwndHex.";
          return false;
        }

        var hwnd = ReadString(obj.Value, "hwndHex") ?? ReadString(obj.Value, "hwnd") ?? "";
        if (string.IsNullOrWhiteSpace(hwnd))
        {
          error = "HwndHex is required.";
          return false;
        }

        target = new Target.WindowByHwnd(hwnd);
        return true;

      case "query":
      case "windowbyquery":
        if (obj is null || obj.Value.ValueKind != JsonValueKind.Object)
        {
          error = "Query target requires query object.";
          return false;
        }

        var qObj = obj.Value;
        if (TryGet(qObj, "query", out var inner) && inner.ValueKind == JsonValueKind.Object)
        {
          qObj = inner;
        }

        var title = ReadString(qObj, "titleContains");
        var proc = ReadString(qObj, "processName");
        var pid = ReadInt(qObj, "processId");

        target = new Target.WindowByQuery(new WindowQuery(title, proc, pid));
        return true;

      default:
        error = $"Unknown target kind: '{kindRaw}'.";
        return false;
    }
  }

  private static bool TryGet(JsonElement obj, string name, out JsonElement value)
  {
    value = default;
    if (obj.ValueKind != JsonValueKind.Object)
    {
      return false;
    }

    foreach (var p in obj.EnumerateObject())
    {
      if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
      {
        value = p.Value;
        return true;
      }
    }

    return false;
  }

  private static UiaPropertiesMode? ReadUiaPropertiesMode(JsonElement obj, string name)
  {
    var s = ReadString(obj, name);
    if (string.IsNullOrWhiteSpace(s))
    {
      return null;
    }

    return s.Trim().ToLowerInvariant() switch
    {
      "basic" => UiaPropertiesMode.Basic,
      "all" => UiaPropertiesMode.All,
      _ => null,
    };
  }

  private static ActionMethod? ReadActionMethod(JsonElement obj, string name)
  {
    var s = ReadString(obj, name);
    if (string.IsNullOrWhiteSpace(s))
    {
      return null;
    }

    return s.Trim().ToLowerInvariant() switch
    {
      "auto" => ActionMethod.Auto,
      "uia" => ActionMethod.Uia,
      "input" => ActionMethod.Input,
      _ => null,
    };
  }

  private static ScrollDirection? ReadScrollDirection(JsonElement obj, string name)
  {
    var s = ReadString(obj, name);
    if (string.IsNullOrWhiteSpace(s))
    {
      return null;
    }

    return s.Trim().ToLowerInvariant() switch
    {
      "vertical" => ScrollDirection.Vertical,
      "horizontal" => ScrollDirection.Horizontal,
      _ => null,
    };
  }

  private static ObserveEventSet? ReadObserveEventSet(JsonElement obj, string name)
  {
    if (obj.ValueKind != JsonValueKind.Object)
    {
      return null;
    }

    if (!TryGet(obj, name, out var e))
    {
      return null;
    }

    if (e.ValueKind != JsonValueKind.Array)
    {
      return null;
    }

    var hasFocus = false;
    var hasOther = false;
    foreach (var item in e.EnumerateArray())
    {
      if (item.ValueKind != JsonValueKind.String)
      {
        continue;
      }

      var s = (item.GetString() ?? "").Trim().ToLowerInvariant();
      if (s == "focus") hasFocus = true;
      else if (s is "structure" or "property") hasOther = true;
    }

    if (hasOther && hasFocus) return ObserveEventSet.All;
    if (hasOther) return ObserveEventSet.Structure;
    if (hasFocus) return ObserveEventSet.Focus;
    return null;
  }
}
