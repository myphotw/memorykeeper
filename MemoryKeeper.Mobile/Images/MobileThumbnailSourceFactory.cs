using MemoryKeeper.Application;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Mobile.Configuration;
using MemoryKeeper.Mobile.Http;
using Microsoft.Maui.Controls;

namespace MemoryKeeper.Mobile.Images;

public sealed class MobileThumbnailSourceFactory : IMobileThumbnailSourceFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMobileBackendEndpointResolver _endpointResolver;
    private readonly BoundedThumbnailMemoryCache _cache;

    public MobileThumbnailSourceFactory(
        IHttpClientFactory httpClientFactory,
        IMobileBackendEndpointResolver endpointResolver,
        BoundedThumbnailMemoryCache cache)
    {
        _httpClientFactory = httpClientFactory;
        _endpointResolver = endpointResolver;
        _cache = cache;
    }

    public ImageSource? Create(string fileId, string? thumbnailUrl)
    {
        if (string.IsNullOrWhiteSpace(thumbnailUrl))
        {
            return null;
        }

        var cacheKey = string.IsNullOrWhiteSpace(fileId)
            ? $"url:{thumbnailUrl.Trim()}"
            : $"file:{fileId.Trim()}";
        return new StreamImageSource
        {
            Stream = cancellationToken => OpenThumbnailAsync(
                cacheKey,
                thumbnailUrl,
                cancellationToken),
        };
    }

    private async Task<Stream> OpenThumbnailAsync(
        string cacheKey,
        string thumbnailUrl,
        CancellationToken cancellationToken)
    {
        try
        {
            var baseUri = await _endpointResolver
                .ResolveAsync(cancellationToken)
                .ConfigureAwait(false);
            var absoluteUrl = BackendMediaUrlResolver.ToAbsoluteUrl(
                baseUri.ToString(),
                thumbnailUrl);
            if (!Uri.TryCreate(absoluteUrl, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                return Stream.Null;
            }

            var bytes = await _cache
                .GetOrLoadAsync(
                    cacheKey,
                    token => FetchThumbnailAsync(baseUri, uri, token),
                    cancellationToken)
                .ConfigureAwait(false);
            return bytes is { Length: > 0 }
                ? new MemoryStream(bytes, writable: false)
                : Stream.Null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Stream.Null;
        }
    }

    private async Task<byte[]?> FetchThumbnailAsync(
        Uri baseUri,
        Uri uri,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(MobileHttpClientNames.Backend);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            MobileBackendRequestContext.MarkSelectedBackend(request, baseUri);
            using var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var bytes = await response.Content
                .ReadAsByteArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            return bytes.Length > 0 ? bytes : null;
        }
        catch
        {
            return null;
        }
    }
}
