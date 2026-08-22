using System.Net;
using System.Text;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Infrastructure;
using MemoryKeeper.Infrastructure.Repositories.Api;
using MemoryKeeper.Infrastructure.Services.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class BackendChangeFeedRepositoryContractTests
{
    [Fact]
    public async Task GetChanges_UsesCommonRouteAndServiceQuery_AndMapsCompleteEventSchema()
    {
        const string key = "GET /api/common/changes?cursor=17&limit=500&service_name=MemoryKeeper";
        var handler = new RecordingHandler(key, """
            {
              "items": [
                {
                  "cursor": 18,
                  "service_name": "MemoryKeeper",
                  "resource_type": "MemoryKeeperFileMetadata",
                  "resource_id": "file-1",
                  "operation": "UPDATE",
                  "revision": 4,
                  "tombstone": false,
                  "changed_at": "2026-08-22T01:02:03Z"
                }
              ],
              "next_cursor": 18,
              "has_more": true
            }
            """);
        using var provider = BuildProvider(handler);
        var repository = provider.GetRequiredService<IBackendChangeFeed>();

        var response = await repository.GetChangesAsync(17, 500);

        var change = Assert.Single(response.Items);
        Assert.Equal(18, response.NextCursor);
        Assert.True(response.HasMore);
        Assert.Equal("MemoryKeeper", change.ServiceName);
        Assert.Equal("MemoryKeeperFileMetadata", change.ResourceType);
        Assert.Equal("file-1", change.ResourceId);
        Assert.Equal("UPDATE", change.Operation);
        Assert.Equal(4, change.Revision);
        Assert.False(change.Tombstone);
        Assert.Equal(DateTimeOffset.Parse("2026-08-22T01:02:03Z"), change.ChangedAt);
        Assert.Equal(key, handler.RequestKey);
        Assert.Equal("Bearer change-test-token", handler.AuthorizationHeader);
    }

    private static ServiceProvider BuildProvider(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddTcBackendApiClient(options =>
        {
            options.ApiBaseUrl = "http://localhost:8000";
            options.AuthToken = "change-test-token";
            options.ServiceName = "MemoryKeeper";
            options.RetryCount = 0;
        });
        services.PostConfigure<TcBackendOptions>(options =>
        {
            options.ApiBaseUrl = "http://localhost:8000";
            options.AuthToken = "change-test-token";
            options.ServiceName = "MemoryKeeper";
        });
        services.AddHttpClient(BaseApiClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddSingleton<IBackendChangeFeed, BackendChangeFeedRepository>();
        return services.BuildServiceProvider();
    }

    private sealed class RecordingHandler(string expectedKey, string body) : HttpMessageHandler
    {
        public string RequestKey { get; private set; } = string.Empty;
        public string AuthorizationHeader { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestKey = $"{request.Method.Method} {request.RequestUri!.PathAndQuery}";
            AuthorizationHeader = request.Headers.Authorization?.ToString() ?? string.Empty;
            var status = string.Equals(RequestKey, expectedKey, StringComparison.Ordinal)
                ? HttpStatusCode.OK
                : HttpStatusCode.NotFound;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
