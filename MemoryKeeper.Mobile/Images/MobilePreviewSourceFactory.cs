using MemoryKeeper.Application;
using MemoryKeeper.Mobile.Configuration;
using MemoryKeeper.Mobile.Http;

namespace MemoryKeeper.Mobile.Images;

public sealed class MobilePreviewSourceFactory : IMobilePreviewSourceFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMobileBackendConfiguration _configuration;
    private readonly IMobileBackendEndpointResolver _endpointResolver;

    public MobilePreviewSourceFactory(
        IHttpClientFactory httpClientFactory,
        IMobileBackendConfiguration configuration,
        IMobileBackendEndpointResolver endpointResolver)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _endpointResolver = endpointResolver;
    }

    public async Task<MobilePreviewLoadResult> LoadAsync(
        string fileId,
        string? previewUrl,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            Uri baseUri;
            try
            {
                baseUri = await _endpointResolver.ResolveAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException exception) when (
                attempt == 0 && exception is not MobileBackendConnectionException)
            {
                continue;
            }
            catch
            {
                MobilePreviewDiagnostics.WriteFailure(fileId, "missing", "configuration");
                return MobilePreviewLoadResult.Failure("missing", "configuration");
            }

            var baseUrl = baseUri.ToString();
            if (!MobileBackendMediaUrlPolicy.CanUseConfiguredApiUrl(
                    previewUrl,
                    _configuration))
            {
                MobilePreviewDiagnostics.WriteFailure(fileId, "foreign-api", "url");
                return MobilePreviewLoadResult.Failure("foreign-api", "url");
            }

            var absoluteUrl = BackendMediaUrlResolver.ResolvePreviewUrl(baseUrl, fileId, previewUrl);
            var requestDescription = BackendMediaUrlResolver.DescribeForDiagnostics(baseUrl, absoluteUrl);
            if (!Uri.TryCreate(absoluteUrl, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                MobilePreviewDiagnostics.WriteFailure(fileId, requestDescription, "url");
                return MobilePreviewLoadResult.Failure(requestDescription, "url");
            }

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
                    if (attempt == 0
                        && MobileBackendRetryPolicy.IsRetryableStatus(response.StatusCode))
                    {
                        continue;
                    }

                    MobilePreviewDiagnostics.WriteFailure(
                        fileId,
                        requestDescription,
                        "http",
                        response.StatusCode);
                    return MobilePreviewLoadResult.Failure(requestDescription, "http");
                }

                var mediaType = response.Content.Headers.ContentType?.MediaType;
                if (!string.IsNullOrWhiteSpace(mediaType)
                    && !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(mediaType, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
                {
                    MobilePreviewDiagnostics.WriteFailure(fileId, requestDescription, "content-type");
                    return MobilePreviewLoadResult.Failure(requestDescription, "content-type");
                }

                var previewBytes = await response.Content
                    .ReadAsByteArrayAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (previewBytes.Length == 0)
                {
                    MobilePreviewDiagnostics.WriteFailure(fileId, requestDescription, "empty-body");
                    return MobilePreviewLoadResult.Failure(requestDescription, "empty-body");
                }

                return MobilePreviewLoadResult.Success(previewBytes, requestDescription);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException) when (attempt == 0)
            {
            }
            catch (Exception exception)
            {
                MobilePreviewDiagnostics.WriteFailure(
                    fileId,
                    requestDescription,
                    "request",
                    exception: exception);
                return MobilePreviewLoadResult.Failure(requestDescription, "request");
            }
        }

        return MobilePreviewLoadResult.Failure("missing", "request");
    }
}
