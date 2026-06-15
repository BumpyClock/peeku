using System.CommandLine;
using System.CommandLine.Parsing;
using peeku;

namespace peeku.Cli;

internal static class CliWindowCommands
{
  internal static Command CreateWindowCommand()
  {
    var window = new Command("window", "Window management (move, resize, state, close)");
    window.Add(CreateMoveCommand());
    window.Add(CreateResizeCommand());
    window.Add(CreateSetBoundsCommand());
    window.Add(CreateMinimizeCommand());
    window.Add(CreateMaximizeCommand());
    window.Add(CreateRestoreCommand());
    window.Add(CreateCloseCommand());
    return window;
  }

  private static Command CreateMoveCommand()
  {
    var cmd = new Command("move", "Move a window to the given screen coordinates");
    var targetOpts = CliTargets.AddTo(cmd, allowQuery: true);

    var xOpt = new Option<int>("--x") { Description = "Screen X coordinate (physical pixels)" };
    xOpt.Required = true;
    var yOpt = new Option<int>("--y") { Description = "Screen Y coordinate (physical pixels)" };
    yOpt.Required = true;

    cmd.Add(xOpt);
    cmd.Add(yOpt);

    cmd.SetAction(async (ParseResult parse, CancellationToken ct) =>
    {
      var ctx = CliContextAccessor.Current;
      using var scope = TimeoutScope.Create(ctx.Timeout, ct);

      if (!CliTargets.TryParseOrDefaultFocused(parse, targetOpts, out var target, out var targetError))
        return CliErrors.Write(ctx, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, targetError ?? "Invalid target."));

      var client = CliPeekuClient.CreateDefault();
      var res = await client.WindowMoveAsync(new WindowMoveRequest(target, parse.GetValue(xOpt), parse.GetValue(yOpt)), scope.Token).ConfigureAwait(false);
      CliOutput.Write(res, ctx.Format);
      return res.Ok ? 0 : ExitCodes.For(res.Error, scope.DeadlineElapsed);
    });

