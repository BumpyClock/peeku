using System.CommandLine;
using System.CommandLine.Parsing;
using peeku;

namespace peeku.Cli;

internal static class CliActionCommands
{
  internal static void AddAll(RootCommand root)
  {
    root.Add(CreateClickCommand());
    root.Add(CreateInvokeCommand());
    root.Add(CreateSetValueCommand());
    root.Add(CreateTypeCommand());
    root.Add(CreateScrollCommand());
    root.Add(CreateHotkeyCommand());
  }

  private static Command CreateClickCommand()
  {
    var cmd = new Command("click", "Click an element");
    var targetOpts = CliTargets.AddTo(cmd, allowQuery: true);

    var selection = AddSelectionOptions(cmd);

    var methodOpt = new Option<string>("--method") { Description = "auto|uia|input" };
    methodOpt.DefaultValueFactory = _ => "auto";
    methodOpt.Validators.Add(r =>
    {
      var v = (r.GetValueOrDefault<string>() ?? "auto").Trim();
      if (!string.Equals(v, "auto", StringComparison.OrdinalIgnoreCase) &&
          !string.Equals(v, "uia", StringComparison.OrdinalIgnoreCase) &&
          !string.Equals(v, "input", StringComparison.OrdinalIgnoreCase))
      {
        r.AddError("Invalid --method. Allowed: auto|uia|input");
      }
    });

    cmd.Add(methodOpt);

    cmd.SetAction(async (ParseResult parse, CancellationToken ct) =>
    {
      var ctx = CliContextAccessor.Current;
      using var cts = CreateTimeoutCts(ctx.Timeout, ct);

      if (!TryParseSelection(parse, selection, out var elementRef, out var selector, out var selectionError))
      {
        WriteInvalidArgument(ctx, selectionError ?? "Invalid selection.");
        return 1;
      }

      var method = ParseMethod(parse.GetValue(methodOpt));

      var client = CliPeekuClient.CreateDefault();
      var res = await client.ClickAsync(new ClickRequest(
        Element: elementRef,
        Selector: selector,
        Target: CliTargets.ParseOptional(parse, targetOpts),
        Method: method), cts.Token).ConfigureAwait(false);

      CliOutput.Write(res, ctx.Format);
      return res.Ok ? 0 : 1;
    });

    return cmd;
  }

  private static Command CreateInvokeCommand()
  {
    var cmd = new Command("invoke", "Invoke an element (UIA invoke when available)");
    var targetOpts = CliTargets.AddTo(cmd, allowQuery: true);
    var selection = AddSelectionOptions(cmd);

    cmd.SetAction(async (ParseResult parse, CancellationToken ct) =>
    {
      var ctx = CliContextAccessor.Current;
      using var cts = CreateTimeoutCts(ctx.Timeout, ct);

      if (!TryParseSelection(parse, selection, out var elementRef, out var selector, out var selectionError))
      {
        WriteInvalidArgument(ctx, selectionError ?? "Invalid selection.");
        return 1;
      }

      var client = CliPeekuClient.CreateDefault();
      var res = await client.InvokeAsync(new InvokeRequest(
        Element: elementRef,
        Selector: selector,
        Target: CliTargets.ParseOptional(parse, targetOpts)), cts.Token).ConfigureAwait(false);

      CliOutput.Write(res, ctx.Format);
      return res.Ok ? 0 : 1;
    });

    return cmd;
  }

  private static Command CreateSetValueCommand()
  {
    var cmd = new Command("set-value", "Set Value pattern text on an element");
    var targetOpts = CliTargets.AddTo(cmd, allowQuery: true);

    var selection = AddSelectionOptions(cmd);
    var valueOpt = new Option<string>("--value") { Description = "Value text" };
    valueOpt.Required = true;

    cmd.Add(valueOpt);

    cmd.SetAction(async (ParseResult parse, CancellationToken ct) =>
    {
      var ctx = CliContextAccessor.Current;
      using var cts = CreateTimeoutCts(ctx.Timeout, ct);

      if (!TryParseSelection(parse, selection, out var elementRef, out var selector, out var selectionError))
      {
        WriteInvalidArgument(ctx, selectionError ?? "Invalid selection.");
        return 1;
      }

      var value = parse.GetValue(valueOpt) ?? "";

      var client = CliPeekuClient.CreateDefault();
      var res = await client.SetValueAsync(new SetValueRequest(
        Element: elementRef,
        Selector: selector,
        Target: CliTargets.ParseOptional(parse, targetOpts),
        Value: value), cts.Token).ConfigureAwait(false);

      CliOutput.Write(res, ctx.Format);
      return res.Ok ? 0 : 1;
    });

    return cmd;
  }

