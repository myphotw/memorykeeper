using System.Net;
using System.Text;
using System.Text.Json;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Infrastructure;
using MemoryKeeper.Infrastructure.Repositories.Api;
using MemoryKeeper.Infrastructure.Services.Api;
using MemoryKeeper.Infrastructure.Services.Api.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class MemoryKeeperWriteApiRepositoryTests
{
    private const string FileId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task MetadataPatch_OnlySendsChangedFields_AndDeleteUsesMemoryKeeperEndpoint()
    {
        var patchKey = $"PATCH /api/memorykeeper/files/{FileId}/metadata";
        var deleteKey = $"DELETE /api/memorykeeper/files/{FileId}";
        var handler = new RecordingHandler
        {
            Responses =
            {
                [patchKey] = $"{{\"file_id\":\"{FileId}\",\"favorite\":true,\"memo\":null,\"revision\":4,\"gps_lat\":37.5,\"gps_lon\":127.0,\"country\":\"대한민국\",\"province\":null,\"city\":null,\"district\":null,\"place_name\":null,\"memorykeeper_place_id\":null,\"place_revision\":9}}",
                [deleteKey] = $"{{\"file_id\":\"{FileId}\",\"cleanup_status\":\"CLEANED\",\"physical_file_deleted\":true}}",
            },
        };
        using var provider = BuildProvider(handler);
        var repository = provider.GetRequiredService<IMemoryKeeperWriteApiRepository>();

        var patched = await repository.PatchMetadataAsync(FileId, new MemoryKeeperFileMetadataPatchRequest
        {
            ExpectedRevision = 3,
            Favorite = true,
            ChangedFields = new HashSet<string> { "favorite" },
        });
        var deleted = await repository.DeleteFileAsync(FileId);

        using var payload = JsonDocument.Parse(handler.Bodies[patchKey]);
        Assert.Equal(2, payload.RootElement.EnumerateObject().Count());
        Assert.Equal(3, payload.RootElement.GetProperty("expected_revision").GetInt32());
        Assert.True(payload.RootElement.GetProperty("favorite").GetBoolean());
        Assert.Equal(4, patched.Revision);
        Assert.Equal(9, patched.PlaceRevision);
        Assert.True(deleted.PhysicalFileDeleted);
    }

    [Fact]
    public async Task MetadataPatch_CanExplicitlyClearMemoAndRawLocation()
    {
        var key = $"PATCH /api/memorykeeper/files/{FileId}/metadata";
        var handler = new RecordingHandler
        {
            Responses =
            {
                [key] = $"{{\"file_id\":\"{FileId}\",\"favorite\":false,\"memo\":null,\"revision\":2,\"gps_lat\":null,\"gps_lon\":null,\"country\":null,\"province\":null,\"city\":null,\"district\":null,\"place_name\":null,\"memorykeeper_place_id\":null,\"place_revision\":7}}",
            },
        };
        using var provider = BuildProvider(handler);
        var repository = provider.GetRequiredService<IMemoryKeeperWriteApiRepository>();

        await repository.PatchMetadataAsync(FileId, new MemoryKeeperFileMetadataPatchRequest
        {
            ExpectedRevision = 1,
            Memo = null,
            GpsLat = null,
            GpsLon = null,
            ChangedFields = new HashSet<string> { "memo", "gps_lat", "gps_lon" },
        });

        using var payload = JsonDocument.Parse(handler.Bodies[key]);
        Assert.Equal(JsonValueKind.Null, payload.RootElement.GetProperty("memo").ValueKind);
        Assert.Equal(JsonValueKind.Null, payload.RootElement.GetProperty("gps_lat").ValueKind);
        Assert.False(payload.RootElement.TryGetProperty("country", out _));
    }

    [Fact]
    public async Task TagCrudMergeAndFileRelations_UseIntegerTagIdentityAndRevisions()
    {
        var handler = new RecordingHandler
        {
            Responses =
            {
                ["GET /api/memorykeeper/tags?limit=500&offset=0"] = "{\"items\":[{\"id\":11,\"name\":\"가족\",\"tag_type\":\"USER\",\"source\":\"USER\",\"favorite\":true,\"usage_count\":1,\"revision\":2}],\"total\":1}",
                ["POST /api/memorykeeper/tags"] = "{\"id\":12,\"name\":\"여행\",\"tag_type\":\"USER\",\"source\":\"USER\",\"favorite\":false,\"usage_count\":0,\"revision\":1}",
                ["PATCH /api/memorykeeper/tags/11"] = "{\"id\":11,\"name\":\"우리 가족\",\"tag_type\":\"USER\",\"source\":\"USER\",\"favorite\":true,\"usage_count\":1,\"revision\":3}",
                ["DELETE /api/memorykeeper/tags/11?expected_revision=3"] = string.Empty,
                ["POST /api/memorykeeper/tags/12/merge"] = "{\"id\":11,\"name\":\"우리 가족\",\"tag_type\":\"USER\",\"source\":\"USER\",\"favorite\":true,\"usage_count\":2,\"revision\":4}",
                [$"POST /api/memorykeeper/files/{FileId}/tags/11"] = $"{{\"file_id\":\"{FileId}\",\"tag_id\":11,\"assigned\":true,\"revision\":5}}",
                [$"DELETE /api/memorykeeper/files/{FileId}/tags/11?expected_revision=5"] = $"{{\"file_id\":\"{FileId}\",\"tag_id\":11,\"assigned\":false,\"revision\":6}}",
            },
            StatusCodes = { ["DELETE /api/memorykeeper/tags/11?expected_revision=3"] = HttpStatusCode.NoContent },
        };
        using var provider = BuildProvider(handler);
        var repository = provider.GetRequiredService<IMemoryKeeperWriteApiRepository>();

        Assert.Equal(11, Assert.Single((await repository.GetTagsAsync()).Items).Id);
        await repository.CreateTagAsync(new MemoryKeeperTagCreateRequest { Name = "여행" });
        await repository.UpdateTagAsync(11, new MemoryKeeperTagUpdateRequest { Revision = 2, Name = "우리 가족" });
        await repository.DeleteTagAsync(11, 3);
        await repository.MergeTagAsync(12, new MemoryKeeperTagMergeRequest { SourceRevision = 1, TargetTagId = 11, TargetRevision = 3 });
        Assert.Equal(5, (await repository.AssignFileTagAsync(FileId, 11, 4)).Revision);
        Assert.Equal(6, (await repository.RemoveFileTagAsync(FileId, 11, 5)).Revision);

        using var createPayload = JsonDocument.Parse(handler.Bodies["POST /api/memorykeeper/tags"]);
        Assert.Equal("여행", createPayload.RootElement.GetProperty("name").GetString());
        Assert.False(createPayload.RootElement.GetProperty("favorite").GetBoolean());

        using var updatePayload = JsonDocument.Parse(handler.Bodies["PATCH /api/memorykeeper/tags/11"]);
        Assert.Equal(2, updatePayload.RootElement.GetProperty("revision").GetInt32());
        Assert.Equal("우리 가족", updatePayload.RootElement.GetProperty("name").GetString());
        Assert.False(updatePayload.RootElement.TryGetProperty("favorite", out _));

        using var mergePayload = JsonDocument.Parse(handler.Bodies["POST /api/memorykeeper/tags/12/merge"]);
        Assert.Equal(1, mergePayload.RootElement.GetProperty("source_revision").GetInt32());
        Assert.Equal(11, mergePayload.RootElement.GetProperty("target_tag_id").GetInt32());
        Assert.Equal(3, mergePayload.RootElement.GetProperty("target_revision").GetInt32());

        using var assignPayload = JsonDocument.Parse(handler.Bodies[$"POST /api/memorykeeper/files/{FileId}/tags/11"]);
        Assert.Equal(4, assignPayload.RootElement.GetProperty("expected_revision").GetInt32());
        Assert.False(handler.Bodies.ContainsKey("DELETE /api/memorykeeper/tags/11?expected_revision=3"));
        Assert.False(handler.Bodies.ContainsKey($"DELETE /api/memorykeeper/files/{FileId}/tags/11?expected_revision=5"));
    }

    [Fact]
    public async Task PendingListAndBatchAssignment_PreserveRawGeographyAndPlaceRevision()
    {
        var pendingKey = "GET /api/memorykeeper/pending?page=1&page_size=200&include_suggestions=true";
        var assignKey = "POST /api/memorykeeper/pending/assign-place";
        var placeId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var handler = new RecordingHandler
        {
            Responses =
            {
                [pendingKey] = $"{{\"items\":[{{\"file_id\":\"{FileId}\",\"thumbnail_url\":\"/thumb.jpg\",\"capture_datetime\":\"2026-08-15T01:00:00Z\",\"gps_lat\":37.5,\"gps_lon\":127.0,\"country\":\"대한민국\",\"province\":\"서울특별시\",\"city\":\"서울\",\"district\":\"종로구\",\"place_name\":\"원시 주소\",\"memorykeeper_place_id\":null,\"place_revision\":7}}],\"total\":1,\"page\":1,\"page_size\":200}}",
                [assignKey] = $"{{\"items\":[{{\"file_id\":\"{FileId}\",\"memorykeeper_place_id\":\"{placeId:D}\",\"place_revision\":8}}],\"assigned_count\":1}}",
            },
        };
        using var provider = BuildProvider(handler);
        var repository = provider.GetRequiredService<IMemoryKeeperWriteApiRepository>();

        var item = Assert.Single((await repository.GetPendingAsync()).Items);
        Assert.Equal("원시 주소", item.PlaceName);
        Assert.Equal(7, item.PlaceRevision);
        Assert.Equal("http://localhost:8000/thumb.jpg", item.ThumbnailUrl);
        await repository.AssignPendingPlaceAsync(new MemoryKeeperPendingAssignRequest
        {
            FileIds = [FileId],
            MemorykeeperPlaceId = placeId,
            ExpectedRevisions = new Dictionary<string, int> { [FileId] = 7 },
        });

        using var payload = JsonDocument.Parse(handler.Bodies[assignKey]);
        Assert.Equal(7, payload.RootElement.GetProperty("expected_revisions").GetProperty(FileId).GetInt32());
        Assert.Equal(placeId, payload.RootElement.GetProperty("memorykeeper_place_id").GetGuid());
    }

    [Fact]
    public async Task Conflict_IsExposedToCallerForRefreshFlow()
    {
        var key = $"PATCH /api/memorykeeper/files/{FileId}/metadata";
        var handler = new RecordingHandler
        {
            Responses = { [key] = "{\"detail\":{\"current_revision\":5}}" },
            StatusCodes = { [key] = HttpStatusCode.Conflict },
        };
        using var provider = BuildProvider(handler);
        var repository = provider.GetRequiredService<IMemoryKeeperWriteApiRepository>();

        var error = await Assert.ThrowsAsync<ApiException>(() => repository.PatchMetadataAsync(
            FileId,
            new MemoryKeeperFileMetadataPatchRequest
            {
                ExpectedRevision = 4,
                Memo = "stale",
                ChangedFields = new HashSet<string> { "memo" },
            }));
        Assert.Equal(HttpStatusCode.Conflict, error.StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Delete_ExposesBackendStatusForFriendlyUx(HttpStatusCode statusCode)
    {
        var key = $"DELETE /api/memorykeeper/files/{FileId}";
        var handler = new RecordingHandler
        {
            Responses = { [key] = "{\"detail\":\"delete failed\"}" },
            StatusCodes = { [key] = statusCode },
        };
        using var provider = BuildProvider(handler);
        var repository = provider.GetRequiredService<IMemoryKeeperWriteApiRepository>();

        var error = await Assert.ThrowsAsync<ApiException>(() => repository.DeleteFileAsync(FileId));
        Assert.Equal(statusCode, error.StatusCode);
    }

    private static ServiceProvider BuildProvider(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddTcBackendApiClient(options =>
        {
            options.ApiBaseUrl = "http://localhost:8000";
            options.AuthToken = "write-test-token";
            options.Timeout = 10;
            options.RetryCount = 0;
        });
        services.PostConfigure<TcBackendOptions>(options =>
        {
            options.ApiBaseUrl = "http://localhost:8000";
            options.AuthToken = "write-test-token";
        });
        services.AddHttpClient(BaseApiClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddSingleton<IMemoryKeeperWriteApiRepository, MemoryKeeperWriteApiRepository>();
        return services.BuildServiceProvider();
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public Dictionary<string, string> Responses { get; init; } = new(StringComparer.Ordinal);
        public Dictionary<string, HttpStatusCode> StatusCodes { get; init; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Bodies { get; } = new(StringComparer.Ordinal);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var key = $"{request.Method.Method} {request.RequestUri!.PathAndQuery}";
            if (request.Content is not null)
            {
                Bodies[key] = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            var found = Responses.TryGetValue(key, out var body);
            var status = StatusCodes.TryGetValue(key, out var configured)
                ? configured
                : found ? HttpStatusCode.OK : HttpStatusCode.NotFound;
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body ?? $"missing stub: {key}", Encoding.UTF8, "application/json"),
            };
        }
    }
}
