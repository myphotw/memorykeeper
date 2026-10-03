using System.Net;
using MemoryKeeper.Mobile.Configuration;

namespace MemoryKeeper.Mobile.Http;

public sealed class MobileBackendEndpointFailureHandler : DelegatingHandler
{
    private readonly IMobileBackendConfiguration _configuration;
    private readonly IMobileBackendEndpointResolver _endpointResolver;

    public MobileBackendEndpointFailureHandler(
        IMobileBackendConfiguration configuration,
        IMobileBackendEndpointResolver endpointResolver)
    {
        _configuration = configuration;
        _endpointResolver = endpointResolver;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var isBackendRequest =
            MobileBackendRequestContext.TryGetSelectedBackend(request, out var selectedBaseUri)
            && MobileBackendAuthenticationHandler.IsConfiguredBackendOrigin(
                selectedBaseUri,
                _configuration)
            && MobileBackendAuthenticationHandler.IsSameOrigin(
                request.RequestUri,
                selectedBaseUri);
        try
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (isBackendRequest && IsEndpointFailure(response.StatusCode))
            {
                _endpointResolver.Invalidate();
            }

            return response;
        }
        catch (OperationCanceledException) when (
            isBackendRequest && !cancellationToken.IsCancellationRequested)
        {
            _endpointResolver.Invalidate();
            throw;
        }
        catch (HttpRequestException) when (isBackendRequest)
        {
            _endpointResolver.Invalidate();
            throw;
        }
    }

    private static bool IsEndpointFailure(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;
}
