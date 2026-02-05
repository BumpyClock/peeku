using System.CommandLine;
using System.CommandLine.Parsing;
using peeku;

namespace peeku.Cli;

internal static class CliUiaCommands
{
  internal static void AddAll(RootCommand root)
  {
    root.Add(CreateUiaCommand());
    root.Add(CreateSeeCommand());
    root.Add(CreateFindCommand());
    root.Add(CreateElementCommand());
  }

  private static Command CreateUiaCommand()
  {
    var uia = new Command("uia", "UI Automation (UIA)");
    uia.Add(CreateUiaSnapshotCommand());
    return uia;
  }

  private static Command CreateUiaSnapshotCommand()
  {
    var cmd = new Command("snapshot", "Capture UIA snapshot tree");

    var targetOpts = CliTargets.AddTo(cmd, allowQuery: true);

    var depthOpt = new Option<int>("--depth") { Description = "Tree depth" };
    depthOpt.DefaultValueFactory = _ => 6;

    var maxNodesOpt = new Option<int>("--maxNodes") { Description = "Max nodes" };
    maxNodesOpt.DefaultValueFactory = _ => 5000;

    var propsOpt = new Option<string>("--includeProperties") { Description = "basic|all" };
    propsOpt.DefaultValueFactory = _ => "basic";
    propsOpt.Validators.Add(r =>
    {
      var v = r.GetValueOrDefault<string>() ?? "basic";
      if (!string.Equals(v, "basic", StringComparison.OrdinalIgnoreCase) &&
          !string.Equals(v, "all", StringComparison.OrdinalIgnoreCase))
      {
        r.AddError("Invalid --includeProperties. Allowed: basic|all");
      }
    });

    cmd.Add(depthOpt);
    cmd.Add(maxNodesOpt);
    cmd.Add(propsOpt);

    cmd.SetAction(async (ParseResult parse, CancellationToken ct) =>
    {
      var ctx = CliContextAccessor.Current;
      using var cts = CreateTimeoutCts(ctx.Timeout, ct);

      var target = CliTargets.ParseOrDefaultFocused(parse, targetOpts);
      var props = ParseProps(parse.GetValue(propsOpt));

      var req = new UiaSnapshotRequest(
        Target: target,
        Depth: parse.GetValue(depthOpt),
        MaxNodes: parse.GetValue(maxNodesOpt),
        IncludeProperties: props);

      var client = CliPeekuClient.CreateDefault();
      var res = await client.UiaSnapshotAsync(req, cts.Token).ConfigureAwait(false);
      CliOutput.Write(res, ctx.Format);
      return res.Ok ? 0 : 1;
    });

    return cmd;
  }

  private static Command CreateSeeCommand()
  {
    var cmd = new Command("see", "Capture image + UIA snapshot");

    var targetOpts = CliTargets.AddTo(cmd, allowQuery: true);

    var includeBase64Opt = new Option<bool>("--includeBase64") { Description = "Include base64 PNG in response" };
    includeBase64Opt.DefaultValueFactory = _ => false;

    var depthOpt = new Option<int>("--depth") { Description = "Tree depth" };
    depthOpt.DefaultValueFactory = _ => 6;

    var maxNodesOpt = new Option<int>("--maxNodes") { Description = "Max nodes" };
    maxNodesOpt.DefaultValueFactory = _ => 5000;

    var propsOpt = new Option<string>("--includeProperties") { Description = "basic|all" };
    propsOpt.DefaultValueFactory = _ => "basic";
    propsOpt.Validators.Add(r =>
    {
      var v = r.GetValueOrDefault<string>() ?? "basic";
      if (!string.Equals(v, "basic", StringComparison.OrdinalIgnoreCase) &&
          !string.Equals(v, "all", StringComparison.OrdinalIgnoreCase))
      {
        r.AddError("Invalid --includeProperties. Allowed: basic|all");
      }
    });

    cmd.Add(includeBase64Opt);
    cmd.Add(depthOpt);
    cmd.Add(maxNodesOpt);
    cmd.Add(propsOpt);

    cmd.SetAction(async (ParseResult parse, CancellationToken ct) =>
    {
      var ctx = CliContextAccessor.Current;
      using var cts = CreateTimeoutCts(ctx.Timeout, ct);

      var target = CliTargets.ParseOrDefaultFocused(parse, targetOpts);
      var props = ParseProps(parse.GetValue(propsOpt));

      var req = new SeeRequest(
        Target: target,
        Depth: parse.GetValue(depthOpt),
        MaxNodes: parse.GetValue(maxNodesOpt),
        IncludeBase64: parse.GetValue(includeBase64Opt),
        IncludeProperties: props);

      var client = CliPeekuClient.CreateDefault();
      var res = await client.SeeAsync(req, cts.Token).ConfigureAwait(false);
      CliOutput.Write(res, ctx.Format);
      return res.Ok ? 0 : 1;
    });

    return cmd;
  }

