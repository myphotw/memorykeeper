using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace MemoryKeeper.Infrastructure.Services.Api;

public sealed class TcBackendAuthenticationHandler : DelegatingHandler
{
    private readonly IOptionsMonitor<TcBackendOptions> _options;

    public TcBackendAuthenticationHandler(IOptionsMonitor<TcBackendOptions> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        if (request.RequestUri is not null
            && TcBackendRequestPolicy.RequiresBearer(request.RequestUri, options.ApiBaseUrl))
        {
            var token = options.AuthToken?.Trim();
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new ApiException(
                    HttpStatusCode.Unauthorized,
                    "TC-Backend authentication is not configured.",
                    category: ApiErrorCategory.Unauthorized);
            }

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
