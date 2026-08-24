using System.Net;
using System.Text;
using System.Text.Json;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Infrastructure.Repositories.Api;
using MemoryKeeper.Infrastructure.Services.Api;
using MemoryKeeper.Infrastructure.Services.Api.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class MemoryKeeperOperationsApiRepositoryTests
{
    [Fact]
    public async Task AutoTagEndpoints_MatchBackendContractAndUseBackendMonthlyLimit()
    {
        var handler = new RecordingHandler
        {
            Responses =
            {
                ["GET /api/memorykeeper/auto-tags/status"] =
                    "{\"service_available\":true,\"credential_ready\":true,\"worker_online\":true,\"quota_available\":false,\"monthly_limit_reached\":true,\"quota_waiting_count\":4,\"waiting_count\":7,\"processing_count\":1,\"failed_count\":2,\"today_completed_count\":3,\"monthly_usage\":777,\"monthly_limit\":777,\"monthly_remaining\":0,\"curation_version\":2}",
                ["GET /api/memorykeeper/auto-tags/failed?page=1&page_size=50"] =
                    "{\"items\":[{\"job_id\":9,\"file_id\":\"abc\",\"failed_at\":null,\"retry_count\":1,\"safe_error_code\":\"TEMPORARY\",\"retryable\":true}],\"total\":1,\"page\":1,\"page_size\":50}",
                ["POST /api/memorykeeper/auto-tags/retry-failed?limit=100"] =
                    "{\"requested_count\":2,\"requeued_count\":1,\"skipped_count\":1,\"failed_count\":1}",
                ["POST /api/memorykeeper/auto-tags/jobs/9/retry"] =
                    "{\"requested_count\":1,\"requeued_count\":1,\"skipped_count\":0,\"failed_count\":0}",
            },
        };
        using var provider = BuildProvider(handler);
        var repository = provider.GetRequiredService<IMemoryKeeperOperationsApiRepository>();

        var status = await repository.GetAutoTagStatusAsync();
        var failed = await repository.GetFailedAutoTagsAsync();
        var bulk = await repository.RetryFailedAutoTagsAsync();
        var one = await repository.RetryAutoTagJobAsync(9);

        Assert.Equal(777, status.MonthlyLimit);
        Assert.Equal(4, status.QuotaWaitingCount);
        Assert.True(status.MonthlyLimitReached);
        Assert.True(Assert.Single(failed.Items).Retryable);
        Assert.Equal(1, bulk.RequeuedCount);
        Assert.Equal(1, one.RequeuedCount);
    }

    [Fact]
    public async Task ResetPreviewAndExecute_MatchPostBodyAndPreservationResponse()
    {
        var handler = new RecordingHandler
        {
            Responses =
            {
                ["POST /api/memorykeeper/reset/preview"] =
                    "{\"memorykeeper_file_count\":10,\"place_count\":2,\"user_tag_count\":3,\"favorite_count\":4,\"memo_count\":5,\"file_tag_relation_count\":6,\"file_tag_suppression_count\":1,\"pending_count\":2,\"preserved_common_file_count\":10,\"preserved_raw_vision_count\":8,\"shared_with_other_service_count\":1,\"upload_job_count\":0,\"active_upload_job_count\":0,\"processing_vision_job_count\":0,\"reset_blocked\":false}",
                ["POST /api/memorykeeper/reset/execute"] =
                    "{\"reset_completed\":true,\"affected_file_count\":10,\"removed_place_count\":2,\"removed_user_tag_count\":3,\"cleared_state_count\":10,\"preserved_common_file_count\":10,\"preserved_raw_vision_count\":8,\"reset_event_cursor\":99}",
            },
        };
        using var provider = BuildProvider(handler);
        var repository = provider.GetRequiredService<IMemoryKeeperOperationsApiRepository>();

        var preview = await repository.PreviewResetAsync();
        var result = await repository.ExecuteResetAsync(
            new MemoryKeeperResetExecuteRequest { Confirmation = "RESET_MEMORYKEEPER" });

        Assert.Equal(10, preview.PreservedCommonFileCount);
        Assert.Equal(8, preview.PreservedRawVisionCount);
        Assert.True(result.ResetCompleted);
        using var body = JsonDocument.Parse(handler.Bodies["POST /api/memorykeeper/reset/execute"]);
        Assert.Equal("RESET_MEMORYKEEPER", body.RootElement.GetProperty("confirmation").GetString());
    }

    [Fact]
    public async Task OriginalDownload_UsesAuthenticatedBackendClientAndDoesNotMutateSource()
    {
        var handler = new RecordingHandler
        {
            Responses = { ["GET /api/common/gallery/file/original"] = "original-bytes" },
        };
        using var provider = BuildProvider(handler);
        var client = provider.GetRequiredService<BaseApiClient>();
        await using var destination = new MemoryStream();

        await client.DownloadToAsync("/api/common/gallery/file/original", destination);

        Assert.Equal("original-bytes", Encoding.UTF8.GetString(destination.ToArray()));
        Assert.Contains("Bearer operations-test-token", handler.AuthorizationHeaders);
    }

    private static ServiceProvider BuildProvider(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddTcBackendApiClient(options =>
        {
            options.ApiBaseUrl = "http://localhost:8000";
            options.AuthToken = "operations-test-token";
            options.Timeout = 10;
            options.RetryCount = 0;
        });
        services.PostConfigure<TcBackendOptions>(options =>
        {
            options.ApiBaseUrl = "http://localhost:8000";
            options.AuthToken = "operations-test-token";
        });
        services.AddHttpClient(BaseApiClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddSingleton<IMemoryKeeperOperationsApiRepository, MemoryKeeperOperationsApiRepository>();
        return services.BuildServiceProvider();
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public Dictionary<string, string> Responses { get; init; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Bodies { get; } = new(StringComparer.Ordinal);
        public List<string> AuthorizationHeaders { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var key = $"{request.Method.Method} {request.RequestUri!.PathAndQuery}";
            AuthorizationHeaders.Add(request.Headers.Authorization?.ToString() ?? string.Empty);
            if (request.Content is not null)
            {
                Bodies[key] = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            var found = Responses.TryGetValue(key, out var body);
            return new HttpResponseMessage(found ? HttpStatusCode.OK : HttpStatusCode.NotFound)
            {
                Content = new StringContent(body ?? $"missing stub: {key}", Encoding.UTF8, "application/json"),
            };
        }
    }
}
