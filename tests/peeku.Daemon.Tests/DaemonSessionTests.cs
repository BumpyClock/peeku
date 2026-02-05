using peeku;
using peeku.Daemon;
using Xunit;

namespace peeku.Daemon.Tests;

/// <summary>
/// Daemon session tests for actor queue behavior.
/// </summary>
/// <example>
/// <code>
/// using var session = new DaemonSession(() => new WindowsClient());
/// var result = await session.ExecuteAsync((client, ct) => client.WindowsFocusedAsync(ct), CancellationToken.None);
/// </code>
/// </example>
public sealed class DaemonSessionTests
{
  [Fact]
  public async Task Queued_work_executes_sequentially()
  {
    using var session = new DaemonSession(() => new WindowsClient());
    var results = new List<int>();
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

    var first = session.ExecuteAsync(async (_, token) =>
    {
      results.Add(1);
      await Task.Delay(TimeSpan.FromMilliseconds(50), token);
      results.Add(2);
      return 1;
    }, cts.Token);

    var second = session.ExecuteAsync((_, token) =>
    {
      results.Add(3);
      return Task.FromResult(2);
    }, cts.Token);

    await Task.WhenAll(first, second);

    Assert.Equal(new[] { 1, 2, 3 }, results);
  }

  [Fact]
  public async Task Dispose_stops_the_actor_thread_without_hanging()
  {
    using var session = new DaemonSession(() => new WindowsClient());
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

    var work = session.ExecuteAsync(async (_, token) =>
    {
      started.SetResult(true);
      await Task.Delay(TimeSpan.FromSeconds(30), token);
      return 1;
    }, cts.Token);

    await started.Task.WaitAsync(cts.Token);

    var disposeTask = Task.Run(() => session.Dispose());
    var completed = await Task.WhenAny(disposeTask, Task.Delay(TimeSpan.FromSeconds(2), cts.Token));

    Assert.Same(disposeTask, completed);
    await Assert.ThrowsAnyAsync<TaskCanceledException>(async () => await work);
  }
}
