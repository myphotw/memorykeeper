using MemoryKeeper.Mobile.Http;

namespace MemoryKeeper.Mobile.Configuration;

public sealed class MobileBackendEndpointResolver : IMobileBackendEndpointResolver, IDisposable
{
    internal static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(2);

    private readonly IMobileBackendConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMobileNetworkChangeSource _networkChangeSource;
    private readonly TimeSpan _probeTimeout;
    private readonly TimeSpan _cacheDuration;
    private readonly SemaphoreSlim _selectionGate = new(1, 1);
    private readonly object _stateLock = new();
    private Uri? _cachedBaseUri;
    private DateTimeOffset _cacheExpiresAt;
    private long _generation;
    private bool _disposed;

    public MobileBackendEndpointResolver(
        IMobileBackendConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        IMobileNetworkChangeSource networkChangeSource)
        : this(configuration, httpClientFactory, networkChangeSource, ProbeTimeout, CacheDuration)
    {
    }

    internal MobileBackendEndpointResolver(
        IMobileBackendConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        IMobileNetworkChangeSource networkChangeSource,
        TimeSpan probeTimeout,
        TimeSpan cacheDuration)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _networkChangeSource = networkChangeSource;
        _probeTimeout = probeTimeout;
        _cacheDuration = cacheDuration;
        _networkChangeSource.NetworkChanged += OnNetworkChanged;
    }

    public Uri? CurrentBaseUri
    {
        get
        {
            lock (_stateLock)
            {
                if (_cachedBaseUri is not null && DateTimeOffset.UtcNow < _cacheExpiresAt)
                {
                    return _cachedBaseUri;
                }

                _cachedBaseUri = null;
                return null;
            }
        }
    }

    public async Task<Uri> ResolveAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!_configuration.IsConfigured
            || _configuration.InternalBaseUri is not { } internalBaseUri
            || _configuration.ExternalBaseUri is not { } externalBaseUri)
        {
            throw new MobileBackendConfigurationException();
        }

        var cached = CurrentBaseUri;
        if (cached is not null)
        {
            return cached;
        }

        await _selectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cached = CurrentBaseUri;
            if (cached is not null)
            {
                return cached;
            }

            var generation = Volatile.Read(ref _generation);
            var selected = await IsInternalReachableAsync(internalBaseUri)
                .ConfigureAwait(false)
                ? internalBaseUri
                : externalBaseUri;

            cancellationToken.ThrowIfCancellationRequested();
            lock (_stateLock)
            {
                if (generation == _generation)
                {
                    _cachedBaseUri = selected;
                    _cacheExpiresAt = DateTimeOffset.UtcNow + _cacheDuration;
                }
            }

            return selected;
        }
        finally
        {
            _selectionGate.Release();
        }
    }

    public void Invalidate()
    {
        lock (_stateLock)
        {
            _generation++;
            _cachedBaseUri = null;
            _cacheExpiresAt = default;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _networkChangeSource.NetworkChanged -= OnNetworkChanged;
        _selectionGate.Dispose();
    }

    private async Task<bool> IsInternalReachableAsync(Uri internalBaseUri)
    {
        using var timeout = new CancellationTokenSource(_probeTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(internalBaseUri, "health"));
        try
        {
            var client = _httpClientFactory.CreateClient(MobileHttpClientNames.Probe);
            using var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    private void OnNetworkChanged(object? sender, EventArgs args) => Invalidate();

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MobileBackendEndpointResolver));
        }
    }
}
