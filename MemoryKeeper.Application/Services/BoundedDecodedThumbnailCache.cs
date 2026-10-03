using System.Diagnostics.CodeAnalysis;

namespace MemoryKeeper.Application.Services;

/// <summary>
/// Keeps decoded session thumbnails under an allocation-byte budget.
/// Eviction only releases the cache reference because a platform view may still display the value.
/// </summary>
public sealed class BoundedDecodedThumbnailCache<TValue>
    where TValue : class
{
    public const long DefaultMaxBytes = 48L * 1024 * 1024;

    private readonly object _gate = new();
    private readonly long _maxBytes;
    private readonly Func<TValue, long> _allocationSize;
    private readonly Dictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _leastRecentlyUsed = new();
    private long _totalBytes;

    public BoundedDecodedThumbnailCache(
        Func<TValue, long> allocationSize,
        long maxBytes = DefaultMaxBytes)
    {
        _allocationSize = allocationSize ?? throw new ArgumentNullException(nameof(allocationSize));
        if (maxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        }

        _maxBytes = maxBytes;
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    public long TotalBytes
    {
        get
        {
            lock (_gate)
            {
                return _totalBytes;
            }
        }
    }

    public bool TryGet(string key, [NotNullWhen(true)] out TValue? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var entry))
            {
                value = null;
                return false;
            }

            _leastRecentlyUsed.Remove(entry.Node);
            _leastRecentlyUsed.AddFirst(entry.Node);
            value = entry.Value;
            return true;
        }
    }

    public bool AddOrUpdate(string key, TValue value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        var size = _allocationSize(value);
        if (size <= 0 || size > _maxBytes)
        {
            return false;
        }

        lock (_gate)
        {
            if (_entries.Remove(key, out var existing))
            {
                _leastRecentlyUsed.Remove(existing.Node);
                _totalBytes -= existing.Size;
            }

            var node = _leastRecentlyUsed.AddFirst(key);
            _entries.Add(key, new CacheEntry(value, size, node));
            _totalBytes += size;

            while (_totalBytes > _maxBytes)
            {
                var oldest = _leastRecentlyUsed.Last;
                if (oldest is null)
                {
                    break;
                }

                _leastRecentlyUsed.RemoveLast();
                if (_entries.Remove(oldest.Value, out var evicted))
                {
                    _totalBytes -= evicted.Size;
                }
            }

            return _entries.ContainsKey(key);
        }
    }

    private sealed record CacheEntry(
        TValue Value,
        long Size,
        LinkedListNode<string> Node);
}
