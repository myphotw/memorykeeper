using System.Net.Http.Headers;
using MemoryKeeper.Mobile.Configuration;
using MemoryKeeper.Mobile.Security;

namespace MemoryKeeper.Mobile.Http;

public sealed class MobileBackendAuthenticationHandler : DelegatingHandler
{
    private readonly IMobileBackendConfiguration _configuration;
    private readonly ISecureTokenStore _tokenStore;

    public MobileBackendAuthenticationHandler(
        IMobileBackendConfiguration configuration,
        ISecureTokenStore tokenStore)
    {
        _configuration = configuration;
        _tokenStore = tokenStore;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (RequiresBearer(request.RequestUri, _configuration.GetBaseUri()))
        {
            var token = await _tokenStore.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new InvalidOperationException("Backend 인증 정보가 준비되지 않았습니다.");
            }

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    internal static bool RequiresBearer(Uri? requestUri, Uri? backendBaseUri)
    {
        if (requestUri is null
            || backendBaseUri is null
            || !requestUri.IsAbsoluteUri
            || !SameOrigin(requestUri, backendBaseUri))
        {
            return false;
        }

        var path = requestUri.AbsolutePath;
        return path.Equals("/api", StringComparison.OrdinalIgnoreCase)
               || path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SameOrigin(Uri requestUri, Uri backendBaseUri) =>
        string.Equals(requestUri.Scheme, backendBaseUri.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(requestUri.Host, backendBaseUri.Host, StringComparison.OrdinalIgnoreCase)
        && requestUri.Port == backendBaseUri.Port;
}
