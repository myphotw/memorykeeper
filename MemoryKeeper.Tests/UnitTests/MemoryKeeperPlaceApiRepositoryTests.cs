using System.Net;
using System.Text;
using System.Text.Json;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Infrastructure.Repositories.Api;
using MemoryKeeper.Infrastructure.Services.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class MemoryKeeperPlaceApiRepositoryTests
{
    private static readonly Guid PlaceId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private const string PlaceJson = """
        {"id":"11111111-2222-3333-4444-555555555555","display_name":"피아골","canonical_name":"지리산 피아골","address":"대한민국 전라남도 구례군 토지면","postal_code":"57623","country":"대한민국","province":"전라남도","city":"구례군","district":"토지면","latitude":35.22742,"longitude":127.59052,"radius_m":250,"provider_place_id":"google-1","category":"park","active":true,"favorite":true,"usage_count":3,"last_used_at":"2026-08-15T05:00:00Z","revision":4,"created_at":"2026-08-15T04:00:00Z","updated_at":"2026-08-15T05:00:00Z"}
        """;

    [Fact]
    public async Task List_Create_Update_UseAuthenticatedNasApiAndSnakeCasePayloads()
    {
        var handler = new RecordingHandler
        {
            Responses =
            {
                ["GET /api/memorykeeper/places?limit=500&offset=0"] = $"{{\"items\":[{PlaceJson}],\"total\":1,\"limit\":500,\"offset\":0}}",
                ["POST /api/memorykeeper/places"] = PlaceJson,
                [$"PATCH /api/memorykeeper/places/{PlaceId:D}"] = PlaceJson,
            },
        };
        using var provider = BuildProvider(handler);
        var repository = provider.GetRequiredService<IMemoryKeeperPlaceApiRepository>();

        var list = await repository.GetPlacesAsync();
        var listed = Assert.Single(list.Items);
        Assert.Equal("피아골", listed.DisplayName);
        Assert.Equal("토지면", listed.District);
        Assert.Equal(250, listed.RadiusM);
        Assert.Equal(4, listed.Revision);

        await repository.CreatePlaceAsync(new MemoryKeeperPlaceCreateApiRequest
        {
            DisplayName = "피아골",
            District = "토지면",
            Latitude = 35.22742,
            Longitude = 127.59052,
            RadiusM = 250,
        });
        await repository.UpdatePlaceAsync(PlaceId, new MemoryKeeperPlaceUpdateApiRequest
        {
            Revision = 4,
            DisplayName = "지리산 피아골",
        });

        using var createJson = JsonDocument.Parse(handler.Bodies["POST /api/memorykeeper/places"]);
        Assert.Equal(250, createJson.RootElement.GetProperty("radius_m").GetDouble());
        Assert.Equal("토지면", createJson.RootElement.GetProperty("district").GetString());
        Assert.Contains("\"revision\":4", handler.Bodies[$"PATCH /api/memorykeeper/places/{PlaceId:D}"]);
        Assert.All(handler.AuthorizationHeaders, header => Assert.Equal("Bearer place-test-token", header));
    }

    [Fact]
    public async Task Reclassify_RadiusImpact_AndFileAssignment_UseBackendContracts()
    {
        const string fileId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
        var handler = new RecordingHandler
        {
            Responses =
            {
                [$"POST /api/memorykeeper/places/{PlaceId:D}/reclassify"] =
                    $"{{\"place_id\":\"{PlaceId:D}\",\"scanned\":5,\"assigned\":4,\"reassigned\":2,\"unassigned_outside_radius\":1,\"unchanged\":0}}",
                ["POST /api/memorykeeper/places/radius-impact"] =
                    $"{{\"matched_file_count\":4,\"affected_file_ids\":[\"{fileId}\"],\"overlapping_places\":[]}}",
                [$"PATCH /api/memorykeeper/files/{fileId}/place"] =
                    $"{{\"file_id\":\"{fileId}\",\"memorykeeper_place_id\":\"{PlaceId:D}\",\"place_display_name\":\"피아골\",\"place_revision\":8}}",
            },
        };
        using var provider = BuildProvider(handler);
        var repository = provider.GetRequiredService<IMemoryKeeperPlaceApiRepository>();

        var reclass = await repository.ReclassifyAsync(PlaceId, true);
        var impact = await repository.GetRadiusImpactAsync(new MemoryKeeperRadiusImpactApiRequest
        {
            PlaceId = PlaceId,
            Latitude = 35.2,
            Longitude = 127.5,
            RadiusM = 250,
        });
        var assigned = await repository.AssignFilePlaceAsync(fileId, PlaceId, 7);

        Assert.Equal(4, reclass.Assigned);
        Assert.Equal(2, reclass.Reassigned);
        Assert.Equal(4, impact.MatchedFileCount);
        Assert.Equal(8, assigned.PlaceRevision);
        Assert.Contains("\"reassign_from_other_places\":true", handler.Bodies[$"POST /api/memorykeeper/places/{PlaceId:D}/reclassify"]);
        Assert.Contains("\"expected_revision\":7", handler.Bodies[$"PATCH /api/memorykeeper/files/{fileId}/place"]);
        Assert.Contains($"\"memorykeeper_place_id\":\"{PlaceId:D}\"", handler.Bodies[$"PATCH /api/memorykeeper/files/{fileId}/place"]);
    }

    [Fact]
    public async Task RevisionConflict_BecomesFriendlyDomainException()
    {
        var key = $"PATCH /api/memorykeeper/places/{PlaceId:D}";
        var handler = new RecordingHandler
        {
            Responses = { [key] = "{\"detail\":\"REVISION_CONFLICT\"}" },
            StatusCodes = { [key] = HttpStatusCode.Conflict },
        };
        using var provider = BuildProvider(handler);
        var repository = provider.GetRequiredService<IMemoryKeeperPlaceApiRepository>();

        var error = await Assert.ThrowsAsync<MemoryKeeperPlaceRevisionConflictException>(() =>
            repository.UpdatePlaceAsync(PlaceId, new MemoryKeeperPlaceUpdateApiRequest { Revision = 1 }));

        Assert.Contains("새로 고친", error.Message);
    }

    [Fact]
    public async Task ClearAssignment_SendsExplicitNull_AndDeleteUsesNasEndpoint()
    {
        const string fileId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
        var patchKey = $"PATCH /api/memorykeeper/files/{fileId}/place";
        var deleteKey = $"DELETE /api/memorykeeper/places/{PlaceId:D}";
        var handler = new RecordingHandler
        {
            Responses =
            {
                [patchKey] = $"{{\"file_id\":\"{fileId}\",\"memorykeeper_place_id\":null,\"place_display_name\":null,\"place_canonical_name\":null,\"geocoded_place_name\":\"원기교 주소\",\"place_match_source\":\"MANUAL_CLEAR\",\"place_match_distance_m\":null,\"place_revision\":9}}",
                [deleteKey] = string.Empty,
            },
            StatusCodes = { [deleteKey] = HttpStatusCode.NoContent },
        };
        using var provider = BuildProvider(handler);
        var repository = provider.GetRequiredService<IMemoryKeeperPlaceApiRepository>();

        var cleared = await repository.AssignFilePlaceAsync(fileId, null, 8);
        await repository.DeletePlaceAsync(PlaceId);

        using var payload = JsonDocument.Parse(handler.Bodies[patchKey]);
        Assert.Equal(JsonValueKind.Null, payload.RootElement.GetProperty("memorykeeper_place_id").ValueKind);
        Assert.Equal(9, cleared.PlaceRevision);
        Assert.Contains(deleteKey, handler.RequestKeys);
    }

    private static ServiceProvider BuildProvider(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddTcBackendApiClient(options =>
        {
            options.ApiBaseUrl = "http://localhost:8000";
            options.AuthToken = "place-test-token";
            options.Timeout = 10;
            options.RetryCount = 0;
        });
        services.PostConfigure<TcBackendOptions>(options =>
        {
            options.ApiBaseUrl = "http://localhost:8000";
            options.AuthToken = "place-test-token";
        });
        services.AddHttpClient(BaseApiClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddSingleton<IMemoryKeeperPlaceApiRepository, MemoryKeeperPlaceApiRepository>();
        return services.BuildServiceProvider();
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public Dictionary<string, string> Responses { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, HttpStatusCode> StatusCodes { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Bodies { get; } = new(StringComparer.Ordinal);
        public List<string> AuthorizationHeaders { get; } = [];
        public List<string> RequestKeys { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var key = $"{request.Method.Method} {request.RequestUri!.PathAndQuery}";
            RequestKeys.Add(key);
            AuthorizationHeaders.Add(request.Headers.Authorization?.ToString() ?? string.Empty);
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
