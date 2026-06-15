using peeku;
using Xunit;

namespace peeku.Core.Tests;

/// <summary>
/// Tests the pid-scoped <see cref="Win32Windows.ListWindows(WindowsListRequest, int?, CancellationToken)"/>
/// overload, which filters by process id INSIDE the EnumWindows callback (before the Limit cap) so the
/// durable refId re-walk can always reach the target pid's windows on a busy desktop.
/// </summary>
/// <example>
/// <code>
/// var wins = Win32Windows.ListWindows(new WindowsListRequest(Limit: 256), processId: 1234, CancellationToken.None);
/// </code>
/// </example>
public sealed class Win32WindowsPidFilterTests
{
  [Fact]
  public void Pid_scoped_enumeration_returns_only_the_requested_pid()
  {
    // Discover any pid that currently owns a top-level window (no app launch needed).
    var all = Win32Windows.ListWindows(
      new WindowsListRequest(Limit: 256),
      CancellationToken.None);

    if (all.Count == 0)
    {
      return; // Bare/headless desktop with no top-level windows: nothing to assert positively.
    }

    var targetPid = all[0].ProcessId;

    var scoped = Win32Windows.ListWindows(
      new WindowsListRequest(Limit: 256),
      processId: targetPid,
      CancellationToken.None);

    Assert.NotEmpty(scoped);
    Assert.All(scoped, w => Assert.Equal(targetPid, w.ProcessId));
  }

  [Fact]
  public void Pid_filter_beats_the_limit_cap_so_the_target_pid_is_never_starved()
  {
    // Pick a target pid that is NOT the first window enumerated, so an unscoped Limit:1 call would cap
    // before reaching it. The pid-scoped Limit:1 call must still find it because the filter runs first.
    var all = Win32Windows.ListWindows(
      new WindowsListRequest(Limit: 256),
      CancellationToken.None);

    int? laterPid = null;
    var firstPid = all.Count > 0 ? all[0].ProcessId : (int?)null;
    foreach (var w in all)
    {
      if (firstPid is not null && w.ProcessId != firstPid.Value)
      {
        laterPid = w.ProcessId;
        break;
      }
    }

    if (laterPid is null)
    {
      return; // Need at least two distinct pids owning top-level windows to demonstrate the cap bug.
    }

    // Unscoped Limit:1 captures only the first-enumerated window: the later pid is hidden behind the cap.
    var capped = Win32Windows.ListWindows(
      new WindowsListRequest(Limit: 1),
      CancellationToken.None);
    Assert.Single(capped);

    // Pid-scoped Limit:1 still finds the later pid because the filter is applied before the cap.
    var scoped = Win32Windows.ListWindows(
      new WindowsListRequest(Limit: 1),
      processId: laterPid.Value,
      CancellationToken.None);

    Assert.Single(scoped);
    Assert.Equal(laterPid.Value, scoped[0].ProcessId);
  }

  [Fact]
  public void Pid_scoped_enumeration_returns_empty_for_a_pid_with_no_top_level_windows()
  {
    var scoped = Win32Windows.ListWindows(
      new WindowsListRequest(Limit: 256),
      processId: int.MaxValue,
      CancellationToken.None);

    Assert.Empty(scoped);
  }
}
