using System;
using peeku.Cli;
using Xunit;

namespace peeku.Cli.Tests;

public sealed class CliTimeoutTests
{
  [Theory]
  [InlineData("5000", 5000)]      // bare integer => milliseconds (the headline fix)
  [InlineData("0", 0)]
  [InlineData("250", 250)]
  [InlineData("500ms", 500)]
  [InlineData("5s", 5000)]
  [InlineData("2m", 120000)]
  [InlineData("00:00:10", 10000)] // classic hh:mm:ss back-compat
  [InlineData("00:01:30", 90000)]
  [InlineData("1.5s", 1500)]
  public void TryParse_ValidForms(string raw, double expectedMs)
  {
    Assert.True(CliTimeout.TryParse(raw, out var ts, out var err));
    Assert.Null(err);
    Assert.Equal(expectedMs, ts.TotalMilliseconds, precision: 3);
  }

  [Theory]
  [InlineData("")]
  [InlineData("   ")]
  [InlineData(null)]
  [InlineData("-5")]
  [InlineData("abc")]
  [InlineData("5x")]
  public void TryParse_InvalidForms(string? raw)
  {
    Assert.False(CliTimeout.TryParse(raw, out _, out var err));
    Assert.NotNull(err);
  }

  [Fact]
  public void TryParse_BareInteger_IsMilliseconds_NotDays()
  {
    // Regression: TimeSpan.Parse("5000") == 5000 days; CliTimeout must read it as 5 seconds.
    Assert.True(CliTimeout.TryParse("5000", out var ts, out _));
    Assert.Equal(TimeSpan.FromSeconds(5), ts);
    Assert.NotEqual(TimeSpan.FromDays(5000), ts);
  }
}
