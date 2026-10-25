using System.Net;
using System.Security.Authentication;
using MemoryKeeper.Mobile.Configuration;

namespace MemoryKeeper.Mobile.Http;

internal static class MobileBackendRetryPolicy
{
    public static bool IsRetryableStatus(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    public static MobileBackendFailureKind ClassifyTransport(HttpRequestException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is AuthenticationException)
            {
                return MobileBackendFailureKind.Tls;
            }
        }

        return MobileBackendFailureKind.Network;
    }
}
