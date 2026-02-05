using System.Text.Json;
using peeku;
using Xunit;

namespace peeku.Core.Tests;

/// <summary>
/// Batch argument parsing tests.
/// </summary>
/// <example>
/// <code>
/// var ok = BatchArgs.TryReadSelector(JsonDocument.Parse("{}").RootElement, out var selector, out _);
/// </code>
/// </example>
public sealed class BatchArgsTests
{
  [Fact]
  public void The_selector_can_disable_cached_snapshot()
  {
    using var doc = JsonDocument.Parse("{\"selector\":{\"expr\":\"window\",\"preferCachedSnapshot\":false}}");

    var ok = BatchArgs.TryReadSelector(doc.RootElement, out var selector, out var error);

    Assert.True(ok);
    Assert.True(string.IsNullOrWhiteSpace(error));
    Assert.False(selector.PreferCachedSnapshot);
  }
}
