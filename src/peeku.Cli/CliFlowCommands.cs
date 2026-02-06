using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.Json;
using peeku;

namespace peeku.Cli;

internal static class CliFlowCommands
{
  internal static void AddAll(RootCommand root)
  {
    root.Add(CreateObserveCommand());
    root.Add(CreateWaitCommand());
    root.Add(CreateWatchCommand());
    root.Add(CreateBatchCommand());
  }

  private static Command CreateObserveCommand()
  {
    var cmd = new Command("observe", "Observe UIA events (focus supported)");
    var targetOpts = CliTargets.AddTo(cmd, allowQuery: true);

    var eventsOpt = new Option<string>("--events") { Description = "structure|property|focus|all" };
    eventsOpt.DefaultValueFactory = _ => "all";
    eventsOpt.Validators.Add(r =>
    {
      var v = (r.GetValueOrDefault<string>() ?? "all").Trim();
      if (!string.Equals(v, "structure", StringComparison.OrdinalIgnoreCase) &&
          !string.Equals(v, "property", StringComparison.OrdinalIgnoreCase) &&
          !string.Equals(v, "focus", StringComparison.OrdinalIgnoreCase) &&
          !string.Equals(v, "all", StringComparison.OrdinalIgnoreCase))
      {
        r.AddError("Invalid --events. Allowed: structure|property|focus|all");
      }
    });

    var durationOpt = new Option<TimeSpan>("--duration") { Description = "Observation duration (e.g. 00:00:10)" };
    durationOpt.DefaultValueFactory = _ => TimeSpan.FromSeconds(10);

    var maxEventsOpt = new Option<int>("--max-events") { Description = "Max events" };
    maxEventsOpt.DefaultValueFactory = _ => 200;

    cmd.Add(eventsOpt);
    cmd.Add(durationOpt);
    cmd.Add(maxEventsOpt);

    cmd.SetAction(async (ParseResult parse, CancellationToken ct) =>
    {
      var ctx = CliContextAccessor.Current;
      using var cts = CreateTimeoutCts(ctx.Timeout, ct);

      var target = CliTargets.ParseOrDefaultFocused(parse, targetOpts);
      var req = new ObserveRequest(
        Target: target,
        Events: ParseEvents(parse.GetValue(eventsOpt)),
        Duration: parse.GetValue(durationOpt),
        MaxEvents: parse.GetValue(maxEventsOpt));

      var events = new List<ObservationEvent>(capacity: Math.Clamp(req.MaxEvents, 0, 512));

      try
      {
        var client = CliPeekuClient.CreateDefault();
        await foreach (var ev in client.ObserveAsync(req, cts.Token).ConfigureAwait(false))
        {
          events.Add(ev);
        }
      }
      catch (OperationCanceledException)
      {
        // normal: duration/timeout
      }

      CliOutput.Write(events, ctx.Format);
      return 0;
    });

    return cmd;
  }

  private static Command CreateWaitCommand()
  {
    var cmd = new Command("wait", "Wait for selector to match");
    var targetOpts = CliTargets.AddTo(cmd, allowQuery: true);

    var selectorOpt = new Option<string>("--selector") { Description = "Selector expression" };
    selectorOpt.Required = true;

    var liveOpt = new Option<bool>("--live") { Description = "Use live UIA evaluation (event-driven) for selector" };
    liveOpt.DefaultValueFactory = _ => false;

    cmd.Add(selectorOpt);
    cmd.Add(liveOpt);

    cmd.SetAction(async (ParseResult parse, CancellationToken ct) =>
    {
      var ctx = CliContextAccessor.Current;

      var selectorRaw = parse.GetValue(selectorOpt) ?? "";
      var selector = new Selector(selectorRaw.Trim(), PreferCachedSnapshot: !parse.GetValue(liveOpt));
      var target = CliTargets.ParseOrDefaultFocused(parse, targetOpts);

      var client = CliPeekuClient.CreateDefault();
      var res = await client.WaitAsync(new WaitRequest(
        Selector: selector,
        Target: target,
        Timeout: ctx.Timeout), ct).ConfigureAwait(false);

      CliOutput.Write(res, ctx.Format);
      return res.Ok ? 0 : 1;
    });

    return cmd;
  }

