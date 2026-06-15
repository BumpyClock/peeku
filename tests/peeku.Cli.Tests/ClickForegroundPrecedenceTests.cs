using System.CommandLine;
using System.CommandLine.Parsing;
using peeku;
using peeku.Cli;
using Xunit;

namespace peeku.Cli.Tests;

/// <summary>
/// Tests the --method / --foreground precedence rule on the click command.
/// Rule: --method input implies --foreground=true.
///       --method input + explicit --foreground=false → InvalidArgument.
/// Uses IsImplicit to distinguish "explicitly typed on the command line" from
/// "provided by DefaultValueFactory".
/// </summary>
public sealed class ClickForegroundPrecedenceTests
{
  private static (Option<string> methodOpt, Option<bool> foregroundOpt, RootCommand root) BuildTree()
  {
    var cmd = new Command("click", "Click an element");

    var methodOpt = new Option<string>("--method") { Description = "auto|uia|input" };
    methodOpt.DefaultValueFactory = _ => "auto";

    var foregroundOpt = new Option<bool>("--foreground");
    foregroundOpt.DefaultValueFactory = _ => false;

    cmd.Add(methodOpt);
    cmd.Add(foregroundOpt);

    var root = new RootCommand();
    root.Add(cmd);
    return (methodOpt, foregroundOpt, root);
  }

  // Helper: mirror the exact Implicit check from CliActionCommands.
  // Implicit==true means value came from DefaultValueFactory, not the command line.
  private static bool WasExplicit(ParseResult pr, Option<bool> opt)
  {
    var res = pr.GetResult(opt);
    return res is not null && !res.Implicit;
  }

  [Fact]
  public void NoFlags_ForegroundIsImplicit_DefaultFalse()
  {
    var (methodOpt, foregroundOpt, root) = BuildTree();
    var pr = root.Parse(["click"]);

    // Value defaults to false but was not explicitly typed.
    Assert.False(pr.GetValue(foregroundOpt));
    Assert.False(WasExplicit(pr, foregroundOpt));
  }

  [Fact]
  public void MethodInput_WithoutExplicitForeground_NoConflict_ForegroundBecomesTrue()
  {
    var (methodOpt, foregroundOpt, root) = BuildTree();
    var pr = root.Parse(["click", "--method", "input"]);

    Assert.Equal("input", pr.GetValue(methodOpt));
    Assert.False(WasExplicit(pr, foregroundOpt)); // not explicitly supplied

    // Simulate handler: method=input, no explicit --foreground → no conflict, foreground=true.
    var methodStr = pr.GetValue(methodOpt);
    var foregroundWasExplicit = WasExplicit(pr, foregroundOpt);
    var isConflict = string.Equals(methodStr, "input", StringComparison.OrdinalIgnoreCase)
      && foregroundWasExplicit
      && !pr.GetValue(foregroundOpt);

    Assert.False(isConflict);
    // Resulting foreground = true
    var foreground = string.Equals(methodStr, "input", StringComparison.OrdinalIgnoreCase)
      ? true
      : pr.GetValue(foregroundOpt);
    Assert.True(foreground);
  }

  [Fact]
  public void MethodInput_ExplicitForegroundFalse_ConflictDetected()
  {
    var (methodOpt, foregroundOpt, root) = BuildTree();
    var pr = root.Parse(["click", "--method", "input", "--foreground", "false"]);

    Assert.Equal("input", pr.GetValue(methodOpt));
    Assert.True(WasExplicit(pr, foregroundOpt)); // explicitly typed

    // Handler detects conflict.
    var methodStr = pr.GetValue(methodOpt);
    var foregroundWasExplicit = WasExplicit(pr, foregroundOpt);
    var isConflict = string.Equals(methodStr, "input", StringComparison.OrdinalIgnoreCase)
      && foregroundWasExplicit
      && !pr.GetValue(foregroundOpt);

    Assert.True(isConflict, "--method input + --foreground=false must produce a conflict");
  }

  [Fact]
  public void MethodAuto_ExplicitForegroundTrue_NoConflict()
  {
    var (methodOpt, foregroundOpt, root) = BuildTree();
    var pr = root.Parse(["click", "--method", "auto", "--foreground", "true"]);

    Assert.Equal("auto", pr.GetValue(methodOpt));
    Assert.True(pr.GetValue(foregroundOpt));
    Assert.True(WasExplicit(pr, foregroundOpt));
  }
}