    return cmd;
  }

  private static Command CreateResizeCommand()
  {
    var cmd = new Command("resize", "Resize a window");
    var targetOpts = CliTargets.AddTo(cmd, allowQuery: true);

    var widthOpt = new Option<int>("--width") { Description = "Width (physical pixels)" };
    widthOpt.Required = true;
    var heightOpt = new Option<int>("--height") { Description = "Height (physical pixels)" };
    heightOpt.Required = true;

    cmd.Add(widthOpt);
    cmd.Add(heightOpt);

    cmd.SetAction(async (ParseResult parse, CancellationToken ct) =>
    {
      var ctx = CliContextAccessor.Current;
      using var scope = TimeoutScope.Create(ctx.Timeout, ct);

      if (!CliTargets.TryParseOrDefaultFocused(parse, targetOpts, out var target, out var targetError))
        return CliErrors.Write(ctx, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, targetError ?? "Invalid target."));

      var client = CliPeekuClient.CreateDefault();
      var res = await client.WindowResizeAsync(new WindowResizeRequest(target, parse.GetValue(widthOpt), parse.GetValue(heightOpt)), scope.Token).ConfigureAwait(false);
      CliOutput.Write(res, ctx.Format);
      return res.Ok ? 0 : ExitCodes.For(res.Error, scope.DeadlineElapsed);
    });

    return cmd;
  }

  private static Command CreateSetBoundsCommand()
  {
    var cmd = new Command("set-bounds", "Move and resize a window in one call");
    var targetOpts = CliTargets.AddTo(cmd, allowQuery: true);

    var xOpt = new Option<int>("--x") { Description = "Screen X (physical pixels)" };
    xOpt.Required = true;
    var yOpt = new Option<int>("--y") { Description = "Screen Y (physical pixels)" };
    yOpt.Required = true;
    var widthOpt = new Option<int>("--width") { Description = "Width (physical pixels)" };
    widthOpt.Required = true;
    var heightOpt = new Option<int>("--height") { Description = "Height (physical pixels)" };
    heightOpt.Required = true;

    cmd.Add(xOpt);
    cmd.Add(yOpt);
    cmd.Add(widthOpt);
    cmd.Add(heightOpt);

    cmd.SetAction(async (ParseResult parse, CancellationToken ct) =>
    {
      var ctx = CliContextAccessor.Current;
      using var scope = TimeoutScope.Create(ctx.Timeout, ct);

      if (!CliTargets.TryParseOrDefaultFocused(parse, targetOpts, out var target, out var targetError))
        return CliErrors.Write(ctx, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, targetError ?? "Invalid target."));

      var client = CliPeekuClient.CreateDefault();
      var res = await client.WindowSetBoundsAsync(
        new WindowBoundsRequest(target, parse.GetValue(xOpt), parse.GetValue(yOpt), parse.GetValue(widthOpt), parse.GetValue(heightOpt)),
        scope.Token).ConfigureAwait(false);
      CliOutput.Write(res, ctx.Format);
      return res.Ok ? 0 : ExitCodes.For(res.Error, scope.DeadlineElapsed);
    });

    return cmd;
  }

  private static Command CreateMinimizeCommand()
  {
    var cmd = new Command("minimize", "Minimize a window");
    var targetOpts = CliTargets.AddTo(cmd, allowQuery: true);

    cmd.SetAction(async (ParseResult parse, CancellationToken ct) =>
    {
      var ctx = CliContextAccessor.Current;
      using var scope = TimeoutScope.Create(ctx.Timeout, ct);

      if (!CliTargets.TryParseOrDefaultFocused(parse, targetOpts, out var target, out var targetError))
        return CliErrors.Write(ctx, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, targetError ?? "Invalid target."));

      var client = CliPeekuClient.CreateDefault();
      var res = await client.WindowMinimizeAsync(new WindowStateRequest(target), scope.Token).ConfigureAwait(false);
      CliOutput.Write(res, ctx.Format);
      return res.Ok ? 0 : ExitCodes.For(res.Error, scope.DeadlineElapsed);
    });

    return cmd;
  }

  private static Command CreateMaximizeCommand()
  {
    var cmd = new Command("maximize", "Maximize a window");
    var targetOpts = CliTargets.AddTo(cmd, allowQuery: true);

    cmd.SetAction(async (ParseResult parse, CancellationToken ct) =>
    {
      var ctx = CliContextAccessor.Current;
      using var scope = TimeoutScope.Create(ctx.Timeout, ct);

      if (!CliTargets.TryParseOrDefaultFocused(parse, targetOpts, out var target, out var targetError))
        return CliErrors.Write(ctx, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, targetError ?? "Invalid target."));

      var client = CliPeekuClient.CreateDefault();
      var res = await client.WindowMaximizeAsync(new WindowStateRequest(target), scope.Token).ConfigureAwait(false);
      CliOutput.Write(res, ctx.Format);
      return res.Ok ? 0 : ExitCodes.For(res.Error, scope.DeadlineElapsed);
    });

    return cmd;
  }

  private static Command CreateRestoreCommand()
  {
    var cmd = new Command("restore", "Restore a minimized or maximized window");
    var targetOpts = CliTargets.AddTo(cmd, allowQuery: true);

    cmd.SetAction(async (ParseResult parse, CancellationToken ct) =>
    {
      var ctx = CliContextAccessor.Current;
      using var scope = TimeoutScope.Create(ctx.Timeout, ct);

      if (!CliTargets.TryParseOrDefaultFocused(parse, targetOpts, out var target, out var targetError))
        return CliErrors.Write(ctx, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, targetError ?? "Invalid target."));

      var client = CliPeekuClient.CreateDefault();
      var res = await client.WindowRestoreAsync(new WindowStateRequest(target), scope.Token).ConfigureAwait(false);
      CliOutput.Write(res, ctx.Format);
      return res.Ok ? 0 : ExitCodes.For(res.Error, scope.DeadlineElapsed);
    });

    return cmd;
  }

  private static Command CreateCloseCommand()
  {
    var cmd = new Command("close", "Close a window gracefully (WM_CLOSE) and report whether it closed");
    var targetOpts = CliTargets.AddTo(cmd, allowQuery: true);

    var waitMsOpt = new Option<int>("--waitMs") { Description = "Max ms to wait for window to close (default 2000)" };
    waitMsOpt.DefaultValueFactory = _ => 2000;
    cmd.Add(waitMsOpt);

    cmd.SetAction(async (ParseResult parse, CancellationToken ct) =>
    {
      var ctx = CliContextAccessor.Current;
      using var scope = TimeoutScope.Create(ctx.Timeout, ct);

      if (!CliTargets.TryParseOrDefaultFocused(parse, targetOpts, out var target, out var targetError))
        return CliErrors.Write(ctx, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, targetError ?? "Invalid target."));

      var client = CliPeekuClient.CreateDefault();
      var res = await client.WindowCloseAsync(new WindowCloseRequest(target, parse.GetValue(waitMsOpt)), scope.Token).ConfigureAwait(false);
      CliOutput.Write(res, ctx.Format);
      return res.Ok ? 0 : ExitCodes.For(res.Error, scope.DeadlineElapsed);
    });

    return cmd;
  }
}
