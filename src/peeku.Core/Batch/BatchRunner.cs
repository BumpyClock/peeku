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
            TitleContains: BatchArgs.ReadString(args, "titleContains"),
            ProcessName: BatchArgs.ReadString(args, "processName"),
            Limit: BatchArgs.ReadInt(args, "limit") ?? 50);

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
          if (!BatchArgs.TryReadTarget(args, out var target, out var targetError))
          {
            return (false, null, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, targetError ?? "Invalid target."));
          }

          var req = new CaptureImageRequest(
            Target: target,
            OutPath: BatchArgs.ReadString(args, "out") ?? BatchArgs.ReadString(args, "outPath"),
            IncludeBase64: BatchArgs.ReadBool(args, "includeBase64") ?? false);

          var res = await client.CaptureImageAsync(req, ct).ConfigureAwait(false);
          return (res.Ok, res, res.Error);
        }

        case "peeku_uia_snapshot":
        {
          if (!BatchArgs.TryReadTarget(args, out var target, out var targetError))
          {
            return (false, null, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, targetError ?? "Invalid target."));
          }

          var req = new UiaSnapshotRequest(
            Target: target,
            Depth: BatchArgs.ReadInt(args, "depth") ?? 6,
            MaxNodes: BatchArgs.ReadInt(args, "maxNodes") ?? 5000,
            IncludeProperties: BatchArgs.ReadUiaPropertiesMode(args, "includeProperties") ?? UiaPropertiesMode.Basic);

          var res = await client.UiaSnapshotAsync(req, ct).ConfigureAwait(false);
          return (res.Ok, res, res.Error);
        }

        case "peeku_see":
        {
          if (!BatchArgs.TryReadTarget(args, out var target, out var targetError))
          {
            return (false, null, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, targetError ?? "Invalid target."));
          }

          var req = new SeeRequest(
            Target: target,
            Depth: BatchArgs.ReadInt(args, "depth") ?? 6,
            MaxNodes: BatchArgs.ReadInt(args, "maxNodes") ?? 5000,
            IncludeBase64: BatchArgs.ReadBool(args, "includeBase64") ?? false,
            IncludeProperties: BatchArgs.ReadUiaPropertiesMode(args, "includeProperties") ?? UiaPropertiesMode.Basic);

          var res = await client.SeeAsync(req, ct).ConfigureAwait(false);
          return (res.Ok, res, res.Error);
        }

        case "peeku_find":
        {
          if (!BatchArgs.TryReadSelector(args, out var selector, out var selectorError))
          {
            return (false, null, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, selectorError ?? "Invalid selector."));
          }

          var target = BatchArgs.TryReadTarget(args, out var t, out _) ? t : null;
          var req = new FindRequest(
            Selector: selector,
            Target: target,
            Limit: BatchArgs.ReadInt(args, "limit") ?? 20);

          var res = await client.FindAsync(req, ct).ConfigureAwait(false);
          return (res.Ok, res, res.Error);
        }

        case "peeku_element_get":
        {
          var hasElement = BatchArgs.TryReadElementRef(args, "elementRef", out var element, out _);
          var hasSelector = BatchArgs.TryReadSelector(args, out var selector, out _);

          if (hasElement == hasSelector)
          {
            return (false, null, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Provide exactly one of elementRef or selector."));
          }

          var target = BatchArgs.TryReadTarget(args, out var t, out _) ? t : null;
          var req = new ElementGetRequest(
            Element: hasElement ? element : null,
            Selector: hasSelector ? selector : null,
            Target: target,
            IncludeProperties: BatchArgs.ReadUiaPropertiesMode(args, "includeProperties") ?? UiaPropertiesMode.All);

          var res = await client.ElementGetAsync(req, ct).ConfigureAwait(false);
          return (res.Ok, res, res.Error);
        }

        case "peeku_click":
        {
          var x = BatchArgs.ReadInt(args, "x");
          var y = BatchArgs.ReadInt(args, "y");
          var hasCoords = x.HasValue && y.HasValue;

          // When coords given, element/selector are optional (canvas escape hatch).
          ElementRef? element = null;
          Selector? selector = null;
          Target? target = null;

          if (hasCoords)
          {
            // Check if both coords AND selection were provided → InvalidArgument.
            var hasElement = BatchArgs.TryReadElementRef(args, "elementRef", out var elemRef, out _);
            var hasSelector = BatchArgs.TryReadSelector(args, out var selRef, out _);
            if (hasElement || hasSelector)
            {
              return (false, null, PeekuErrors.Create(PeekuErrorCode.InvalidArgument,
                "Provide coordinates (x/y) OR an element/selector, not both."));
            }

            target = BatchArgs.TryReadTarget(args, out var t, out _) ? t : null;
          }
          else
          {
            var (selOk, elem, sel, tgt, error) = BatchArgs.ReadSelection(args);
            if (!selOk)
            {
              return (false, null, error);
            }

            element = elem;
            selector = sel;
            target = tgt;
          }

          var method = BatchArgs.ReadActionMethod(args, "method") ?? ActionMethod.Auto;
          var foreground = BatchArgs.ReadBool(args, "foreground") ?? false;
          var globalCoords = BatchArgs.ReadBool(args, "globalCoords") ?? false;
          var doubleClick = BatchArgs.ReadBool(args, "double") ?? false;
          var right = BatchArgs.ReadBool(args, "right") ?? false;

          var req = new ClickRequest(element, selector, target, method, foreground, x, y, globalCoords, doubleClick, right);
          var res = await client.ClickAsync(req, ct).ConfigureAwait(false);
          return (res.Ok, res, res.Error);
        }

        case "peeku_invoke":
        {
          var (selOk, element, selector, target, error) = BatchArgs.ReadSelection(args);
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
          var value = BatchArgs.ReadString(args, "value") ?? "";
          var (selOk, element, selector, target, error) = BatchArgs.ReadSelection(args);
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
          var text = BatchArgs.ReadString(args, "text") ?? "";
          var append = BatchArgs.ReadBool(args, "append") ?? true;
          var delayMs = BatchArgs.ReadInt(args, "delayMs");
          var (selOk, element, selector, target, error) = BatchArgs.ReadSelection(args);
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
          var direction = BatchArgs.ReadScrollDirection(args, "direction") ?? ScrollDirection.Vertical;
          var delta = BatchArgs.ReadInt(args, "delta");
          var lines = BatchArgs.ReadInt(args, "lines");
          var (selOk, element, selector, target, error) = BatchArgs.ReadSelection(args);
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
          var keys = BatchArgs.ReadString(args, "keys") ?? "";
          var res = await client.HotkeyAsync(new HotkeyRequest(keys), ct).ConfigureAwait(false);
          return (res.Ok, res, res.Error);
        }

        case "peeku_press":
        {
          var keys = BatchArgs.ReadStringArray(args, "keys") ?? Array.Empty<string>();
          var count = BatchArgs.ReadInt(args, "count") ?? 1;
          var delayMs = BatchArgs.ReadInt(args, "delayMs");
          var holdMs = BatchArgs.ReadInt(args, "holdMs");
          var target = BatchArgs.TryReadTarget(args, out var pt, out _) ? pt : null;

          var req = new PressRequest(keys, count, delayMs, holdMs, target);
          var res = await client.PressAsync(req, ct).ConfigureAwait(false);
          return (res.Ok, res, res.Error);
        }

        case "peeku_windows_focus":
        {
          if (!BatchArgs.TryReadTarget(args, out var target, out var targetError))
          {
            return (false, null, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, targetError ?? "Invalid target."));
          }

          var res = await client.WindowFocusAsync(new WindowFocusRequest(target), ct).ConfigureAwait(false);
          return (res.Ok, res, res.Error);
        }

        case "peeku_wait":
        {
          if (!BatchArgs.TryReadSelector(args, out var selector, out var selectorError))
          {
            return (false, null, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, selectorError ?? "Invalid selector."));
          }

          if (!BatchArgs.TryReadTarget(args, out var target, out var targetError))
          {
            return (false, null, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, targetError ?? "Invalid target."));
          }

          var timeoutMs = BatchArgs.ReadInt(args, "timeoutMs") ?? 10_000;
          var condition = BatchArgs.ReadWaitCondition(args, "condition") ?? WaitCondition.Exists;
          var expectedValue = BatchArgs.ReadString(args, "value");
          var req = new WaitRequest(selector, target, TimeSpan.FromMilliseconds(Math.Max(0, timeoutMs)), condition, expectedValue);
          var res = await client.WaitAsync(req, ct).ConfigureAwait(false);
          return (res.Ok, res, res.Error);
        }

        case "peeku_observe":
        {
          if (!BatchArgs.TryReadTarget(args, out var target, out var targetError))
          {
            return (false, null, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, targetError ?? "Invalid target."));
          }

          var durationMs = BatchArgs.ReadInt(args, "durationMs") ?? 10_000;
          var maxEvents = BatchArgs.ReadInt(args, "maxEvents") ?? 200;
          var eventsSet = BatchArgs.ReadObserveEventSet(args, "events") ?? ObserveEventSet.All;

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

        case "peeku_diff":
        {
          if (!BatchArgs.TryReadNamedTarget(args, "targetBefore", out var targetBefore, out var beforeError))
          {
            return (false, null, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, beforeError ?? "Invalid targetBefore."));
          }

          if (!BatchArgs.TryReadNamedTarget(args, "targetAfter", out var targetAfter, out var afterError))
          {
            return (false, null, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, afterError ?? "Invalid targetAfter."));
          }

          var req = new DiffRequest(
            TargetBefore: targetBefore,
            TargetAfter: targetAfter,
            Depth: BatchArgs.ReadInt(args, "depth") ?? 6,
            MaxNodes: BatchArgs.ReadInt(args, "maxNodes") ?? 5000,
            IncludeProperties: BatchArgs.ReadUiaPropertiesMode(args, "includeProperties") ?? UiaPropertiesMode.Basic);

          var res = await client.DiffAsync(req, ct).ConfigureAwait(false);
          return (res.Ok, res, res.Error);
        }

        case "peeku_element_from_point":
        {
          var x = BatchArgs.ReadInt(args, "x");
          var y = BatchArgs.ReadInt(args, "y");
          if (x is null || y is null)
          {
            return (false, null, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "x and y are required."));
          }

          var target = BatchArgs.TryReadTarget(args, out var t, out _) ? t : null;
          var req = new ElementAtPointRequest(
            X: x.Value,
            Y: y.Value,
            Target: target,
            IncludeProperties: BatchArgs.ReadUiaPropertiesMode(args, "includeProperties") ?? UiaPropertiesMode.All);

          var res = await client.ElementAtPointAsync(req, ct).ConfigureAwait(false);
          return (res.Ok, res, res.Error);
        }

        case "peeku_window_move":
        {
          if (!BatchArgs.TryReadTarget(args, out var target, out var targetError))
            return (false, null, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, targetError ?? "Invalid target."));
          var x = BatchArgs.ReadInt(args, "x");
          var y = BatchArgs.ReadInt(args, "y");
          if (x is null || y is null)
            return (false, null, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "x and y are required."));
          var res = await client.WindowMoveAsync(new WindowMoveRequest(target, x.Value, y.Value), ct).ConfigureAwait(false);
          return (res.Ok, res, res.Error);
        }

        case "peeku_window_resize":
        {
          if (!BatchArgs.TryReadTarget(args, out var target, out var targetError))
            return (false, null, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, targetError ?? "Invalid target."));
          var width = BatchArgs.ReadInt(args, "width");
          var height = BatchArgs.ReadInt(args, "height");
          if (width is null || height is null)
            return (false, null, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "width and height are required."));
          var res = await client.WindowResizeAsync(new WindowResizeRequest(target, width.Value, height.Value), ct).ConfigureAwait(false);
          return (res.Ok, res, res.Error);
        }

        case "peeku_window_set_bounds":
        {
          if (!BatchArgs.TryReadTarget(args, out var target, out var targetError))
            return (false, null, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, targetError ?? "Invalid target."));
          var x = BatchArgs.ReadInt(args, "x");
          var y = BatchArgs.ReadInt(args, "y");
          var width = BatchArgs.ReadInt(args, "width");
          var height = BatchArgs.ReadInt(args, "height");
          if (x is null || y is null || width is null || height is null)
            return (false, null, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "x, y, width, and height are required."));
          var res = await client.WindowSetBoundsAsync(new WindowBoundsRequest(target, x.Value, y.Value, width.Value, height.Value), ct).ConfigureAwait(false);
          return (res.Ok, res, res.Error);
        }

        case "peeku_window_minimize":
        {
          if (!BatchArgs.TryReadTarget(args, out var target, out var targetError))
            return (false, null, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, targetError ?? "Invalid target."));
          var res = await client.WindowMinimizeAsync(new WindowStateRequest(target), ct).ConfigureAwait(false);
          return (res.Ok, res, res.Error);
        }

        case "peeku_window_maximize":
        {
          if (!BatchArgs.TryReadTarget(args, out var target, out var targetError))
            return (false, null, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, targetError ?? "Invalid target."));
          var res = await client.WindowMaximizeAsync(new WindowStateRequest(target), ct).ConfigureAwait(false);
          return (res.Ok, res, res.Error);
        }

        case "peeku_window_restore":
        {
          if (!BatchArgs.TryReadTarget(args, out var target, out var targetError))
            return (false, null, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, targetError ?? "Invalid target."));
          var res = await client.WindowRestoreAsync(new WindowStateRequest(target), ct).ConfigureAwait(false);
          return (res.Ok, res, res.Error);
        }

        case "peeku_window_close":
        {
          if (!BatchArgs.TryReadTarget(args, out var target, out var targetError))
            return (false, null, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, targetError ?? "Invalid target."));
          var waitMs = BatchArgs.ReadInt(args, "waitMs") ?? 2000;
          var res = await client.WindowCloseAsync(new WindowCloseRequest(target, waitMs), ct).ConfigureAwait(false);
          return (res.Ok, res, res.Error);
        }

        case "peeku_app_launch":
        {
          var launchTarget = BatchArgs.ReadString(args, "target") ?? "";
          if (string.IsNullOrWhiteSpace(launchTarget))
            return (false, null, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "target is required."));
          var req = new AppLaunchRequest(
            Target: launchTarget,
            Args: BatchArgs.ReadString(args, "args"),
            WaitUntilReady: BatchArgs.ReadBool(args, "waitUntilReady") ?? false,
            WaitMs: BatchArgs.ReadInt(args, "waitMs") ?? 5000,
            NoFocus: BatchArgs.ReadBool(args, "noFocus") ?? false);
          var res = await client.AppLaunchAsync(req, ct).ConfigureAwait(false);
          return (res.Ok, res, res.Error);
        }

        case "peeku_app_quit":
        {
          var except = BatchArgs.ReadStringArray(args, "except");
          var req = new AppQuitRequest(
            ProcessId: BatchArgs.ReadInt(args, "processId"),
            ProcessName: BatchArgs.ReadString(args, "processName"),
            Force: BatchArgs.ReadBool(args, "force") ?? false,
            All: BatchArgs.ReadBool(args, "all") ?? false,
            Except: except,
            WaitMs: BatchArgs.ReadInt(args, "waitMs") ?? 3000);
          var res = await client.AppQuitAsync(req, ct).ConfigureAwait(false);
          return (res.Ok, res, res.Error);
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

}
