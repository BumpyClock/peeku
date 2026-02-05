using peeku.Cli;
using Xunit;

namespace peeku.Cli.Tests;

/// <summary>
/// Daemon marker persistence tests.
/// </summary>
/// <example>
/// <code>
/// var marker = new DaemonMarker("pipe", 1, DateTimeOffset.UtcNow, "1", "1.0.0");
/// marker.Save("path");
/// </code>
/// </example>
public sealed class DaemonMarkerTests
{
  [Fact]
  public void The_marker_can_be_saved_and_loaded()
  {
    var tempDir = Path.Combine(Path.GetTempPath(), "peeku-tests", Guid.NewGuid().ToString("n"));
    var path = Path.Combine(tempDir, "daemon.json");
    var startedAt = new DateTimeOffset(2026, 2, 5, 12, 0, 0, TimeSpan.Zero);
    var marker = new DaemonMarker("peeku.test.v1", 4242, startedAt, "1", "1.2.3");

    marker.Save(path);

    var ok = DaemonMarker.TryLoad(path, out var loaded);

    Assert.True(ok);
    Assert.Equal(marker.PipeName, loaded.PipeName);
    Assert.Equal(marker.Pid, loaded.Pid);
    Assert.Equal(marker.StartedAt, loaded.StartedAt);
    Assert.Equal(marker.ProtocolVersion, loaded.ProtocolVersion);
    Assert.Equal(marker.BuildVersion, loaded.BuildVersion);
  }

  [Fact]
  public void The_marker_try_load_returns_false_when_missing()
  {
    var tempDir = Path.Combine(Path.GetTempPath(), "peeku-tests", Guid.NewGuid().ToString("n"));
    var path = Path.Combine(tempDir, "missing.json");

    var ok = DaemonMarker.TryLoad(path, out _);

    Assert.False(ok);
  }
}
