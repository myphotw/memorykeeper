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
        if (MobileBackendRequestContext.TryGetSelectedBackend(request, out var selectedBaseUri)
            && IsConfiguredBackendOrigin(selectedBaseUri, _configuration)
            && RequiresBearer(request.RequestUri, selectedBaseUri))
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

    internal static bool IsConfiguredBackendOrigin(
        Uri? requestUri,
        IMobileBackendConfiguration configuration) =>
        requestUri is not null
        && (SameOrigin(requestUri, configuration.InternalBaseUri)
            || SameOrigin(requestUri, configuration.ExternalBaseUri));

    internal static bool IsSameOrigin(Uri? requestUri, Uri? backendBaseUri) =>
        requestUri is not null && SameOrigin(requestUri, backendBaseUri);

    private static bool SameOrigin(Uri requestUri, Uri? backendBaseUri) =>
        backendBaseUri is not null
        && requestUri.IsAbsoluteUri
        && string.Equals(requestUri.Scheme, backendBaseUri.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(requestUri.Host, backendBaseUri.Host, StringComparison.OrdinalIgnoreCase)
        && requestUri.Port == backendBaseUri.Port;
}
