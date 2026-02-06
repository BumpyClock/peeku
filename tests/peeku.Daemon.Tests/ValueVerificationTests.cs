using peeku.Daemon;
using Xunit;

namespace peeku.Daemon.Tests;

public sealed class ValueVerificationTests
{
  [Fact]
  public void EvaluateValueVerification_supported_and_matching_returns_success()
  {
    var outcome = DaemonPeekuClient.EvaluateValueVerification(
      operation: "set-value",
      valuePatternSupported: true,
      expectedValue: "hello",
      actualValue: "hello");

    Assert.True(outcome.Ok);
    Assert.Null(outcome.Error);
    Assert.Null(outcome.Warning);
    Assert.Equal("set-value", outcome.Evidence.GetProperty("operation").GetString());
    Assert.Equal("matched", outcome.Evidence.GetProperty("status").GetString());
    Assert.True(outcome.Evidence.GetProperty("valuePatternSupported").GetBoolean());
    Assert.Equal("hello", outcome.Evidence.GetProperty("expectedValue").GetString());
    Assert.Equal("hello", outcome.Evidence.GetProperty("actualValue").GetString());
    Assert.True(outcome.Evidence.GetProperty("verificationPerformed").GetBoolean());
    Assert.True(outcome.Evidence.GetProperty("verificationMatched").GetBoolean());
  }

  [Fact]
  public void EvaluateValueVerification_supported_but_mismatch_returns_failure()
  {
    var outcome = DaemonPeekuClient.EvaluateValueVerification(
      operation: "type",
      valuePatternSupported: true,
      expectedValue: "hello",
      actualValue: "hullo");

    Assert.False(outcome.Ok);
    Assert.NotNull(outcome.Error);
    Assert.Equal("Internal", outcome.Error!.Code);
    Assert.Equal("type verification failed: expected 'hello', actual 'hullo'.", outcome.Error.Message);
    Assert.Equal("type", outcome.Evidence.GetProperty("operation").GetString());
    Assert.Equal("mismatch", outcome.Evidence.GetProperty("status").GetString());
    Assert.True(outcome.Evidence.GetProperty("verificationPerformed").GetBoolean());
    Assert.False(outcome.Evidence.GetProperty("verificationMatched").GetBoolean());
  }

  [Fact]
  public void EvaluateValueVerification_unsupported_returns_success_with_warning()
  {
    var outcome = DaemonPeekuClient.EvaluateValueVerification(
      operation: "set-value",
      valuePatternSupported: false,
      expectedValue: "hello",
      actualValue: null);

    Assert.True(outcome.Ok);
    Assert.Null(outcome.Error);
    Assert.Equal("ValuePattern is not supported; verification skipped.", outcome.Warning);
    Assert.Equal("unsupported", outcome.Evidence.GetProperty("status").GetString());
    Assert.False(outcome.Evidence.GetProperty("valuePatternSupported").GetBoolean());
    Assert.False(outcome.Evidence.GetProperty("verificationPerformed").GetBoolean());
    Assert.Equal(System.Text.Json.JsonValueKind.Null, outcome.Evidence.GetProperty("verificationMatched").ValueKind);
  }
}