  private static Command CreateTypeCommand()
  {
    var cmd = new Command("type", "Type text into an element");
    var targetOpts = CliTargets.AddTo(cmd, allowQuery: true);

    var selection = AddSelectionOptions(cmd);

    var textOpt = new Option<string>("--text") { Description = "Text to type" };
    textOpt.Required = true;

    var appendOpt = new Option<string>("--append") { Description = "true|false" };
    appendOpt.DefaultValueFactory = _ => "true";
    appendOpt.Validators.Add(r =>
    {
      var v = (r.GetValueOrDefault<string>() ?? "true").Trim();
      if (!string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) &&
          !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase))
      {
        r.AddError("Invalid --append. Allowed: true|false");
      }
    });

    var delayOpt = new Option<int?>("--delay-ms") { Description = "Optional inter-key delay (ms)" };

    cmd.Add(textOpt);
    cmd.Add(appendOpt);
    cmd.Add(delayOpt);

    cmd.SetAction(async (ParseResult parse, CancellationToken ct) =>
    {
      var ctx = CliContextAccessor.Current;
      using var cts = CreateTimeoutCts(ctx.Timeout, ct);

      if (!TryParseSelection(parse, selection, out var elementRef, out var selector, out var selectionError))
      {
        WriteInvalidArgument(ctx, selectionError ?? "Invalid selection.");
        return 1;
      }

      var text = parse.GetValue(textOpt) ?? "";

      var appendRaw = parse.GetValue(appendOpt) ?? "true";
      var append = !string.Equals(appendRaw.Trim(), "false", StringComparison.OrdinalIgnoreCase);

      var client = CliPeekuClient.CreateDefault();
      var res = await client.TypeAsync(new TypeRequest(
        Element: elementRef,
        Selector: selector,
        Target: CliTargets.ParseOptional(parse, targetOpts),
        Text: text,
        Append: append,
        DelayMs: parse.GetValue(delayOpt)), cts.Token).ConfigureAwait(false);

      CliOutput.Write(res, ctx.Format);
      return res.Ok ? 0 : 1;
    });

    return cmd;
  }

  private static Command CreateScrollCommand()
  {
    var cmd = new Command("scroll", "Scroll an element");
    var targetOpts = CliTargets.AddTo(cmd, allowQuery: true);

    var selection = AddSelectionOptions(cmd);

    var deltaOpt = new Option<int?>("--delta") { Description = "Wheel delta (e.g. -120|120)" };
    var linesOpt = new Option<int?>("--lines") { Description = "Line count (positive/negative)" };

    var directionOpt = new Option<string>("--direction") { Description = "vertical|horizontal" };
    directionOpt.DefaultValueFactory = _ => "vertical";
    directionOpt.Validators.Add(r =>
    {
      var v = (r.GetValueOrDefault<string>() ?? "vertical").Trim();
      if (!string.Equals(v, "vertical", StringComparison.OrdinalIgnoreCase) &&
          !string.Equals(v, "horizontal", StringComparison.OrdinalIgnoreCase))
      {
        r.AddError("Invalid --direction. Allowed: vertical|horizontal");
      }
    });

    cmd.Add(deltaOpt);
    cmd.Add(linesOpt);
    cmd.Add(directionOpt);

    cmd.SetAction(async (ParseResult parse, CancellationToken ct) =>
    {
      var ctx = CliContextAccessor.Current;
      using var cts = CreateTimeoutCts(ctx.Timeout, ct);

      if (!TryParseSelection(parse, selection, out var elementRef, out var selector, out var selectionError))
      {
        WriteInvalidArgument(ctx, selectionError ?? "Invalid selection.");
        return 1;
      }

      var delta = parse.GetValue(deltaOpt);
      var lines = parse.GetValue(linesOpt);
      if (delta is null == lines is null)
      {
        WriteInvalidArgument(ctx, "Provide exactly one of --delta or --lines.");
        return 1;
      }

      var direction = ParseScrollDirection(parse.GetValue(directionOpt));

      var client = CliPeekuClient.CreateDefault();
      var res = await client.ScrollAsync(new ScrollRequest(
        Element: elementRef,
        Selector: selector,
        Target: CliTargets.ParseOptional(parse, targetOpts),
        Delta: delta,
        Lines: lines,
        Direction: direction), cts.Token).ConfigureAwait(false);

      CliOutput.Write(res, ctx.Format);
      return res.Ok ? 0 : 1;
    });

    return cmd;
  }

  private static Command CreateHotkeyCommand()
  {
    var cmd = new Command("hotkey", "Send a hotkey chord (e.g. CTRL+SHIFT+S)");

    var keysOpt = new Option<string>("--keys") { Description = "Hotkey chord" };
    keysOpt.Required = true;
    cmd.Add(keysOpt);

    cmd.SetAction(async (ParseResult parse, CancellationToken ct) =>
    {
      var ctx = CliContextAccessor.Current;
      using var cts = CreateTimeoutCts(ctx.Timeout, ct);

      var keys = parse.GetValue(keysOpt) ?? "";
      var client = CliPeekuClient.CreateDefault();
      var res = await client.HotkeyAsync(new HotkeyRequest(Keys: keys), cts.Token).ConfigureAwait(false);

      CliOutput.Write(res, ctx.Format);
      return res.Ok ? 0 : 1;
    });

    return cmd;
  }

  private sealed record SelectionOptions(
    Option<string?> Ref,
    Option<string?> SnapshotId,
    Option<string?> Selector,
    Option<bool> Live);

  private static SelectionOptions AddSelectionOptions(Command cmd)
  {
    var refOpt = new Option<string?>("--ref") { Description = "Element refId" };
    var snapshotIdOpt = new Option<string?>("--snapshotId") { Description = "Optional snapshotId for elementRef" };
    var selectorOpt = new Option<string?>("--selector") { Description = "Selector expression" };
    var liveOpt = new Option<bool>("--live") { Description = "Use live UIA evaluation for selector" };
    liveOpt.DefaultValueFactory = _ => false;

    cmd.Add(refOpt);
    cmd.Add(snapshotIdOpt);
    cmd.Add(selectorOpt);
    cmd.Add(liveOpt);

    return new SelectionOptions(refOpt, snapshotIdOpt, selectorOpt, liveOpt);
  }

  private static bool TryParseSelection(
    ParseResult parse,
    SelectionOptions o,
    out ElementRef? elementRef,
    out Selector? selector,
    out string? error)
  {
    elementRef = null;
    selector = null;
    error = null;

    var refId = parse.GetValue(o.Ref);
    var snapshotId = parse.GetValue(o.SnapshotId);
    var selectorExpr = parse.GetValue(o.Selector);
    var live = parse.GetValue(o.Live);

    var hasRef = !string.IsNullOrWhiteSpace(refId);
    var hasSelector = !string.IsNullOrWhiteSpace(selectorExpr);
    if (hasRef == hasSelector)
    {
      error = "Provide exactly one of --ref or --selector.";
      return false;
    }

    if (!string.IsNullOrWhiteSpace(snapshotId) && !hasRef)
    {
      error = "--snapshotId requires --ref.";
      return false;
    }

    if (hasRef)
    {
      elementRef = new ElementRef(refId!.Trim(), string.IsNullOrWhiteSpace(snapshotId) ? null : snapshotId!.Trim());
      return true;
    }

    selector = new Selector((selectorExpr ?? "").Trim(), PreferCachedSnapshot: !live);
    return true;
  }

  private static ActionMethod ParseMethod(string? raw)
  {
    if (string.Equals(raw, "uia", StringComparison.OrdinalIgnoreCase)) return ActionMethod.Uia;
    if (string.Equals(raw, "input", StringComparison.OrdinalIgnoreCase)) return ActionMethod.Input;
    return ActionMethod.Auto;
  }

  private static ScrollDirection ParseScrollDirection(string? raw)
    => string.Equals(raw, "horizontal", StringComparison.OrdinalIgnoreCase)
      ? ScrollDirection.Horizontal
      : ScrollDirection.Vertical;

  private static CancellationTokenSource CreateTimeoutCts(TimeSpan timeout, CancellationToken ct)
  {
    var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    if (timeout > TimeSpan.Zero)
    {
      cts.CancelAfter(timeout);
    }

    return cts;
  }

  private static void WriteInvalidArgument(CliContext ctx, string message)
  {
    CliOutput.Write(new
    {
      ok = false,
      meta = new { traceId = ctx.TraceId },
      error = new { code = "InvalidArgument", message },
      traceId = ctx.TraceId,
    }, ctx.Format);
  }
}
