using peeku;
using peeku.Cli;
using Xunit;

namespace peeku.Cli.Tests;

public sealed class ExitCodesTests
{
  private static PeekuError CanceledError => new PeekuError("Canceled", "Operation was canceled.", null);

  [Fact]
  public void Canceled_with_deadline_elapsed_returns_Timeout()
  {
    var code = ExitCodes.For(CanceledError, deadlineElapsed: true);
    Assert.Equal(ExitCodes.Timeout, code);
  }

  [Fact]
  public void Canceled_without_deadline_elapsed_returns_Canceled()
  {
    var code = ExitCodes.For(CanceledError, deadlineElapsed: false);
    Assert.Equal(ExitCodes.Canceled, code);
  }

  [Fact]
  public void One_arg_overload_returns_Canceled_for_Canceled_error()
  {
    var code = ExitCodes.For(CanceledError);
    Assert.Equal(ExitCodes.Canceled, code);
  }

  [Fact]
  public void Non_Canceled_code_is_unaffected_by_deadline_flag()
  {
    var notFoundError = new PeekuError("NotFound", "Not found.", null);
    Assert.Equal(ExitCodes.NotFound, ExitCodes.For(notFoundError, deadlineElapsed: true));
    Assert.Equal(ExitCodes.NotFound, ExitCodes.For(notFoundError, deadlineElapsed: false));
  }

  [Fact]
  public void Null_error_returns_GenericFailure_regardless_of_flag()
  {
    Assert.Equal(ExitCodes.GenericFailure, ExitCodes.For(null, deadlineElapsed: true));
    Assert.Equal(ExitCodes.GenericFailure, ExitCodes.For(null, deadlineElapsed: false));
  }

  [Fact]
  public void TimeoutScope_DeadlineElapsed_false_when_invocation_token_fires()
  {
    using var cts = new CancellationTokenSource();
    using var scope = TimeoutScope.Create(TimeSpan.FromSeconds(30), cts.Token);

    cts.Cancel(); // simulate Ctrl-C

    Assert.False(scope.DeadlineElapsed);
  }

  [Fact]
  public void TimeoutScope_DeadlineElapsed_true_when_only_deadline_fires()
  {
    using var scope = TimeoutScope.Create(TimeSpan.FromMilliseconds(1), CancellationToken.None);

    // spin-wait for the CancelAfter to fire
    var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
    while (!scope.Token.IsCancellationRequested && DateTimeOffset.UtcNow < deadline)
    {
      Thread.Sleep(5);
    }

    Assert.True(scope.DeadlineElapsed);
  }

  [Fact]
  public void TimeoutScope_no_timeout_does_not_cancel()
  {
    using var scope = TimeoutScope.Create(TimeSpan.Zero, CancellationToken.None);
    Assert.False(scope.Token.IsCancellationRequested);
  }
}
