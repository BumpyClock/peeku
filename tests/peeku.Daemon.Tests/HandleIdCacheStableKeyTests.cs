using peeku.Daemon;
using Xunit;

namespace peeku.Daemon.Tests;

/// <summary>
/// Tests for <see cref="HandleIdCache{T}.TryGetByStableKey"/>, the durable-id lookup added for FIX B.
/// </summary>
/// <example>
/// <code>
/// var cache = new HandleIdCache&lt;string&gt;(capacity: 2, ttl: TimeSpan.FromMinutes(1));
/// _ = cache.Store("value", stableKey: "uia:1:abc");
/// var ok = cache.TryGetByStableKey("uia:1:abc", out var value);
/// </code>
/// </example>
public sealed class HandleIdCacheStableKeyTests
{
  [Fact]
  public void TryGetByStableKey_resolves_a_value_stored_under_its_durable_key()
  {
    var now = new DateTimeOffset(2026, 2, 5, 0, 0, 0, TimeSpan.Zero);
    var cache = new HandleIdCache<string>(capacity: 4, ttl: TimeSpan.FromMinutes(1), clock: () => now);

    _ = cache.Store("alpha", stableKey: "uia:100:deadbeef");

    var ok = cache.TryGetByStableKey("uia:100:deadbeef", out var value);

    Assert.True(ok);
    Assert.Equal("alpha", value);
  }

  [Fact]
  public void TryGetByStableKey_returns_false_for_an_unknown_key()
  {
    var cache = new HandleIdCache<string>(capacity: 4, ttl: TimeSpan.FromMinutes(1));

    var ok = cache.TryGetByStableKey("uia:1:nope", out var value);

    Assert.False(ok);
    Assert.Null(value);
  }

  [Fact]
  public void TryGetByStableKey_returns_false_after_the_entry_expires()
  {
    var now = new DateTimeOffset(2026, 2, 5, 0, 0, 0, TimeSpan.Zero);
    var cache = new HandleIdCache<string>(capacity: 4, ttl: TimeSpan.FromSeconds(1), clock: () => now);

    _ = cache.Store("alpha", stableKey: "uia:1:abc");

    now = now.AddSeconds(2);

    var ok = cache.TryGetByStableKey("uia:1:abc", out _);

    Assert.False(ok);
  }

  [Fact]
  public void TryGetByStableKey_touches_the_entry_so_it_survives_a_later_capacity_eviction()
  {
    var now = new DateTimeOffset(2026, 2, 5, 0, 0, 0, TimeSpan.Zero);
    var cache = new HandleIdCache<string>(capacity: 2, ttl: TimeSpan.FromMinutes(1), clock: () => now);

    _ = cache.Store("alpha", stableKey: "uia:1:a");
    _ = cache.Store("bravo", stableKey: "uia:1:b");

    // Touch alpha by stable key so bravo becomes the least-recently-used entry.
    _ = cache.TryGetByStableKey("uia:1:a", out _);

    _ = cache.Store("charlie", stableKey: "uia:1:c");

    Assert.True(cache.TryGetByStableKey("uia:1:a", out var a));
    Assert.False(cache.TryGetByStableKey("uia:1:b", out _));
    Assert.True(cache.TryGetByStableKey("uia:1:c", out var c));
    Assert.Equal("alpha", a);
    Assert.Equal("charlie", c);
  }

  [Fact]
  public void Capacity_at_least_MaxNodes_keeps_the_first_stored_node_reachable()
  {
    // FIX A cache-tuning guard: a single snapshot stores up to SnapshotMaxNodes (5000) handles. With
    // capacity >= that ceiling, the first-stored node (root) is still reachable after the last store.
    var now = new DateTimeOffset(2026, 2, 5, 0, 0, 0, TimeSpan.Zero);
    var cache = new HandleIdCache<int>(capacity: DaemonPeekuClient.HandleCacheCapacity, ttl: TimeSpan.FromMinutes(5), clock: () => now);

    var firstHandle = cache.Store(0, stableKey: "uia:1:node-0");
    for (var i = 1; i < DaemonPeekuClient.SnapshotMaxNodes; i++)
    {
      _ = cache.Store(i, stableKey: $"uia:1:node-{i}");
    }

    Assert.True(DaemonPeekuClient.HandleCacheCapacity >= DaemonPeekuClient.SnapshotMaxNodes);
    Assert.True(cache.TryGet(firstHandle, out var firstValue));
    Assert.Equal(0, firstValue);
    Assert.True(cache.TryGetByStableKey("uia:1:node-0", out var firstByKey));
    Assert.Equal(0, firstByKey);
  }
}
