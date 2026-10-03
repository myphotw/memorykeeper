namespace MemoryKeeper.Application.Services;

/// <summary>
/// Stores encoded thumbnail bytes in a bounded LRU and shares one active load per identity.
/// One consumer cannot cancel a load still used by others; fully abandoned loads may stop early.
/// </summary>
public sealed class BoundedThumbnailMemoryCache
{
    public const int DefaultMaxEntries = 256;
    public const long DefaultMaxBytes = 64L * 1024 * 1024;
    public const int DefaultMaxConcurrentLoads = 6;

    private readonly object _gate = new();
    private readonly int _maxEntries;
    private readonly long _maxBytes;
    private readonly Dictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, InFlightLoad> _inFlight = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _leastRecentlyUsed = new();
    private readonly SemaphoreSlim _loadGate;
    private long _totalBytes;

    public BoundedThumbnailMemoryCache(
        int maxEntries = DefaultMaxEntries,
        long maxBytes = DefaultMaxBytes,
        int maxConcurrentLoads = DefaultMaxConcurrentLoads)
    {
        if (maxEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxEntries));
        }

        if (maxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        }

        if (maxConcurrentLoads <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrentLoads));
        }

        _maxEntries = maxEntries;
        _maxBytes = maxBytes;
        _loadGate = new SemaphoreSlim(maxConcurrentLoads, maxConcurrentLoads);
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

    public async Task<byte[]?> GetOrLoadAsync(
        string key,
        Func<CancellationToken, Task<byte[]?>> loader,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(loader);
        cancellationToken.ThrowIfCancellationRequested();

        InFlightLoad sharedLoad;
        var ownsLoad = false;
        lock (_gate)
        {
            if (TryGetLocked(key, out var cached))
            {
                return cached;
            }

            if (!_inFlight.TryGetValue(key, out sharedLoad!))
            {
                sharedLoad = new InFlightLoad();
                _inFlight.Add(key, sharedLoad);
                ownsLoad = true;
            }

            sharedLoad.ConsumerCount++;
        }

        if (ownsLoad)
        {
            _ = LoadAndCompleteAsync(key, loader, sharedLoad);
        }

        try
        {
            return await sharedLoad.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ReleaseConsumer(key, sharedLoad);
        }
    }

    public bool TryGet(string key, out byte[]? bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        lock (_gate)
        {
            return TryGetLocked(key, out bytes);
        }
    }

    private async Task LoadAndCompleteAsync(
        string key,
        Func<CancellationToken, Task<byte[]?>> loader,
        InFlightLoad load)
    {
        byte[]? bytes = null;
        try
        {
            await _loadGate.WaitAsync(load.Cancellation.Token).ConfigureAwait(false);
            try
            {
                bytes = await loader(load.Cancellation.Token).ConfigureAwait(false);
            }
            finally
            {
                _loadGate.Release();
            }

            if (bytes is { Length: > 0 })
            {
                AddOrUpdate(key, bytes);
            }
            else
            {
                bytes = null;
            }
        }
        catch
        {
            // A failed image load remains retryable and is never inserted into the cache.
            bytes = null;
        }
        finally
        {
            lock (_gate)
            {
                if (_inFlight.TryGetValue(key, out var current)
                    && ReferenceEquals(current, load))
                {
                    _inFlight.Remove(key);
                }
            }

            load.Completion.TrySetResult(bytes);
            load.Cancellation.Dispose();
        }
    }

    private void ReleaseConsumer(string key, InFlightLoad load)
    {
        var cancelLoad = false;
        lock (_gate)
        {
            load.ConsumerCount--;
            if (load.ConsumerCount == 0 && !load.Completion.Task.IsCompleted)
            {
                if (_inFlight.TryGetValue(key, out var current)
                    && ReferenceEquals(current, load))
                {
                    _inFlight.Remove(key);
                }

                cancelLoad = true;
            }
        }

        if (cancelLoad)
        {
            try
            {
                load.Cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The shared load completed between the state check and cancellation.
            }
        }
    }

    private bool TryGetLocked(string key, out byte[]? bytes)
    {
        if (!_entries.TryGetValue(key, out var entry))
        {
            bytes = null;
            return false;
        }

        _leastRecentlyUsed.Remove(entry.Node);
        _leastRecentlyUsed.AddFirst(entry.Node);
        bytes = entry.Bytes;
        return true;
    }

    private void AddOrUpdate(string key, byte[] bytes)
    {
        if (bytes.LongLength > _maxBytes)
        {
            return;
        }

        lock (_gate)
        {
            if (_entries.Remove(key, out var existing))
            {
                _leastRecentlyUsed.Remove(existing.Node);
                _totalBytes -= existing.Bytes.LongLength;
            }

            var node = _leastRecentlyUsed.AddFirst(key);
            _entries.Add(key, new CacheEntry(bytes, node));
            _totalBytes += bytes.LongLength;

            while (_entries.Count > _maxEntries || _totalBytes > _maxBytes)
            {
                var oldest = _leastRecentlyUsed.Last;
                if (oldest is null)
                {
                    break;
                }

                _leastRecentlyUsed.RemoveLast();
                if (_entries.Remove(oldest.Value, out var evicted))
                {
                    _totalBytes -= evicted.Bytes.LongLength;
                }
            }
        }
    }

    private sealed record CacheEntry(byte[] Bytes, LinkedListNode<string> Node);

    private sealed class InFlightLoad
    {
        public TaskCompletionSource<byte[]?> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationTokenSource Cancellation { get; } = new();

        public int ConsumerCount { get; set; }
    }
}
