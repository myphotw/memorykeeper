using System.Net.Http.Headers;
using MemoryKeeper.Mobile.Configuration;

namespace MemoryKeeper.Mobile.Http;

public sealed class MobileBackendAuthenticationHandler : DelegatingHandler
{
    private readonly IMobileBackendConfiguration _configuration;

    public MobileBackendAuthenticationHandler(IMobileBackendConfiguration configuration)
    {
        _configuration = configuration;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (RequiresBearer(request.RequestUri, _configuration.BaseUri))
        {
            var token = _configuration.BearerToken;
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new MobileBackendConfigurationException();
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
