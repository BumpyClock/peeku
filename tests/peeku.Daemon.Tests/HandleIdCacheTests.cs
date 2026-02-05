using peeku.Daemon;
using Xunit;

namespace peeku.Daemon.Tests;

/// <summary>
/// Handle cache tests for TTL and LRU behavior.
/// </summary>
/// <example>
/// <code>
/// var cache = new HandleIdCache&lt;string&gt;(capacity: 2, ttl: TimeSpan.FromMinutes(1));
/// var handle = cache.Store("value");
/// var ok = cache.TryGet(handle, out var value);
/// </code>
/// </example>
public sealed class HandleIdCacheTests
{
  [Fact]
  public void Storing_an_item_then_getting_it_returns_the_value_before_expiration()
  {
    var now = new DateTimeOffset(2026, 2, 5, 0, 0, 0, TimeSpan.Zero);
    var cache = new HandleIdCache<string>(capacity: 2, ttl: TimeSpan.FromSeconds(10), clock: () => now);

    var handle = cache.Store("alpha");

    var ok = cache.TryGet(handle, out var value);

    Assert.True(ok);
    Assert.Equal("alpha", value);
  }

  [Fact]
  public void Expired_entries_are_evicted_and_not_returned()
  {
    var now = new DateTimeOffset(2026, 2, 5, 0, 0, 0, TimeSpan.Zero);
    var cache = new HandleIdCache<string>(capacity: 2, ttl: TimeSpan.FromSeconds(1), clock: () => now);

    var handle = cache.Store("alpha");

    now = now.AddSeconds(2);

    var ok = cache.TryGet(handle, out _);

    Assert.False(ok);
  }

  [Fact]
  public void Least_recently_used_entry_is_evicted_when_capacity_is_exceeded()
  {
    var now = new DateTimeOffset(2026, 2, 5, 0, 0, 0, TimeSpan.Zero);
    var cache = new HandleIdCache<string>(capacity: 2, ttl: TimeSpan.FromMinutes(1), clock: () => now);

    var handleA = cache.Store("alpha");
    var handleB = cache.Store("bravo");

    _ = cache.TryGet(handleA, out _);

    var handleC = cache.Store("charlie");

    var okA = cache.TryGet(handleA, out var valueA);
    var okB = cache.TryGet(handleB, out _);
    var okC = cache.TryGet(handleC, out var valueC);

    Assert.True(okA);
    Assert.False(okB);
    Assert.True(okC);
    Assert.Equal("alpha", valueA);
    Assert.Equal("charlie", valueC);
  }

  [Fact]
  public void Stable_keys_reuse_the_existing_handle_and_update_the_value()
  {
    var now = new DateTimeOffset(2026, 2, 5, 0, 0, 0, TimeSpan.Zero);
    var cache = new HandleIdCache<string>(capacity: 2, ttl: TimeSpan.FromMinutes(1), clock: () => now);

    var handle1 = cache.Store("alpha", stableKey: "key-1");
    var handle2 = cache.Store("bravo", stableKey: "key-1");

    var ok = cache.TryGet(handle1, out var value);

    Assert.Equal(handle1, handle2);
    Assert.True(ok);
    Assert.Equal("bravo", value);
  }
}
