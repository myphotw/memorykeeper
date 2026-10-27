using MemoryKeeper.Application.Interfaces;

namespace MemoryKeeper.App.Services;

/// <summary>
/// Shares one generation of an asynchronous read between callers. A caller may cancel
/// its wait without cancelling the read another page is already using.
/// </summary>
internal sealed class GenerationAwareAsyncCache<T> : IDisposable
{
    private readonly object _gate = new();
    private readonly ICatalogInvalidation _invalidation;
    private readonly CatalogSurface _surfaces;
    private readonly Func<CancellationToken, Task<T>> _factory;
    private long _version;
    private long _cachedVersion = -1;
    private long _inFlightVersion = -1;
    private bool _hasValue;
    private T? _value;
    private Task<CacheLoadResult<T>>? _inFlight;

    public GenerationAwareAsyncCache(
        ICatalogInvalidation invalidation,
        CatalogSurface surfaces,
        Func<CancellationToken, Task<T>> factory)
    {
        _invalidation = invalidation;
        _surfaces = surfaces;
        _factory = factory;
        _invalidation.Invalidated += OnInvalidated;
    }

    public async Task<T> GetAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            Task<CacheLoadResult<T>> load;
            long requestedVersion;
            lock (_gate)
            {
                requestedVersion = _version;
                if (_hasValue && _cachedVersion == requestedVersion)
                {
                    return _value!;
                }

                if (_inFlight is not null && _inFlightVersion == requestedVersion)
                {
                    load = _inFlight;
                }
                else
                {
                    _inFlightVersion = requestedVersion;
                    load = LoadAsync(requestedVersion);
                    _inFlight = load;
                }
            }

            var result = await load.WaitAsync(cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                if (result.Version == _version && result.Accepted)
                {
                    return result.Value;
                }
            }

            // An invalidation raced this read. Retry against the current generation so
            // stale preload data can never overwrite a successful place/gallery change.
        }
    }

    private async Task<CacheLoadResult<T>> LoadAsync(long version)
    {
        try
        {
            // Ensure the in-flight task is published under the gate before a synchronously
            // completing (or throwing) repository implementation can enter the cleanup.
            await Task.Yield();
            var value = await _factory(CancellationToken.None).ConfigureAwait(false);
            lock (_gate)
            {
                var accepted = version == _version;
                if (accepted)
                {
                    _value = value;
                    _hasValue = true;
                    _cachedVersion = version;
                }

                return new CacheLoadResult<T>(version, value, accepted);
            }
        }
        finally
        {
            lock (_gate)
            {
                if (_inFlightVersion == version)
                {
                    _inFlight = null;
                    _inFlightVersion = -1;
                }
            }
        }
    }

    private void OnInvalidated(object? sender, CatalogInvalidatedEventArgs e)
    {
        if ((e.Surfaces & _surfaces) == 0)
        {
            return;
        }

        lock (_gate)
        {
            _version++;
            _hasValue = false;
            _value = default;
            _cachedVersion = -1;
        }
    }

    public void Dispose() => _invalidation.Invalidated -= OnInvalidated;

    private sealed record CacheLoadResult<TValue>(long Version, TValue Value, bool Accepted);
}
