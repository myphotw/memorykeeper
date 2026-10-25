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
            if (isBackendRequest)
            {
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    ReportFailure(MobileBackendFailureKind.Authentication);
                }
                else if ((int)response.StatusCode >= 500)
                {
                    ReportFailure(MobileBackendFailureKind.Server);
                }

                if (MobileBackendRetryPolicy.IsRetryableStatus(response.StatusCode))
                {
                    _endpointResolver.Invalidate();
                }
            }

            return response;
        }
        catch (OperationCanceledException) when (
            isBackendRequest && !cancellationToken.IsCancellationRequested)
        {
            ReportFailure(MobileBackendFailureKind.Timeout);
            _endpointResolver.Invalidate();
            throw;
        }
        catch (HttpRequestException exception) when (isBackendRequest)
        {
            ReportFailure(MobileBackendRetryPolicy.ClassifyTransport(exception));
            _endpointResolver.Invalidate();
            throw;
        }
    }

    private void ReportFailure(MobileBackendFailureKind failureKind)
    {
        if (_endpointResolver is IMobileBackendConnectionMonitor monitor)
        {
            monitor.ReportFailure(failureKind);
        }
    }
}