  private static Command CreateBatchCommand()
  {
    var cmd = new Command("batch", "Run a batch of operations from a JSON file");

    var inOpt = new Option<string>("--in") { Description = "Path to JSON ops array" };
    inOpt.Required = true;

    var stopOnErrorOpt = new Option<string>("--stop-on-error") { Description = "true|false" };
    stopOnErrorOpt.DefaultValueFactory = _ => "true";
    stopOnErrorOpt.Validators.Add(r =>
    {
      var v = (r.GetValueOrDefault<string>() ?? "true").Trim();
      if (!string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) &&
          !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase))
      {
        r.AddError("Invalid --stop-on-error. Allowed: true|false");
      }
    });

    cmd.Add(inOpt);
    cmd.Add(stopOnErrorOpt);

    cmd.SetAction(async (ParseResult parse, CancellationToken ct) =>
    {
      var ctx = CliContextAccessor.Current;
      using var cts = CreateTimeoutCts(ctx.Timeout, ct);

      var path = parse.GetValue(inOpt) ?? "";
      if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
      {
        CliOutput.Write(new
        {
          ok = false,
          meta = new { traceId = ctx.TraceId },
          error = new { code = "InvalidArgument", message = "Input file not found.", details = new { path } },
          traceId = ctx.TraceId,
        }, ctx.Format);
        return 1;
      }

      JsonDocument doc;
      try
      {
        doc = JsonDocument.Parse(File.ReadAllText(path));
      }
      catch (Exception ex)
      {
        CliOutput.Write(new
        {
          ok = false,
          meta = new { traceId = ctx.TraceId },
          error = new { code = "InvalidArgument", message = "Failed to parse JSON.", details = new { path, exception = ex.GetType().FullName, ex.Message } },
          traceId = ctx.TraceId,
        }, ctx.Format);
        return 1;
      }

      using (doc)
      {
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
          CliOutput.Write(new
          {
            ok = false,
            meta = new { traceId = ctx.TraceId },
            error = new { code = "InvalidArgument", message = "Batch input must be a JSON array.", details = new { path } },
            traceId = ctx.TraceId,
          }, ctx.Format);
          return 1;
        }

        var emptyArgs = JsonDocument.Parse("{}").RootElement.Clone();
        var ops = new List<BatchOp>(capacity: Math.Clamp(doc.RootElement.GetArrayLength(), 0, 1024));
        foreach (var op in doc.RootElement.EnumerateArray())
        {
          if (op.ValueKind != JsonValueKind.Object)
          {
            continue;
          }

          if (!op.TryGetProperty("tool", out var toolEl) || toolEl.ValueKind != JsonValueKind.String)
          {
            continue;
          }

          var tool = toolEl.GetString();
          if (string.IsNullOrWhiteSpace(tool))
          {
            continue;
          }

          var args = op.TryGetProperty("args", out var argsEl) ? argsEl.Clone() : emptyArgs;
          ops.Add(new BatchOp(tool.Trim(), args));
        }

        var stopOnErrorRaw = parse.GetValue(stopOnErrorOpt) ?? "true";
        var stopOnError = !string.Equals(stopOnErrorRaw.Trim(), "false", StringComparison.OrdinalIgnoreCase);

        var client = CliPeekuClient.CreateDefault();
        var res = await client.BatchAsync(new BatchRequest(
          Ops: ops,
          StopOnError: stopOnError), cts.Token).ConfigureAwait(false);

        CliOutput.Write(res, ctx.Format);
        return res.Ok ? 0 : 1;
      }
    });

    return cmd;
  }

  private static Command CreateWatchCommand()
  {
    var cmd = new Command("watch", "Stream live selector updates as JSONL (daemon only)");
    var targetOpts = CliTargets.AddTo(cmd, allowQuery: true);

    var selectorOpt = new Option<string>("--selector") { Description = "Selector expression" };
    selectorOpt.Required = true;

    var debounceOpt = new Option<int>("--debounce-ms") { Description = "Debounce between evaluations in ms" };
    debounceOpt.DefaultValueFactory = _ => 100;
    debounceOpt.Validators.Add(r =>
    {
      var v = r.GetValueOrDefault<int>();
      if (v < 0)
      {
        r.AddError("Invalid --debounce-ms. Must be >= 0.");
      }
    });

    var limitOpt = new Option<int>("--limit") { Description = "Max matches per evaluation" };
    limitOpt.DefaultValueFactory = _ => 20;
    limitOpt.Validators.Add(r =>
    {
      var v = r.GetValueOrDefault<int>();
      if (v <= 0)
      {
        r.AddError("Invalid --limit. Must be >= 1.");
      }
    });

    cmd.Add(selectorOpt);
    cmd.Add(debounceOpt);
    cmd.Add(limitOpt);

    cmd.SetAction(async (ParseResult parse, CancellationToken ct) =>
    {
      var ctx = CliContextAccessor.Current;

      if (!TryCreateDaemonClient(ctx, out var daemonClient, out var daemonError))
      {
        CliOutput.Write(new
        {
          ok = false,
          meta = new { traceId = ctx.TraceId },
          error = new { code = "InvalidOperation", message = daemonError },
          traceId = ctx.TraceId,
        }, ctx.Format);
        return 1;
      }

      var selectorRaw = parse.GetValue(selectorOpt) ?? "";
      var selector = new Selector(selectorRaw.Trim(), PreferCachedSnapshot: false);
      var target = CliTargets.ParseOrDefaultFocused(parse, targetOpts);
      var debounceMs = Math.Max(0, parse.GetValue(debounceOpt));
      var debounce = TimeSpan.FromMilliseconds(debounceMs);
      var limit = Math.Max(1, parse.GetValue(limitOpt));
      var request = new FindRequest(selector, target, limit);

      var streamOptions = new JsonSerializerOptions
      {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
      };

      var lastSignature = "";
      while (!ct.IsCancellationRequested)
      {
        FindResult res;
        using (var callCts = CreateTimeoutCts(ctx.Timeout, ct))
        {
          res = await daemonClient.FindAsync(request, callCts.Token).ConfigureAwait(false);
        }

        if (!res.Ok)
        {
          Console.Out.WriteLine(JsonSerializer.Serialize(new
          {
            type = "watch.error",
            timestamp = DateTimeOffset.UtcNow,
            selector = request.Selector.Expr,
            error = res.Error,
            meta = res.Meta,
          }, streamOptions));
          return 1;
        }

        var signature = BuildMatchSignature(res.Matches);
        if (!string.Equals(signature, lastSignature, StringComparison.Ordinal))
        {
          Console.Out.WriteLine(JsonSerializer.Serialize(new
          {
            type = "watch.update",
            timestamp = DateTimeOffset.UtcNow,
            selector = request.Selector.Expr,
            count = res.Matches.Count,
            matches = res.Matches,
            meta = res.Meta,
          }, streamOptions));
          lastSignature = signature;
        }

        if (debounce > TimeSpan.Zero)
        {
          await Task.Delay(debounce, ct).ConfigureAwait(false);
        }
      }

      return 0;
    });

    return cmd;
  }

  private static ObserveEventSet ParseEvents(string? raw)
  {
    if (string.Equals(raw, "structure", StringComparison.OrdinalIgnoreCase)) return ObserveEventSet.Structure;
    if (string.Equals(raw, "property", StringComparison.OrdinalIgnoreCase)) return ObserveEventSet.Property;
    if (string.Equals(raw, "focus", StringComparison.OrdinalIgnoreCase)) return ObserveEventSet.Focus;
    return ObserveEventSet.All;
  }

  private static CancellationTokenSource CreateTimeoutCts(TimeSpan timeout, CancellationToken ct)
  {
    var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    if (timeout > TimeSpan.Zero)
    {
      cts.CancelAfter(timeout);
    }

    return cts;
  }

  private static bool TryCreateDaemonClient(CliContext ctx, out IPeekuClient client, out string error)
  {
    client = null!;
    error = "";

    if (!DaemonMarker.TryLoad(out var marker))
    {
      error = "Watch requires daemon. Start with `peeku --daemon`.";
      return false;
    }

    try
    {
      using var pingCts = CreateTimeoutCts(ctx.Timeout, CancellationToken.None);
      var rpc = new DaemonJsonRpcClient(marker.PipeName, ctx.Timeout);
      var ok = rpc.TryPingAsync(pingCts.Token).GetAwaiter().GetResult();
      if (!ok)
      {
        error = "Daemon unreachable. Restart with `peeku --daemon`.";
        return false;
      }

      client = new DaemonPeekuClient(rpc);
      return true;
    }
    catch
    {
      error = "Daemon unreachable. Restart with `peeku --daemon`.";
      return false;
    }
  }

  private static string BuildMatchSignature(IReadOnlyList<FindMatch> matches)
  {
    if (matches is null || matches.Count == 0)
    {
      return "";
    }

    var parts = new string[matches.Count];
    for (var i = 0; i < matches.Count; i++)
    {
      var m = matches[i];
      parts[i] = string.Concat(
        m.Element.RefId ?? "",
        "|",
        m.Rect.X.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
        "|",
        m.Rect.Y.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
        "|",
        m.Rect.Width.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
        "|",
        m.Rect.Height.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture));
    }

    return string.Join(";", parts);
  }
}
