using MemoryKeeper.Application;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Mobile.Configuration;
using MemoryKeeper.Mobile.Http;
using Microsoft.Maui.Controls;

namespace MemoryKeeper.Mobile.Images;

public sealed class MobileThumbnailSourceFactory : IMobileThumbnailSourceFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMobileBackendConfiguration _configuration;
    private readonly IMobileBackendEndpointResolver _endpointResolver;
    private readonly BoundedThumbnailMemoryCache _cache;

    public MobileThumbnailSourceFactory(
        IHttpClientFactory httpClientFactory,
        IMobileBackendConfiguration configuration,
        IMobileBackendEndpointResolver endpointResolver,
        BoundedThumbnailMemoryCache cache)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _endpointResolver = endpointResolver;
        _cache = cache;
    }

    public ImageSource? Create(string fileId, string? thumbnailUrl)
    {
        if (string.IsNullOrWhiteSpace(thumbnailUrl))
        {
            return null;
        }

        var cacheKey = CreateCacheKey(fileId, thumbnailUrl);
        return new MobileThumbnailImageSource
        {
            CacheKey = cacheKey,
            Stream = cancellationToken => OpenThumbnailAsync(
                cacheKey,
                thumbnailUrl,
                cancellationToken),
        };
    }

    public async Task PreloadAsync(
        string fileId,
        string? thumbnailUrl,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(thumbnailUrl))
        {
            return;
        }

        _ = await LoadThumbnailBytesAsync(
                CreateCacheKey(fileId, thumbnailUrl),
                thumbnailUrl,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Stream> OpenThumbnailAsync(
        string cacheKey,
        string thumbnailUrl,
        CancellationToken cancellationToken)
    {
        var bytes = await LoadThumbnailBytesAsync(
                cacheKey,
                thumbnailUrl,
                cancellationToken)
            .ConfigureAwait(false);
        return bytes is { Length: > 0 }
            ? new MemoryStream(bytes, writable: false)
            : Stream.Null;
    }

    private async Task<byte[]?> LoadThumbnailBytesAsync(
        string cacheKey,
        string thumbnailUrl,
        CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await _cache
                .GetOrLoadAsync(
                    cacheKey,
                    token => FetchThumbnailAsync(thumbnailUrl, token),
                    cancellationToken)
                .ConfigureAwait(false);
            return bytes is { Length: > 0 } ? bytes : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static string CreateCacheKey(string fileId, string thumbnailUrl) =>
        string.IsNullOrWhiteSpace(fileId)
            ? $"url:{thumbnailUrl.Trim()}"
            : $"file:{fileId.Trim()}";

    private async Task<byte[]?> FetchThumbnailAsync(
        string thumbnailUrl,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var baseUri = await _endpointResolver
                    .ResolveAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (!MobileBackendMediaUrlPolicy.CanUseConfiguredApiUrl(
                        thumbnailUrl,
                        _configuration))
                {
                    return null;
                }

                var absoluteUrl = BackendMediaUrlResolver.ToAbsoluteUrl(
                    baseUri.ToString(),
                    thumbnailUrl);
                if (!Uri.TryCreate(absoluteUrl, UriKind.Absolute, out var uri)
                    || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
                {
                    return null;
                }

                var client = _httpClientFactory.CreateClient(MobileHttpClientNames.Backend);
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                MobileBackendRequestContext.MarkSelectedBackend(request, baseUri);
                using var response = await client
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    if (attempt == 0
                        && MobileBackendRetryPolicy.IsRetryableStatus(response.StatusCode))
                    {
                        continue;
                    }

                    return null;
                }

                var bytes = await response.Content
                    .ReadAsByteArrayAsync(cancellationToken)
                    .ConfigureAwait(false);
                return bytes.Length > 0 ? bytes : null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException exception) when (
                attempt == 0 && exception is not MobileBackendConnectionException)
            {
            }
            catch
            {
                return null;
            }
        }

        return null;
    }
}
