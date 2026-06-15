namespace peeku.Daemon;

/// <summary>
/// LRU handle cache with TTL for stable ref IDs.
/// </summary>
/// <example>
/// <code>
/// var cache = new HandleIdCache&lt;string&gt;(capacity: 2, ttl: TimeSpan.FromMinutes(1));
/// var handle = cache.Store("value", stableKey: "key");
/// var ok = cache.TryGet(handle, out var value);
/// </code>
/// </example>
public sealed class HandleIdCache<T>
{
  private readonly int _capacity;
  private readonly TimeSpan _ttl;
  private readonly string _prefix;
  private readonly Func<DateTimeOffset> _clock;
  private readonly Dictionary<string, CacheEntry> _byHandle;
  private readonly Dictionary<string, string> _byStableKey;
  private readonly LinkedList<string> _lru;

  public HandleIdCache(int capacity, TimeSpan ttl, string prefix = "h:", Func<DateTimeOffset>? clock = null)
  {
    if (capacity <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be >= 1.");
    }

    if (ttl < TimeSpan.Zero)
    {
      throw new ArgumentOutOfRangeException(nameof(ttl), "TTL must be >= 0.");
    }

    if (string.IsNullOrWhiteSpace(prefix))
    {
      throw new ArgumentException("Prefix is required.", nameof(prefix));
    }

    _capacity = capacity;
    _ttl = ttl;
    _prefix = prefix.Trim();
    _clock = clock ?? (() => DateTimeOffset.UtcNow);
    _byHandle = new Dictionary<string, CacheEntry>(StringComparer.Ordinal);
    _byStableKey = new Dictionary<string, string>(StringComparer.Ordinal);
    _lru = new LinkedList<string>();
  }

  public string Store(T value, string? stableKey = null)
  {
    if (value is null)
    {
      throw new ArgumentNullException(nameof(value));
    }

    var now = _clock();
    var key = string.IsNullOrWhiteSpace(stableKey) ? null : stableKey.Trim();

    if (key is not null && _byStableKey.TryGetValue(key, out var existingHandle))
    {
      if (_byHandle.TryGetValue(existingHandle, out var existing) && !IsExpired(existing, now))
      {
        existing.Value = value;
        Touch(existing, now);
        return existingHandle;
      }

      Remove(existingHandle);
    }

    var handle = CreateHandleId();
    while (_byHandle.ContainsKey(handle))
    {
      handle = CreateHandleId();
    }

    var node = _lru.AddFirst(handle);
    var entry = new CacheEntry(value, key, node, now);
    _byHandle[handle] = entry;

    if (key is not null)
    {
      _byStableKey[key] = handle;
    }

    PruneExpired(now);
    PruneCapacity();
    return handle;
  }

  public bool TryGet(string handleId, out T value)
  {
    if (string.IsNullOrWhiteSpace(handleId))
    {
      value = default!;
      return false;
    }

    if (!IsHandleId(handleId))
    {
      value = default!;
      return false;
    }

    if (!_byHandle.TryGetValue(handleId, out var entry))
    {
      value = default!;
      return false;
    }

    var now = _clock();
    if (IsExpired(entry, now))
    {
      Remove(handleId);
      value = default!;
      return false;
    }

    Touch(entry, now);
    value = entry.Value;
    return true;
  }

  /// <summary>
  /// Resolves a value by its durable stable key (e.g. a <c>uia:pid:hash</c> id) rather than the
  /// internal <c>h:</c> handle. Routes through the same TTL/LRU touch as <see cref="TryGet"/> so an
  /// access keeps the entry warm. Returns false when the key is unknown or the entry has expired.
  /// </summary>
  public bool TryGetByStableKey(string stableKey, out T value)
  {
    if (string.IsNullOrWhiteSpace(stableKey))
    {
      value = default!;
      return false;
    }

    if (!_byStableKey.TryGetValue(stableKey.Trim(), out var handle))
    {
      value = default!;
      return false;
    }

    return TryGet(handle, out value);
  }

  public bool IsHandleId(string handleId)
  {
    if (handleId is null)
    {
      throw new ArgumentNullException(nameof(handleId));
    }

    return handleId.StartsWith(_prefix, StringComparison.OrdinalIgnoreCase);
  }

  private string CreateHandleId()
    => $"{_prefix}{Guid.NewGuid():N}";

  private void Touch(CacheEntry entry, DateTimeOffset now)
  {
    entry.LastAccess = now;
    if (entry.Node.List is not null)
    {
      _lru.Remove(entry.Node);
      _lru.AddFirst(entry.Node);
    }
  }

  private bool IsExpired(CacheEntry entry, DateTimeOffset now)
  {
    if (_ttl <= TimeSpan.Zero)
    {
      return true;
    }

    return now - entry.LastAccess > _ttl;
  }

  private void PruneExpired(DateTimeOffset now)
  {
    if (_ttl <= TimeSpan.Zero)
    {
      ClearAll();
      return;
    }

    while (_lru.Last is not null)
    {
      var handle = _lru.Last.Value;
      if (!_byHandle.TryGetValue(handle, out var entry))
      {
        _lru.RemoveLast();
        continue;
      }

      if (!IsExpired(entry, now))
      {
        break;
      }

      Remove(handle);
    }
  }

  private void PruneCapacity()
  {
    while (_byHandle.Count > _capacity && _lru.Last is not null)
    {
      Remove(_lru.Last.Value);
    }
  }

  private void ClearAll()
  {
    _byHandle.Clear();
    _byStableKey.Clear();
    _lru.Clear();
  }

  private void Remove(string handleId)
  {
    if (!_byHandle.TryGetValue(handleId, out var entry))
    {
      return;
    }

    _byHandle.Remove(handleId);

    if (entry.Node.List is not null)
    {
      _lru.Remove(entry.Node);
    }

    if (entry.StableKey is not null)
    {
      _byStableKey.Remove(entry.StableKey);
    }
  }

  private sealed class CacheEntry
  {
    public CacheEntry(T value, string? stableKey, LinkedListNode<string> node, DateTimeOffset now)
    {
      Value = value;
      StableKey = stableKey;
      Node = node ?? throw new ArgumentNullException(nameof(node));
      LastAccess = now;
    }

    public T Value { get; set; }
    public string? StableKey { get; }
    public LinkedListNode<string> Node { get; }
    public DateTimeOffset LastAccess { get; set; }
  }
}
