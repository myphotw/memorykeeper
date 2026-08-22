using System.Net;
using System.Net.Http.Headers;
using System.Text;
using MemoryKeeper.Infrastructure.Services.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class BackendConnectionTests
{
    [Fact]
    public void Options_Default_To_Production_Nas_And_Allow_Test_Override()
    {
        Assert.Equal(
            "https://onepieces.synology.me:8443",
            new TcBackendOptions().ApiBaseUrl);

        using var provider = BuildProvider(new DelegateHandler(_ => Json("{}")), "http://127.0.0.1:8123");
        var options = provider.GetRequiredService<IOptions<TcBackendOptions>>().Value;
        Assert.Equal("http://127.0.0.1:8123", options.ApiBaseUrl);
    }

    [Fact]
    public async Task Health_Is_Public_And_Readiness_Capabilities_Are_Authenticated()
    {
        var seen = new List<(string Path, AuthenticationHeaderValue? Authorization)>();
        var handler = new DelegateHandler(request =>
        {
            seen.Add((request.RequestUri!.AbsolutePath, request.Headers.Authorization));
            return request.RequestUri.AbsolutePath switch
            {
                "/health" => Json("""{"status":"ok","version":"1.0.0"}"""),
                "/api/common/readiness" => Json("""{"services":{"google_geocoding":{"configured":true,"source":"environment"},"google_places":{"configured":true,"source":"environment"},"weather":{"configured":false,"source":null},"astrometry":{"configured":false,"source":null}},"vision":{"credential_available":true,"worker_running":true,"worker_status":"RUNNING"}}"""),
                "/api/common/capabilities" => Json("""{"api_version":"1.1","service_version":"1.0.0","capabilities":{"gallery":true,"upload":true,"vision":true},"supported_services":["MemoryKeeper"]}"""),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        });

        using var provider = BuildProvider(handler);
        var service = provider.GetRequiredService<BackendConnectionService>();
        var snapshot = await service.CheckAsync();

        Assert.True(snapshot.IsConnected);
        Assert.Equal("1.0.0", snapshot.Health!.Version);
        Assert.True(snapshot.Readiness!.Services["google_geocoding"].Configured);
        Assert.True(snapshot.Capabilities!.Capabilities["gallery"]);
        Assert.Null(seen.Single(item => item.Path == "/health").Authorization);
        Assert.All(
            seen.Where(item => item.Path.StartsWith("/api/", StringComparison.Ordinal)),
            item =>
            {
                Assert.Equal("Bearer", item.Authorization?.Scheme);
                Assert.Equal("unit-test-token", item.Authorization?.Parameter);
            });
    }

    [Fact]
    public async Task Readiness_401_Is_Classified_Without_Leaking_Token()
    {
        var handler = new DelegateHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/common/readiness" => Json("unauthorized", HttpStatusCode.Unauthorized),
            _ => Json("""{"status":"ok","version":"1.0.0"}"""),
        });
        using var provider = BuildProvider(handler);
        var service = provider.GetRequiredService<BackendConnectionService>();

        var error = await Assert.ThrowsAsync<ApiException>(() => service.GetReadinessAsync());

        Assert.Equal(ApiErrorCategory.Unauthorized, error.Category);
        Assert.DoesNotContain("unit-test-token", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Malformed_Health_Is_Classified()
    {
        using var provider = BuildProvider(new DelegateHandler(_ => Json("{}")));
        var service = provider.GetRequiredService<BackendConnectionService>();

        var error = await Assert.ThrowsAsync<ApiException>(() => service.GetHealthAsync());

        Assert.Equal(ApiErrorCategory.MalformedResponse, error.Category);
    }

    [Fact]
    public async Task Timeout_And_Offline_Are_Classified()
    {
        using var timeoutProvider = BuildProvider(new DelegateHandler(_ => throw new TaskCanceledException("timeout")));
        var timeoutClient = timeoutProvider.GetRequiredService<BaseApiClient>();
        var timeout = await Assert.ThrowsAsync<ApiException>(() => timeoutClient.GetAsync<object>("/health"));
        Assert.Equal(ApiErrorCategory.Timeout, timeout.Category);

        using var offlineProvider = BuildProvider(new DelegateHandler(_ => throw new HttpRequestException("offline")));
        var offlineClient = offlineProvider.GetRequiredService<BaseApiClient>();
        var offline = await Assert.ThrowsAsync<ApiException>(() => offlineClient.GetAsync<object>("/health"));
        Assert.Equal(ApiErrorCategory.Network, offline.Category);
    }

    [Fact]
    public void Forbidden_Tls_And_Dns_Are_Classified()
    {
        Assert.Equal(
            ApiErrorCategory.Forbidden,
            ApiErrorClassifier.FromStatusCode(HttpStatusCode.Forbidden));

        var tls = ApiErrorClassifier.FromTransport(
            new HttpRequestException(
                HttpRequestError.SecureConnectionError,
                "tls",
                inner: null),
            HttpMethod.Get,
            "/health");
        Assert.Equal(ApiErrorCategory.Tls, tls.Category);

        var dns = ApiErrorClassifier.FromTransport(
            new HttpRequestException(
                HttpRequestError.NameResolutionError,
                "dns",
                inner: null),
            HttpMethod.Get,
            "/health");
        Assert.Equal(ApiErrorCategory.Dns, dns.Category);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "NAS 연결 인증 정보를 확인하세요.")]
    [InlineData(HttpStatusCode.NotFound, "사진을 찾을 수 없습니다.")]
    [InlineData(HttpStatusCode.Conflict, "다른 곳에서 정보가 변경되었습니다. 최신 정보를 다시 불러온 뒤 다시 시도하세요.")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "입력한 정보를 확인하세요.")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "NAS 서비스에 연결할 수 없습니다. 잠시 후 다시 시도하세요.")]
    public void ApiFailures_MapToUiSafeMessages(HttpStatusCode statusCode, string expected)
    {
        var exception = new ApiException(
            statusCode,
            "internal route/revision message",
            "{\"detail\":{\"code\":\"REVISION_CONFLICT\",\"current_revision\":9}}");

        var actual = ApiErrorClassifier.ToUserMessage(exception, "사진을 찾을 수 없습니다.");

        Assert.Equal(expected, actual);
        Assert.DoesNotContain("revision", actual, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("REVISION_CONFLICT", exception.DetailCode);
    }

    private static ServiceProvider BuildProvider(
        HttpMessageHandler handler,
        string baseUrl = "https://backend.test:8443")
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddTcBackendApiClient(options =>
        {
            options.ApiBaseUrl = baseUrl;
            options.AuthToken = "unit-test-token";
            options.RetryCount = 0;
            options.Timeout = 2;
        });
        services.PostConfigure<TcBackendOptions>(options =>
        {
            options.ApiBaseUrl = baseUrl;
            options.AuthToken = "unit-test-token";
        });
        services.AddHttpClient(BaseApiClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider();
    }

    private static HttpResponseMessage Json(
        string body,
        HttpStatusCode statusCode = HttpStatusCode.OK) => new(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    private sealed class DelegateHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _send;

        public DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> send) => _send = send;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(_send(request));
    }
}
