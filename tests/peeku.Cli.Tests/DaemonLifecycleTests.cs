using System;
using peeku.Cli;
using Xunit;

namespace peeku.Cli.Tests;

public sealed class DaemonLifecycleTests
{
  private const string EnvVar = "PEEKU_NO_DAEMON";

  // Env is process-global; one sequential test (no other test reads PEEKU_NO_DAEMON) avoids races.
  [Fact]
  public void IsDisabledByEnv_ParsesTruthyAndFalsy()
  {
    var original = Environment.GetEnvironmentVariable(EnvVar);
    try
    {
      // Falsy / absent => daemon allowed.
      Environment.SetEnvironmentVariable(EnvVar, null);
      Assert.False(DaemonLifecycle.IsDisabledByEnv());

      Environment.SetEnvironmentVariable(EnvVar, "");
      Assert.False(DaemonLifecycle.IsDisabledByEnv());

      Environment.SetEnvironmentVariable(EnvVar, "0");
      Assert.False(DaemonLifecycle.IsDisabledByEnv());

      Environment.SetEnvironmentVariable(EnvVar, "false");
      Assert.False(DaemonLifecycle.IsDisabledByEnv());

      Environment.SetEnvironmentVariable(EnvVar, "FALSE");
      Assert.False(DaemonLifecycle.IsDisabledByEnv());

      // Truthy => disabled.
      Environment.SetEnvironmentVariable(EnvVar, "1");
      Assert.True(DaemonLifecycle.IsDisabledByEnv());

      Environment.SetEnvironmentVariable(EnvVar, "true");
      Assert.True(DaemonLifecycle.IsDisabledByEnv());

      Environment.SetEnvironmentVariable(EnvVar, "yes");
      Assert.True(DaemonLifecycle.IsDisabledByEnv());

      Environment.SetEnvironmentVariable(EnvVar, " 1 "); // trimmed
      Assert.True(DaemonLifecycle.IsDisabledByEnv());
    }
    finally
    {
      Environment.SetEnvironmentVariable(EnvVar, original);
    }
  }

  [Fact]
  public void DefaultPipeName_IsUserScopedV1()
  {
    var name = DaemonLifecycle.DefaultPipeName();
    Assert.StartsWith("peeku.", name);
    Assert.EndsWith(".v1", name);
  }

  [Fact]
  public void BuildVersion_IsNonEmpty()
  {
    Assert.False(string.IsNullOrWhiteSpace(DaemonLifecycle.BuildVersion()));
  }

  // CanAutoSpawn depends on whether the daemon exe sits next to the test host; assert only that the
  // gate evaluates without throwing (the value itself is environment-dependent and not asserted).
  [Fact]
  public void CanAutoSpawn_DoesNotThrow()
  {
    var ex = Record.Exception(() => DaemonLifecycle.CanAutoSpawn());
    Assert.Null(ex);
  }
}
