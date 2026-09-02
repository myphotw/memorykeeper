using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemoryKeeper.Infrastructure.Services.Api;

public sealed class BackendMediaDownloader
{
    private const int MaxCacheEntries = 64;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<TcBackendOptions> _options;
    private readonly ILogger<BackendMediaDownloader> _logger;
    private readonly ConcurrentDictionary<string, byte[]> _cache = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _cacheOrder = new();

    public BackendMediaDownloader(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<TcBackendOptions> options,
        ILogger<BackendMediaDownloader> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<byte[]> GetBytesAsync(
        string pathOrUrl,
        CancellationToken cancellationToken = default,
        string? context = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pathOrUrl);
        var uri = TcBackendRequestPolicy.ResolveUri(pathOrUrl, _options.CurrentValue.ApiBaseUrl);
        var cacheKey = uri.AbsoluteUri;
        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            var client = _httpClientFactory.CreateClient(BaseApiClient.HttpClientName);
            using var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "Media request completed. Context={Context}, Path={Path}, StatusCode={StatusCode}, BearerExpected={BearerExpected}",
                    context,
                    uri.AbsolutePath,
                    (int)response.StatusCode,
                    TcBackendRequestPolicy.RequiresBearer(uri, _options.CurrentValue.ApiBaseUrl));
                throw new ApiException(
                    response.StatusCode,
                    $"TC-Backend media request failed: {(int)response.StatusCode} ({uri.AbsolutePath})",
                    category: ApiErrorClassifier.FromStatusCode(response.StatusCode));
            }

            var bytes = await response.Content
                .ReadAsByteArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            _logger.LogDebug(
                "Media request completed. Context={Context}, Path={Path}, StatusCode={StatusCode}, BearerExpected={BearerExpected}",
                context,
                uri.AbsolutePath,
                (int)response.StatusCode,
                TcBackendRequestPolicy.RequiresBearer(uri, _options.CurrentValue.ApiBaseUrl));
            AddToCache(cacheKey, bytes);
            return bytes;
        }
        catch (ApiException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "TC-Backend media request failed. Context={Context}, Path={Path}, BearerExpected={BearerExpected}",
                context,
                uri.AbsolutePath,
                TcBackendRequestPolicy.RequiresBearer(uri, _options.CurrentValue.ApiBaseUrl));
            throw ApiErrorClassifier.FromTransport(ex, HttpMethod.Get, uri.AbsolutePath);
        }
    }

    private void AddToCache(string key, byte[] bytes)
    {
        if (!_cache.TryAdd(key, bytes))
        {
            return;
        }

        _cacheOrder.Enqueue(key);
        while (_cache.Count > MaxCacheEntries && _cacheOrder.TryDequeue(out var oldest))
        {
            _cache.TryRemove(oldest, out _);
        }
    }
}
