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
    var cmd = new Command("observe", "Observe UIA events (focus, structure, property)");
    var targetOpts = CliTargets.AddTo(cmd, allowQuery: true);

    var eventsOpt = new Option<string[]>("--events")
    {
      Description = "Channels to observe: structure|property|focus|all (repeatable; focus carries a WinEvent foreground backstop)",
      AllowMultipleArgumentsPerToken = true,
    };
    eventsOpt.DefaultValueFactory = _ => new[] { "all" };
    eventsOpt.Validators.Add(r =>
    {
      foreach (var raw in r.GetValueOrDefault<string[]>() ?? Array.Empty<string>())
      {
        var v = (raw ?? "").Trim();
        if (!string.Equals(v, "structure", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(v, "property", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(v, "focus", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(v, "all", StringComparison.OrdinalIgnoreCase))
        {
          r.AddError($"Invalid --events value '{raw}'. Allowed: structure|property|focus|all");
        }
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

      if (!CliTargets.TryParseOrDefaultFocused(parse, targetOpts, out var target, out var targetError))
      {
        return CliErrors.Write(ctx, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, targetError ?? "Invalid target."));
      }

      var req = new ObserveRequest(
        Target: target,
        Events: ParseEvents(parse.GetValue(eventsOpt)),
        Duration: parse.GetValue(durationOpt),
        MaxEvents: parse.GetValue(maxEventsOpt));

      var events = new List<ObservationEvent>(capacity: Math.Clamp(req.MaxEvents, 0, 512));

      // observe's stream lifetime is owned by --duration, NOT the global --timeout (which
      // defaults to 10s and would otherwise silently truncate a longer --duration). Pass the
      // raw invocation token so Ctrl-C still stops it; --duration governs normal completion.
      try
      {
        var client = CliPeekuClient.CreateDefault();
        await foreach (var ev in client.ObserveAsync(req, ct).ConfigureAwait(false))
        {
          events.Add(ev);
        }
      }
      catch (OperationCanceledException)
      {
        // Ctrl-C on the daemon path surfaces here; in-proc cancellation ends via yield break.
      }

      // The in-proc engine swallows its own cancellation, so detect truncation from the token
      // rather than an exception: a Ctrl-C before --duration is the only way ct is cancelled here.
      if (ct.IsCancellationRequested)
      {
        Console.Error.WriteLine(
          $"[peeku] observe cancelled before --duration ({req.Duration:c}); returning {events.Count} event(s) collected so far.");
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

    // --selector is no longer Required: the positional query is an accepted alternative.
    var selectorOpt = new Option<string?>("--selector") { Description = "Selector expression" };

    var queryArg = new Argument<string?>("query")
    {
      Description = "Selector expression (positional shorthand for --selector)",
      Arity = ArgumentArity.ZeroOrOne,
    };

    var liveOpt = new Option<bool>("--live") { Description = "Use live UIA evaluation (event-driven) for selector" };
    liveOpt.DefaultValueFactory = _ => false;

    var conditionOpt = new Option<string?>("--condition") { Description = "exists|notExists|enabled|disabled|visible|hidden|focused|toggleOn|toggleOff|expanded|collapsed|selected|notSelected|valueEquals|valueContains|nameEquals|nameContains (default: exists)" };
    conditionOpt.DefaultValueFactory = _ => "exists";
    conditionOpt.Validators.Add(r =>
    {
      var v = (r.GetValueOrDefault<string>() ?? "exists").Trim();
      if (!TryParseCondition(v, out _))
      {
        r.AddError($"Invalid --condition '{v}'. See --help for valid values.");
      }
    });

    var valueOpt = new Option<string?>("--value") { Description = "Expected value for valueEquals/valueContains/nameEquals/nameContains conditions" };

    cmd.Add(selectorOpt);
    cmd.Add(queryArg);
    cmd.Add(liveOpt);
    cmd.Add(conditionOpt);
    cmd.Add(valueOpt);

    cmd.SetAction(async (ParseResult parse, CancellationToken ct) =>
    {
      var ctx = CliContextAccessor.Current;

      if (!CliSelection.TryParseSelectorOnly(parse, selectorOpt, queryArg, liveOpt, out var selector, out var selectorError))
      {
        return CliErrors.Write(ctx, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, selectorError ?? "Invalid selector."));
      }

      if (!CliTargets.TryParseOrDefaultFocused(parse, targetOpts, out var target, out var targetError))
      {
        return CliErrors.Write(ctx, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, targetError ?? "Invalid target."));
      }

      var conditionRaw = (parse.GetValue(conditionOpt) ?? "exists").Trim();
      if (!TryParseCondition(conditionRaw, out var condition))
      {
        return CliErrors.Write(ctx, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, $"Invalid --condition '{conditionRaw}'."));
      }

      var expectedValue = parse.GetValue(valueOpt);

      var client = CliPeekuClient.CreateDefault();
      var res = await client.WaitAsync(new WaitRequest(
        Selector: selector!,
        Target: target,
        Timeout: ctx.Timeout,
        Condition: condition,
        ExpectedValue: expectedValue), ct).ConfigureAwait(false);

      CliOutput.Write(res, ctx.Format);
      return res.Ok ? 0 : ExitCodes.For(res.Error);
    });

    return cmd;
  }

  private static Command CreateBatchCommand()
  {
    var cmd = new Command("batch", "Run a batch of operations from a JSON file or stdin");

    // --in is optional: absent or "-" reads the ops array from redirected stdin.
    var inOpt = new Option<string?>("--in") { Description = "Path to JSON ops array, or '-' for stdin" };

    // Positional source: accepts a path or "-" so `cat ops.json | peeku batch -` parses.
    var sourceArg = new Argument<string?>("source")
    {
      Description = "Path to JSON ops array, or '-' for stdin (positional alternative to --in)",
      Arity = ArgumentArity.ZeroOrOne,
    };

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
    cmd.Add(sourceArg);
    cmd.Add(stopOnErrorOpt);

    cmd.SetAction(async (ParseResult parse, CancellationToken ct) =>
    {
      var ctx = CliContextAccessor.Current;
      using var scope = TimeoutScope.Create(ctx.Timeout, ct);

      // --in wins; else the positional source. Both accept a path or "-".
      var inFlag = parse.GetValue(inOpt);
      var path = !string.IsNullOrWhiteSpace(inFlag) ? inFlag : parse.GetValue(sourceArg);

      // Resolve the JSON source: stdin (absent/"-") or a file path.
      string json;
      string sourcePath; // used in error envelopes for coherence
      if (string.IsNullOrWhiteSpace(path) || string.Equals(path.Trim(), "-", StringComparison.Ordinal))
      {
        if (!Console.IsInputRedirected)
        {
          return CliErrors.Write(ctx, PeekuErrors.Create(
            PeekuErrorCode.InvalidArgument,
            "provide ops via --in <path> or pipe JSON to stdin"));
        }

        json = Console.In.ReadToEnd();
        sourcePath = "<stdin>";
      }
      else if (!File.Exists(path))
      {
        return CliErrors.Write(ctx, PeekuErrors.Create(
          PeekuErrorCode.InvalidArgument, "Input file not found.", new { path }));
      }
      else
      {
        json = File.ReadAllText(path);
        sourcePath = path;
      }

      JsonDocument doc;
      try
      {
        doc = JsonDocument.Parse(json);
      }
      catch (Exception ex)
      {
        return CliErrors.Write(ctx, PeekuErrors.Create(
          PeekuErrorCode.InvalidArgument,
          "Failed to parse JSON.",
          new { path = sourcePath, exception = ex.GetType().FullName, ex.Message }));
      }

      using (doc)
      {
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
          return CliErrors.Write(ctx, PeekuErrors.Create(
            PeekuErrorCode.InvalidArgument, "Batch input must be a JSON array.", new { path = sourcePath }));
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
          StopOnError: stopOnError), scope.Token).ConfigureAwait(false);

        CliOutput.Write(res, ctx.Format);
        return res.Ok ? 0 : ExitCodes.For(res.Error, scope.DeadlineElapsed);
      }
    });

    return cmd;
  }

  private static Command CreateWatchCommand()
  {
    var cmd = new Command("watch", "Stream live selector updates as JSONL (daemon only)");
    var targetOpts = CliTargets.AddTo(cmd, allowQuery: true);

    // --selector is no longer Required: the positional query is an accepted alternative.
    var selectorOpt = new Option<string?>("--selector") { Description = "Selector expression" };

    var queryArg = new Argument<string?>("query")
    {
      Description = "Selector expression (positional shorthand for --selector)",
      Arity = ArgumentArity.ZeroOrOne,
    };

    // watch always uses live evaluation (PreferCachedSnapshot: false); expose --live to keep the
    // selector-only helper signature consistent, defaulting to live.
    var liveOpt = new Option<bool>("--live") { Description = "Use live UIA evaluation for selector" };
    liveOpt.DefaultValueFactory = _ => true;

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
    cmd.Add(queryArg);
    cmd.Add(liveOpt);
    cmd.Add(debounceOpt);
    cmd.Add(limitOpt);

    cmd.SetAction(async (ParseResult parse, CancellationToken ct) =>
    {
      var ctx = CliContextAccessor.Current;

      if (!CliSelection.TryParseSelectorOnly(parse, selectorOpt, queryArg, liveOpt, out var parsedSelector, out var selectorError))
      {
        return CliErrors.Write(ctx, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, selectorError ?? "Invalid selector."));
      }

      if (!CliTargets.TryParseOrDefaultFocused(parse, targetOpts, out var target, out var targetError))
      {
        return CliErrors.Write(ctx, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, targetError ?? "Invalid target."));
      }

      if (!TryCreateDaemonClient(ctx, out var daemonClient, out var daemonError))
      {
        return CliErrors.Write(ctx, PeekuErrors.Create(PeekuErrorCode.Unavailable, daemonError));
      }

      // watch always evaluates live, regardless of --live, since it streams change.
      var selector = new Selector(parsedSelector!.Expr, PreferCachedSnapshot: false);
      var debounceMs = Math.Max(0, parse.GetValue(debounceOpt));
      var debounce = TimeSpan.FromMilliseconds(debounceMs);
      var limit = Math.Max(1, parse.GetValue(limitOpt));
      var request = new FindRequest(selector, target, limit);

      var lastSignature = "";
      while (!ct.IsCancellationRequested)
      {
        FindResult res;
        bool callDeadlineElapsed;
        using (var callScope = TimeoutScope.Create(ctx.Timeout, ct))
        {
          res = await daemonClient.FindAsync(request, callScope.Token).ConfigureAwait(false);
          callDeadlineElapsed = callScope.DeadlineElapsed;
        }

        if (!res.Ok)
        {
          // watch always emits compact JSONL regardless of --format.
          CliOutput.WriteLine(new
          {
            type = "watch.error",
            timestamp = DateTimeOffset.UtcNow,
            selector = request.Selector.Expr,
            error = res.Error,
            meta = res.Meta,
          });
          return ExitCodes.For(res.Error, callDeadlineElapsed);
        }

        var signature = BuildMatchSignature(res.Matches);
        if (!string.Equals(signature, lastSignature, StringComparison.Ordinal))
        {
          CliOutput.WriteLine(new
          {
            type = "watch.update",
            timestamp = DateTimeOffset.UtcNow,
            selector = request.Selector.Expr,
            count = res.Matches.Count,
            matches = res.Matches,
            meta = res.Meta,
          });
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

  private static ObserveEventSet ParseEvents(string[]? raw)
  {
    if (raw is null || raw.Length == 0)
    {
      return ObserveEventSet.All;
    }

    // "all" is an alias for the full set; otherwise OR the individual channel tokens through the
    // shared SSOT decoder so subsets like `--events focus --events structure` compose correctly.
    foreach (var t in raw)
    {
      if (string.Equals(t?.Trim(), "all", StringComparison.OrdinalIgnoreCase))
      {
        return ObserveEventSet.All;
      }
    }

    var set = ObserveEventTokens.Decode(raw);
    return set == ObserveEventSet.None ? ObserveEventSet.All : set;
  }

  // Short connect/ping budget for the watch daemon probe so a stale marker no longer costs a
  // full command --timeout (PID-liveness already filters dead markers). (plan §6)
  private static readonly TimeSpan DaemonProbeBudget = TimeSpan.FromMilliseconds(300);

  private static bool TryCreateDaemonClient(CliContext ctx, out IPeekuClient client, out string error)
  {
    client = null!;
    error = "";

    if (!DaemonMarker.TryLoad(out var marker))
    {
      error = "Watch requires daemon. Start with `peeku --daemon`.";
      return false;
    }

    // PID-liveness before the pipe ping: drop a stale marker instead of paying a connect wait.
    if (!marker.IsAlive())
    {
      DaemonMarker.TryDeleteStale();
      error = "Daemon unreachable. Restart with `peeku --daemon`.";
      return false;
    }

    try
    {
      using var pingCts = new CancellationTokenSource(DaemonProbeBudget);
      var rpc = new DaemonJsonRpcClient(marker.PipeName, DaemonProbeBudget);
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

  private static bool TryParseCondition(string raw, out WaitCondition condition)
  {
    condition = WaitCondition.Exists;
    return (raw ?? "").Trim().ToLowerInvariant() switch
    {
      "exists"        => Set(out condition, WaitCondition.Exists),
      "notexists"     => Set(out condition, WaitCondition.NotExists),
      "enabled"       => Set(out condition, WaitCondition.Enabled),
      "disabled"      => Set(out condition, WaitCondition.Disabled),
      "visible"       => Set(out condition, WaitCondition.Visible),
      "hidden"        => Set(out condition, WaitCondition.Hidden),
      "focused"       => Set(out condition, WaitCondition.Focused),
      "toggleon"      => Set(out condition, WaitCondition.ToggleOn),
      "toggleoff"     => Set(out condition, WaitCondition.ToggleOff),
      "expanded"      => Set(out condition, WaitCondition.Expanded),
      "collapsed"     => Set(out condition, WaitCondition.Collapsed),
      "selected"      => Set(out condition, WaitCondition.Selected),
      "notselected"   => Set(out condition, WaitCondition.NotSelected),
      "valueequals"   => Set(out condition, WaitCondition.ValueEquals),
      "valuecontains" => Set(out condition, WaitCondition.ValueContains),
      "nameequals"    => Set(out condition, WaitCondition.NameEquals),
      "namecontains"  => Set(out condition, WaitCondition.NameContains),
      _ => false,
    };

    static bool Set(out WaitCondition c, WaitCondition v) { c = v; return true; }
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
