using System.Net;
using System.Net.Http.Headers;
using MemoryKeeper.Infrastructure.Services.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class BackendMediaDownloaderTests
{
    [Fact]
    public async Task Relative_And_SameOrigin_Media_Use_Bearer_But_External_Does_Not()
    {
        var requests = new List<(Uri Uri, AuthenticationHeaderValue? Authorization)>();
        using var provider = BuildProvider(new CaptureHandler(request =>
        {
            requests.Add((request.RequestUri!, request.Headers.Authorization));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3]),
            };
        }));
        var downloader = provider.GetRequiredService<BackendMediaDownloader>();

        Assert.Equal([1, 2, 3], await downloader.GetBytesAsync("/api/common/gallery/a/thumbnail"));
        Assert.Equal([1, 2, 3], await downloader.GetBytesAsync("https://backend.test:8443/api/common/gallery/a/preview"));
        Assert.Equal([1, 2, 3], await downloader.GetBytesAsync("https://cdn.example.test/image.jpg"));

        Assert.Equal("Bearer", requests[0].Authorization?.Scheme);
        Assert.Equal("media-test-token", requests[0].Authorization?.Parameter);
        Assert.Equal("Bearer", requests[1].Authorization?.Scheme);
        Assert.Equal("media-test-token", requests[1].Authorization?.Parameter);
        Assert.Null(requests[2].Authorization);
        Assert.Equal("backend.test", requests[0].Uri.Host);
    }

    [Fact]
    public async Task Missing_Token_Blocks_Protected_Media_Before_Network()
    {
        var calls = 0;
        using var provider = BuildProvider(
            new CaptureHandler(_ =>
            {
                calls++;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }),
            token: string.Empty);
        var downloader = provider.GetRequiredService<BackendMediaDownloader>();

        var error = await Assert.ThrowsAsync<ApiException>(
            () => downloader.GetBytesAsync("/api/common/gallery/a/thumbnail"));

        Assert.Equal(ApiErrorCategory.Unauthorized, error.Category);
        Assert.Equal(0, calls);
    }

    private static ServiceProvider BuildProvider(HttpMessageHandler handler, string token = "media-test-token")
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddTcBackendApiClient(options =>
        {
            options.ApiBaseUrl = "https://backend.test:8443";
            options.AuthToken = token;
            options.RetryCount = 0;
        });
        services.PostConfigure<TcBackendOptions>(options =>
        {
            options.ApiBaseUrl = "https://backend.test:8443";
            options.AuthToken = token;
        });
        services.AddHttpClient(BaseApiClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider();
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _send;

        public CaptureHandler(Func<HttpRequestMessage, HttpResponseMessage> send) => _send = send;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(_send(request));
    }
}
