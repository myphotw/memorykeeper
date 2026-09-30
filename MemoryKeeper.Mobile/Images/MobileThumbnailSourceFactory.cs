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
    private readonly BoundedThumbnailMemoryCache _cache;

    public MobileThumbnailSourceFactory(
        IHttpClientFactory httpClientFactory,
        IMobileBackendConfiguration configuration,
        BoundedThumbnailMemoryCache cache)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _cache = cache;
    }

    public ImageSource? Create(string fileId, string? thumbnailUrl)
    {
        if (string.IsNullOrWhiteSpace(thumbnailUrl) || _configuration.BaseUri is null)
        {
            return null;
        }

        var absoluteUrl = BackendMediaUrlResolver.ToAbsoluteUrl(
            _configuration.BaseUri.ToString(),
            thumbnailUrl);
        if (!Uri.TryCreate(absoluteUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return null;
        }

        var cacheKey = string.IsNullOrWhiteSpace(fileId)
            ? $"url:{uri.AbsoluteUri}"
            : $"file:{fileId.Trim()}";
        return new StreamImageSource
        {
            Stream = cancellationToken => OpenThumbnailAsync(cacheKey, uri, cancellationToken),
        };
    }

    private async Task<Stream> OpenThumbnailAsync(
        string cacheKey,
        Uri uri,
        CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await _cache
                .GetOrLoadAsync(cacheKey, token => FetchThumbnailAsync(uri, token), cancellationToken)
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

    private async Task<byte[]?> FetchThumbnailAsync(Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(MobileHttpClientNames.Backend);
            using var response = await client
                .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
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
