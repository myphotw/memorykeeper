using MemoryKeeper.Mobile.Http;
using System.Net;

namespace MemoryKeeper.Mobile.Configuration;

public sealed class MobileBackendEndpointResolver :
    IMobileBackendEndpointResolver,
    IMobileBackendConnectionMonitor,
    IDisposable
{
    internal static readonly TimeSpan InternalProbeTimeout = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan ExternalProbeTimeout = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan SingleEndpointProbeTimeout = TimeSpan.FromSeconds(4);
    internal static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan NetworkChangeDebounce = TimeSpan.FromMilliseconds(150);
    private const int MaximumGenerationRestarts = 2;

    private readonly IMobileBackendConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMobileNetworkChangeSource _networkChangeSource;
    private readonly TimeSpan _internalProbeTimeout;
    private readonly TimeSpan _externalProbeTimeout;
    private readonly TimeSpan _singleEndpointProbeTimeout;
    private readonly TimeSpan _cacheDuration;
    private readonly SemaphoreSlim _selectionGate = new(1, 1);
    private readonly object _stateLock = new();
    private Uri? _cachedBaseUri;
    private MobileBackendConnectionException? _cachedFailure;
    private DateTimeOffset _cacheExpiresAt;
    private long _generation;
    private MobileBackendConnectionState _connectionState = MobileBackendConnectionState.Unknown;
    private CancellationTokenSource? _automaticReconnectCancellation;
    private bool _isForeground = true;
    private bool _disposed;

    public MobileBackendEndpointResolver(
        IMobileBackendConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        IMobileNetworkChangeSource networkChangeSource)
        : this(
            configuration,
            httpClientFactory,
            networkChangeSource,
            InternalProbeTimeout,
            ExternalProbeTimeout,
            SingleEndpointProbeTimeout,
            CacheDuration)
    {
    }

    internal MobileBackendEndpointResolver(
        IMobileBackendConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        IMobileNetworkChangeSource networkChangeSource,
        TimeSpan probeTimeout,
        TimeSpan cacheDuration)
        : this(
            configuration,
            httpClientFactory,
            networkChangeSource,
            probeTimeout,
            probeTimeout,
            probeTimeout,
            cacheDuration)
    {
    }

    internal MobileBackendEndpointResolver(
        IMobileBackendConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        IMobileNetworkChangeSource networkChangeSource,
        TimeSpan internalProbeTimeout,
        TimeSpan externalProbeTimeout,
        TimeSpan singleEndpointProbeTimeout,
        TimeSpan cacheDuration)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _networkChangeSource = networkChangeSource;
        _internalProbeTimeout = internalProbeTimeout;
        _externalProbeTimeout = externalProbeTimeout;
        _singleEndpointProbeTimeout = singleEndpointProbeTimeout;
        _cacheDuration = cacheDuration;
        _networkChangeSource.NetworkChanged += OnNetworkChanged;
    }

    public event EventHandler<MobileBackendConnectionChangedEventArgs>? ConnectionChanged;

    public MobileBackendConnectionState ConnectionState
    {
        get
        {
            lock (_stateLock)
            {
                return _connectionState;
            }
        }
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
            || _configuration.ExternalBaseUri is not { } externalBaseUri)
        {
            PublishState(new MobileBackendConnectionState(
                MobileBackendConnectionPhase.Failed,
                FailureKind: MobileBackendFailureKind.Configuration));
            throw new MobileBackendConfigurationException();
        }

        lock (_stateLock)
        {
            if (_cachedFailure is not null)
            {
                throw _cachedFailure;
            }
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

            lock (_stateLock)
            {
                if (_cachedFailure is not null)
                {
                    throw _cachedFailure;
                }
            }

            for (var restart = 0; restart <= MaximumGenerationRestarts; restart++)
            {
                var generation = Volatile.Read(ref _generation);
                PublishState(new MobileBackendConnectionState(
                    MobileBackendConnectionPhase.Checking));

                var internalBaseUri = _configuration.InternalBaseUri;
                if (internalBaseUri is null)
                {
                    var singleResult = await ProbeAsync(
                            externalBaseUri,
                            _singleEndpointProbeTimeout,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (generation != Volatile.Read(ref _generation))
                    {
                        continue;
                    }

                    return CompleteSelection(
                        externalBaseUri,
                        MobileBackendEndpointKind.Single,
                        singleResult,
                        generation);
                }

                var internalResult = await ProbeAsync(
                        internalBaseUri,
                        _internalProbeTimeout,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (generation != Volatile.Read(ref _generation))
                {
                    continue;
                }

                if (internalResult.Success)
                {
                    return CompleteSelection(
                        internalBaseUri,
                        MobileBackendEndpointKind.Internal,
                        internalResult,
                        generation);
                }

                if (!internalResult.CanTryAlternateEndpoint)
                {
                    return CompleteSelection(
                        internalBaseUri,
                        MobileBackendEndpointKind.Internal,
                        internalResult,
                        generation);
                }

                var externalResult = await ProbeAsync(
                        externalBaseUri,
                        _externalProbeTimeout,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (generation != Volatile.Read(ref _generation))
                {
                    continue;
                }

                return CompleteSelection(
                    externalBaseUri,
                    MobileBackendEndpointKind.External,
                    externalResult,
                    generation);
            }

            var unstableFailure = new MobileBackendConnectionException(
                MobileBackendFailureKind.Network);
            PublishState(new MobileBackendConnectionState(
                MobileBackendConnectionPhase.Failed,
                FailureKind: MobileBackendFailureKind.Network));
            throw unstableFailure;
        }
        finally
        {
            _selectionGate.Release();
        }
    }

    public async Task<Uri> ReconnectAsync(CancellationToken cancellationToken = default)
    {
        Invalidate();
        return await ResolveAsync(cancellationToken).ConfigureAwait(false);
    }

    public void SetForeground(bool isForeground)
    {
        lock (_stateLock)
        {
            _isForeground = isForeground;
            if (!isForeground)
            {
                _automaticReconnectCancellation?.Cancel();
                _automaticReconnectCancellation = null;
                return;
            }
        }

        Invalidate();
        ScheduleAutomaticReconnect(NetworkChangeDebounce);
    }

    public void ReportFailure(MobileBackendFailureKind failureKind)
    {
        if (failureKind == MobileBackendFailureKind.None)
        {
            return;
        }

        PublishState(new MobileBackendConnectionState(
            MobileBackendConnectionPhase.Failed,
            FailureKind: failureKind));
    }

    public void Invalidate()
    {
        lock (_stateLock)
        {
            _generation++;
            _cachedBaseUri = null;
            _cachedFailure = null;
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
        lock (_stateLock)
        {
            _automaticReconnectCancellation?.Cancel();
            _automaticReconnectCancellation = null;
        }
        _networkChangeSource.NetworkChanged -= OnNetworkChanged;
        _selectionGate.Dispose();
    }

    private Uri CompleteSelection(
        Uri baseUri,
        MobileBackendEndpointKind endpointKind,
        EndpointProbeResult result,
        long generation)
    {
        if (!result.Success)
        {
            var failure = new MobileBackendConnectionException(
                result.FailureKind,
                result.Exception);
            lock (_stateLock)
            {
                if (generation == _generation)
                {
                    _cachedFailure = failure;
                }
            }

            PublishState(new MobileBackendConnectionState(
                MobileBackendConnectionPhase.Failed,
                FailureKind: result.FailureKind));
            throw failure;
        }

        lock (_stateLock)
        {
            if (generation == _generation)
            {
                _cachedBaseUri = baseUri;
                _cacheExpiresAt = DateTimeOffset.UtcNow + _cacheDuration;
            }
        }

        PublishState(new MobileBackendConnectionState(
            MobileBackendConnectionPhase.Connected,
            endpointKind,
            BaseUri: baseUri));
        return baseUri;
    }

    private async Task<EndpointProbeResult> ProbeAsync(
        Uri baseUri,
        TimeSpan timeoutDuration,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutDuration);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, "health"));
        try
        {
            var client = _httpClientFactory.CreateClient(MobileHttpClientNames.Probe);
            using var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return EndpointProbeResult.Succeeded;
            }

            return new EndpointProbeResult(
                Success: false,
                FailureKind: response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    ? MobileBackendFailureKind.Authentication
                    : (int)response.StatusCode >= 500
                        ? MobileBackendFailureKind.Server
                        : MobileBackendFailureKind.ApiContract,
                CanTryAlternateEndpoint: MobileBackendRetryPolicy.IsRetryableStatus(
                    response.StatusCode));
        }
        catch (OperationCanceledException) when (
            timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return new EndpointProbeResult(
                false,
                MobileBackendFailureKind.Timeout,
                CanTryAlternateEndpoint: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            return new EndpointProbeResult(
                false,
                MobileBackendRetryPolicy.ClassifyTransport(exception),
                CanTryAlternateEndpoint: true,
                Exception: exception);
        }
    }

    private void OnNetworkChanged(object? sender, EventArgs args)
    {
        Invalidate();
        ScheduleAutomaticReconnect(NetworkChangeDebounce);
    }

    private void ScheduleAutomaticReconnect(TimeSpan delay)
    {
        CancellationTokenSource cancellation;
        lock (_stateLock)
        {
            if (!_isForeground || _disposed)
            {
                return;
            }

            _automaticReconnectCancellation?.Cancel();
            cancellation = new CancellationTokenSource();
            _automaticReconnectCancellation = cancellation;
        }

        _ = ReconnectAutomaticallyAsync(delay, cancellation);
    }

    private async Task ReconnectAutomaticallyAsync(
        TimeSpan delay,
        CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(delay, cancellation.Token).ConfigureAwait(false);
            await ResolveAsync(cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (MobileBackendConnectionException)
        {
            // The failed state is already published for the UI. A later network,
            // foreground, manual retry, or request event will trigger another check.
        }
        catch (MobileBackendConfigurationException)
        {
            // Configuration failures are terminal until a new build supplies settings.
        }
        catch
        {
            PublishState(new MobileBackendConnectionState(
                MobileBackendConnectionPhase.Failed,
                FailureKind: MobileBackendFailureKind.Network));
        }
        finally
        {
            lock (_stateLock)
            {
                if (ReferenceEquals(_automaticReconnectCancellation, cancellation))
                {
                    _automaticReconnectCancellation = null;
                }
            }

            cancellation.Dispose();
        }
    }

    private void PublishState(MobileBackendConnectionState state)
    {
        EventHandler<MobileBackendConnectionChangedEventArgs>? handler;
        lock (_stateLock)
        {
            if (_disposed || _connectionState == state)
            {
                return;
            }

            _connectionState = state;
            handler = ConnectionChanged;
        }

        try
        {
            handler?.Invoke(this, new MobileBackendConnectionChangedEventArgs(state));
        }
        catch
        {
            // Connection selection must not fail because an optional UI observer failed.
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MobileBackendEndpointResolver));
        }
    }

    private sealed record EndpointProbeResult(
        bool Success,
        MobileBackendFailureKind FailureKind = MobileBackendFailureKind.None,
        bool CanTryAlternateEndpoint = false,
        Exception? Exception = null)
    {
        public static EndpointProbeResult Succeeded { get; } = new(true);
    }
}
