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
    root.Add(CreatePressCommand());
  }

  private static Command CreateClickCommand()
  {
    var cmd = new Command("click", "Click an element");
    var targetOpts = CliTargets.AddTo(cmd, allowQuery: true);

    var selection = CliSelection.AddTo(cmd);

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

    var foregroundOpt = new Option<bool>("--foreground") { Description = "Use SendInput synthetic mouse click (steals + restores foreground)" };
    foregroundOpt.DefaultValueFactory = _ => false;

    var xOpt = new Option<int?>("--x") { Description = "Window-relative X coordinate (physical pixels); use with --y" };
    var yOpt = new Option<int?>("--y") { Description = "Window-relative Y coordinate (physical pixels); use with --x" };
    var globalCoordsOpt = new Option<bool>("--globalCoords") { Description = "Treat --x/--y as screen-absolute physical pixels (skip window offset)" };
    globalCoordsOpt.DefaultValueFactory = _ => false;
    var doubleOpt = new Option<bool>("--double") { Description = "Double-click (two down/up pairs in one SendInput batch)" };
    doubleOpt.DefaultValueFactory = _ => false;
    var rightOpt = new Option<bool>("--right") { Description = "Right-button click instead of left" };
    rightOpt.DefaultValueFactory = _ => false;

    cmd.Add(methodOpt);
    cmd.Add(foregroundOpt);
    cmd.Add(xOpt);
    cmd.Add(yOpt);
    cmd.Add(globalCoordsOpt);
    cmd.Add(doubleOpt);
    cmd.Add(rightOpt);

    cmd.SetAction(async (ParseResult parse, CancellationToken ct) =>
    {
      var ctx = CliContextAccessor.Current;
      using var scope = TimeoutScope.Create(ctx.Timeout, ct);

      var xVal = parse.GetValue(xOpt);
      var yVal = parse.GetValue(yOpt);
      var hasCoords = xVal.HasValue && yVal.HasValue;

      // Exactly-one-of {coords, element/selector}: both → InvalidArgument.
      var hasXY = xVal.HasValue || yVal.HasValue;
      if (!CliSelection.TryParseOptional(parse, selection, out var elementRef, out var selector, out var selectionError))
      {
        return CliErrors.Write(ctx, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, selectionError ?? "Invalid selection."));
      }

      var hasSelection = elementRef is not null || selector is not null;
      if (hasCoords && hasSelection)
      {
        return CliErrors.Write(ctx, PeekuErrors.Create(PeekuErrorCode.InvalidArgument,
          "Provide coordinates (--x/--y) OR an element/selector, not both."));
      }

      // --x without --y (or vice versa) is not useful.
      if (xVal.HasValue != yVal.HasValue)
      {
        return CliErrors.Write(ctx, PeekuErrors.Create(PeekuErrorCode.InvalidArgument,
          "Provide both --x and --y together."));
      }

      if (!CliTargets.TryParseOptional(parse, targetOpts, out var target, out var targetError))
      {
        return CliErrors.Write(ctx, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, targetError ?? "Invalid target."));
      }

      var method = ParseMethod(parse.GetValue(methodOpt));

      // Precedence: --method input implies --foreground=true.
      // Explicit --foreground=false + --method input is a contradiction → InvalidArgument.
      // IsImplicit==false means the token was actually on the command line (not from DefaultValueFactory).
      bool foreground;
      var foregroundResult = parse.GetResult(foregroundOpt);
      var foregroundWasExplicit = foregroundResult is not null && !foregroundResult.Implicit;
      if (method == ActionMethod.Input)
      {
        if (foregroundWasExplicit && !parse.GetValue(foregroundOpt))
        {
          return CliErrors.Write(ctx, PeekuErrors.Create(PeekuErrorCode.InvalidArgument,
            "--method input implies --foreground; cannot combine with --foreground=false"));
        }

        foreground = true;
      }
      else
      {
        foreground = parse.GetValue(foregroundOpt);
      }

      var client = CliPeekuClient.CreateDefault();
      var res = await client.ClickAsync(new ClickRequest(
        Element: elementRef,
        Selector: selector,
        Target: target,
        Method: method,
        Foreground: foreground,
        X: xVal,
        Y: yVal,
        GlobalCoords: parse.GetValue(globalCoordsOpt),
        Double: parse.GetValue(doubleOpt),
        Right: parse.GetValue(rightOpt)), scope.Token).ConfigureAwait(false);

      CliOutput.Write(res, ctx.Format);
      return res.Ok ? 0 : ExitCodes.For(res.Error, scope.DeadlineElapsed);
    });

    return cmd;
  }

  private static Command CreateInvokeCommand()
  {
    var cmd = new Command("invoke", "Invoke an element (UIA invoke when available)");
    var targetOpts = CliTargets.AddTo(cmd, allowQuery: true);
    var selection = CliSelection.AddTo(cmd);

    cmd.SetAction(async (ParseResult parse, CancellationToken ct) =>
    {
      var ctx = CliContextAccessor.Current;
      using var scope = TimeoutScope.Create(ctx.Timeout, ct);

      if (!CliSelection.TryParseOptional(parse, selection, out var elementRef, out var selector, out var selectionError))
      {
        return CliErrors.Write(ctx, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, selectionError ?? "Invalid selection."));
      }

      if (!CliTargets.TryParseOptional(parse, targetOpts, out var target, out var targetError))
      {
        return CliErrors.Write(ctx, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, targetError ?? "Invalid target."));
      }

      var client = CliPeekuClient.CreateDefault();
      var res = await client.InvokeAsync(new InvokeRequest(
        Element: elementRef,
        Selector: selector,
        Target: target), scope.Token).ConfigureAwait(false);

      CliOutput.Write(res, ctx.Format);
      return res.Ok ? 0 : ExitCodes.For(res.Error, scope.DeadlineElapsed);
    });

    return cmd;
  }

  private static Command CreateSetValueCommand()
  {
    var cmd = new Command("set-value", "Set Value pattern text on an element");
    var targetOpts = CliTargets.AddTo(cmd, allowQuery: true);

    var selection = CliSelection.AddTo(cmd);
    var valueOpt = new Option<string>("--value") { Description = "Value text" };
    valueOpt.Required = true;

    cmd.Add(valueOpt);

    cmd.SetAction(async (ParseResult parse, CancellationToken ct) =>
    {
      var ctx = CliContextAccessor.Current;
      using var scope = TimeoutScope.Create(ctx.Timeout, ct);

      if (!CliSelection.TryParseOptional(parse, selection, out var elementRef, out var selector, out var selectionError))
      {
        return CliErrors.Write(ctx, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, selectionError ?? "Invalid selection."));
      }

      if (!CliTargets.TryParseOptional(parse, targetOpts, out var target, out var targetError))
      {
        return CliErrors.Write(ctx, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, targetError ?? "Invalid target."));
      }

      var value = parse.GetValue(valueOpt) ?? "";

      var client = CliPeekuClient.CreateDefault();
      var res = await client.SetValueAsync(new SetValueRequest(
        Element: elementRef,
        Selector: selector,
        Target: target,
        Value: value), scope.Token).ConfigureAwait(false);

      CliOutput.Write(res, ctx.Format);
      return res.Ok ? 0 : ExitCodes.For(res.Error, scope.DeadlineElapsed);
    });

    return cmd;
  }

  private static Command CreateTypeCommand()
  {
    var cmd = new Command("type", "Type text into an element");
    var targetOpts = CliTargets.AddTo(cmd, allowQuery: true);

    // type's positional is TEXT (maps to --text), not a query selector (plan §3 `type TEXT?`).
    var selection = CliSelection.AddTo(cmd, withPositionalQuery: false);

    var textOpt = new Option<string?>("--text") { Description = "Text to type" };

    var textArg = new Argument<string?>("text")
    {
      Description = "Text to type (positional shorthand for --text)",
      Arity = ArgumentArity.ZeroOrOne,
    };

    var appendOpt = new Option<bool>("--append") { Description = "Append to existing text (default true)" };
    appendOpt.DefaultValueFactory = _ => true;

    var delayOpt = new Option<int?>("--delay-ms") { Description = "Optional inter-key delay (ms)" };

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

    var foregroundOpt = new Option<bool>("--foreground") { Description = "Use SendInput synthetic UNICODE keystrokes (steals + restores foreground). Works on password/UIA-blocked fields. Implied by --method input." };
    foregroundOpt.DefaultValueFactory = _ => false;

    cmd.Add(textOpt);
    cmd.Add(textArg);
    cmd.Add(appendOpt);
    cmd.Add(delayOpt);
    cmd.Add(methodOpt);
    cmd.Add(foregroundOpt);

    cmd.SetAction(async (ParseResult parse, CancellationToken ct) =>
    {
      var ctx = CliContextAccessor.Current;
      using var scope = TimeoutScope.Create(ctx.Timeout, ct);

      // type's positional is TEXT, not a selector: --ref/--selector are OPTIONAL (both may be
      // null), so `type "hello" --app notepad` types into the target-resolved window.
      if (!CliSelection.TryParseOptionalElement(parse, selection, out var elementRef, out var selector, out var selectionError))
      {
        return CliErrors.Write(ctx, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, selectionError ?? "Invalid selection."));
      }

      if (!CliTargets.TryParseOptional(parse, targetOpts, out var target, out var targetError))
      {
        return CliErrors.Write(ctx, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, targetError ?? "Invalid target."));
      }

      // Precedence: --text > positional text.
      var textFlag = parse.GetValue(textOpt);
      var textPositional = parse.GetValue(textArg);
      if (!string.IsNullOrEmpty(textFlag) && !string.IsNullOrEmpty(textPositional))
      {
        return CliErrors.Write(ctx, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Provide text via positional argument or --text, not both."));
      }

      var text = !string.IsNullOrEmpty(textFlag) ? textFlag : textPositional;
      if (string.IsNullOrEmpty(text))
      {
        return CliErrors.Write(ctx, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Provide text via positional argument or --text."));
      }

      var append = parse.GetValue(appendOpt);

      var method = ParseMethod(parse.GetValue(methodOpt));

      // Precedence: --method input implies --foreground=true.
      // Explicit --foreground=false + --method input is a contradiction → InvalidArgument.
      bool foreground;
      var foregroundResult = parse.GetResult(foregroundOpt);
      var foregroundWasExplicit = foregroundResult is not null && !foregroundResult.Implicit;
      if (method == ActionMethod.Input)
      {
        if (foregroundWasExplicit && !parse.GetValue(foregroundOpt))
        {
          return CliErrors.Write(ctx, PeekuErrors.Create(PeekuErrorCode.InvalidArgument,
            "--method input implies --foreground; cannot combine with --foreground=false"));
        }

        foreground = true;
      }
      else
      {
        foreground = parse.GetValue(foregroundOpt);
      }

      var client = CliPeekuClient.CreateDefault();
      var res = await client.TypeAsync(new TypeRequest(
        Element: elementRef,
        Selector: selector,
        Target: target,
        Text: text,
        Append: append,
        DelayMs: parse.GetValue(delayOpt),
        Method: method,
        Foreground: foreground), scope.Token).ConfigureAwait(false);

      CliOutput.Write(res, ctx.Format);
      return res.Ok ? 0 : ExitCodes.For(res.Error, scope.DeadlineElapsed);
    });

    return cmd;
  }

  private static Command CreateScrollCommand()
  {
    var cmd = new Command("scroll", "Scroll an element");
    var targetOpts = CliTargets.AddTo(cmd, allowQuery: true);

    var selection = CliSelection.AddTo(cmd);

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
      using var scope = TimeoutScope.Create(ctx.Timeout, ct);

      if (!CliSelection.TryParseOptional(parse, selection, out var elementRef, out var selector, out var selectionError))
      {
        return CliErrors.Write(ctx, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, selectionError ?? "Invalid selection."));
      }

      if (!CliTargets.TryParseOptional(parse, targetOpts, out var target, out var targetError))
      {
        return CliErrors.Write(ctx, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, targetError ?? "Invalid target."));
      }

      var delta = parse.GetValue(deltaOpt);
      var lines = parse.GetValue(linesOpt);
      if (delta is null == lines is null)
      {
        return CliErrors.Write(ctx, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Provide exactly one of --delta or --lines."));
      }

      var direction = ParseScrollDirection(parse.GetValue(directionOpt));

      var client = CliPeekuClient.CreateDefault();
      var res = await client.ScrollAsync(new ScrollRequest(
        Element: elementRef,
        Selector: selector,
        Target: target,
        Delta: delta,
        Lines: lines,
        Direction: direction), scope.Token).ConfigureAwait(false);

      CliOutput.Write(res, ctx.Format);
      return res.Ok ? 0 : ExitCodes.For(res.Error, scope.DeadlineElapsed);
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
      using var scope = TimeoutScope.Create(ctx.Timeout, ct);

      var keys = parse.GetValue(keysOpt) ?? "";
      var client = CliPeekuClient.CreateDefault();
      var res = await client.HotkeyAsync(new HotkeyRequest(Keys: keys), scope.Token).ConfigureAwait(false);

      CliOutput.Write(res, ctx.Format);
      return res.Ok ? 0 : ExitCodes.For(res.Error, scope.DeadlineElapsed);
    });

    return cmd;
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

  private static Command CreatePressCommand()
  {
    var cmd = new Command("press", "Press one or more named keys in sequence (e.g. enter, tab, down, f5)");

    // Positional keys (one or more). Distinct from hotkey: these are discrete keys, not a chord.
    var keysArg = new Argument<string[]>("keys")
    {
      Description = "Named keys to press in order, e.g. enter | tab tab | down",
      Arity = ArgumentArity.OneOrMore,
    };

    var countOpt = new Option<int>("--count") { Description = "Repeat the whole key sequence N times" };
    countOpt.DefaultValueFactory = _ => 1;

    var delayOpt = new Option<int?>("--delay-ms") { Description = "Delay between keys (ms)" };
    var holdOpt = new Option<int?>("--hold-ms") { Description = "Hold each key down this long before release (ms)" };

    cmd.Add(keysArg);
    cmd.Add(countOpt);
    cmd.Add(delayOpt);
    cmd.Add(holdOpt);

    // Target flags so press can focus a specific window before sending keys.
    var targetOpts = CliTargets.AddTo(cmd, allowQuery: true);

    cmd.SetAction(async (ParseResult parse, CancellationToken ct) =>
    {
      var ctx = CliContextAccessor.Current;
      using var scope = TimeoutScope.Create(ctx.Timeout, ct);

      var keys = parse.GetValue(keysArg) ?? Array.Empty<string>();
      if (keys.Length == 0)
      {
        return CliErrors.Write(ctx, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "At least one key is required."));
      }

      if (!CliTargets.TryParseOptional(parse, targetOpts, out var target, out var targetError))
      {
        return CliErrors.Write(ctx, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, targetError ?? "Invalid target."));
      }

      var client = CliPeekuClient.CreateDefault();
      var res = await client.PressAsync(new PressRequest(
        Keys: keys,
        Count: parse.GetValue(countOpt),
        DelayMs: parse.GetValue(delayOpt),
        HoldMs: parse.GetValue(holdOpt),
        Target: target), scope.Token).ConfigureAwait(false);

      CliOutput.Write(res, ctx.Format);
      return res.Ok ? 0 : ExitCodes.For(res.Error, scope.DeadlineElapsed);
    });

    return cmd;
  }
}