  private static Command CreateFindCommand()
  {
    var cmd = new Command("find", "Find elements by selector");

    var targetOpts = CliTargets.AddTo(cmd, allowQuery: true);

    var selectorOpt = new Option<string>("--selector") { Description = "Selector expression" };
    selectorOpt.Required = true;

    var liveOpt = new Option<bool>("--live") { Description = "Use live UIA evaluation for selector" };
    liveOpt.DefaultValueFactory = _ => false;

    var limitOpt = new Option<int>("--limit") { Description = "Max matches" };
    limitOpt.DefaultValueFactory = _ => 20;

    cmd.Add(selectorOpt);
    cmd.Add(liveOpt);
    cmd.Add(limitOpt);

    cmd.SetAction(async (ParseResult parse, CancellationToken ct) =>
    {
      var ctx = CliContextAccessor.Current;
      using var cts = CreateTimeoutCts(ctx.Timeout, ct);

      var selectorRaw = parse.GetValue(selectorOpt) ?? "";
      var selector = new Selector(selectorRaw.Trim(), PreferCachedSnapshot: !parse.GetValue(liveOpt));
      var target = CliTargets.ParseOptional(parse, targetOpts);

      var req = new FindRequest(
        Selector: selector,
        Target: target,
        Limit: parse.GetValue(limitOpt));

      var client = CliPeekuClient.CreateDefault();
      var res = await client.FindAsync(req, cts.Token).ConfigureAwait(false);
      CliOutput.Write(res, ctx.Format);
      return res.Ok ? 0 : 1;
    });

    return cmd;
  }

  private static Command CreateElementCommand()
  {
    var element = new Command("element", "Element inspection");
    element.Add(CreateElementGetCommand());
    return element;
  }

  private static Command CreateElementGetCommand()
  {
    var cmd = new Command("get", "Get element properties/patterns by ref or selector");

    var targetOpts = CliTargets.AddTo(cmd, allowQuery: true);

    var refOpt = new Option<string?>("--ref") { Description = "Element refId" };
    var snapshotIdOpt = new Option<string?>("--snapshotId") { Description = "Optional snapshotId for elementRef" };

    var selectorOpt = new Option<string?>("--selector") { Description = "Selector expression" };
    var liveOpt = new Option<bool>("--live") { Description = "Use live UIA evaluation for selector" };
    liveOpt.DefaultValueFactory = _ => false;

    var propsOpt = new Option<string>("--includeProperties") { Description = "basic|all" };
    propsOpt.DefaultValueFactory = _ => "all";
    propsOpt.Validators.Add(r =>
    {
      var v = r.GetValueOrDefault<string>() ?? "all";
      if (!string.Equals(v, "basic", StringComparison.OrdinalIgnoreCase) &&
          !string.Equals(v, "all", StringComparison.OrdinalIgnoreCase))
      {
        r.AddError("Invalid --includeProperties. Allowed: basic|all");
      }
    });

    cmd.Add(refOpt);
    cmd.Add(snapshotIdOpt);
    cmd.Add(selectorOpt);
    cmd.Add(liveOpt);
    cmd.Add(propsOpt);

    cmd.SetAction(async (ParseResult parse, CancellationToken ct) =>
    {
      var ctx = CliContextAccessor.Current;
      using var cts = CreateTimeoutCts(ctx.Timeout, ct);

      var refId = parse.GetValue(refOpt);
      var snapshotId = parse.GetValue(snapshotIdOpt);
      var selectorExpr = parse.GetValue(selectorOpt);

      var elementRef = string.IsNullOrWhiteSpace(refId) ? null : new ElementRef(refId!.Trim(), string.IsNullOrWhiteSpace(snapshotId) ? null : snapshotId!.Trim());
      var selector = string.IsNullOrWhiteSpace(selectorExpr) ? null : new Selector(selectorExpr!.Trim(), PreferCachedSnapshot: !parse.GetValue(liveOpt));

      var target = CliTargets.ParseOptional(parse, targetOpts);
      var props = ParseProps(parse.GetValue(propsOpt));

      var req = new ElementGetRequest(
        Element: elementRef,
        Selector: selector,
        Target: target,
        IncludeProperties: props);

      var client = CliPeekuClient.CreateDefault();
      var res = await client.ElementGetAsync(req, cts.Token).ConfigureAwait(false);
      CliOutput.Write(res, ctx.Format);
      return res.Ok ? 0 : 1;
    });

    return cmd;
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

  private static UiaPropertiesMode ParseProps(string? raw)
    => string.Equals(raw, "all", StringComparison.OrdinalIgnoreCase)
      ? UiaPropertiesMode.All
      : UiaPropertiesMode.Basic;
}
