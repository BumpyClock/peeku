using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Linq;
using peeku.Cli;
using Xunit;

namespace peeku.Cli.Tests;

/// <summary>
/// Drift-detector for the CLI command tree: builds the RootCommand exactly as the CLI does
/// (<see cref="CliCommandTree.AddCommands"/>) and asserts the FULL set of registered command +
/// subcommand paths (recursively) matches an explicit expected set — failing on BOTH a missing
/// command and an unexpected new one.
///
/// Why this exists: a refactor once silently DELETED five commands (windows focus, press,
/// element at-point, diff, daemon) and every other test stayed green because nothing enumerated
/// the RootCommand. Adding or removing a command MUST update <c>ExpectedCommandPaths</c> in the
/// same commit. A RED test here means real drift — fix the tree (or the list) deliberately, never
/// weaken the assertion.
/// </summary>
public sealed class CliCommandTreeParityTests
{
  // Every command AND subcommand as a space-joined, fully-qualified path (parent path + child name),
  // recursively. Group nodes (windows, capture, daemon, uia, element) appear alongside their children.
  private static readonly string[] ExpectedCommandPaths =
  {
    "doctor",
    "windows", "windows list", "windows focused", "windows focus",
    "capture", "capture image",
    "daemon", "daemon start", "daemon stop", "daemon status", "daemon serve",
    "uia", "uia snapshot",
    "see",
    "find",
    "element", "element get", "element at-point",
    "click", "invoke", "set-value", "type", "scroll", "hotkey", "press",
    "observe", "wait", "watch", "batch",
    "diff",
    "window", "window move", "window resize", "window set-bounds",
    "window minimize", "window maximize", "window restore", "window close",
    "app", "app launch", "app quit", "app relaunch", "app list",
  };

  [Fact]
  public void CommandTree_MatchesExpectedPaths_NoDropsNoAdditions()
  {
    var root = new RootCommand();
    CliCommandTree.AddCommands(root);

    var actual = new HashSet<string>(StringComparer.Ordinal);
    Collect(root, prefix: "", actual);

    var expected = new HashSet<string>(ExpectedCommandPaths, StringComparer.Ordinal);

    var missing = expected.Except(actual).OrderBy(s => s, StringComparer.Ordinal).ToList();
    var extra = actual.Except(expected).OrderBy(s => s, StringComparer.Ordinal).ToList();

    Assert.True(
      missing.Count == 0 && extra.Count == 0,
      "CLI command-tree drift detected — update ExpectedCommandPaths deliberately if this is intended.\n" +
      $"  MISSING (in expected but NOT registered — a dropped command?): [{string.Join(", ", missing)}]\n" +
      $"  EXTRA   (registered but NOT in expected — a new command?):       [{string.Join(", ", extra)}]");
  }

  // Recursively collects fully-qualified paths of every subcommand so nested drops
  // (e.g. "element at-point", "windows focus", "daemon start") are caught, not just top-level.
  private static void Collect(Command command, string prefix, HashSet<string> set)
  {
    foreach (var sub in command.Subcommands)
    {
      var path = prefix.Length == 0 ? sub.Name : $"{prefix} {sub.Name}";
      set.Add(path);
      Collect(sub, path, set);
    }
  }
}
