using System.Net;
using System.Text;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Infrastructure.Repositories.Api;
using MemoryKeeper.Infrastructure.Services.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemoryKeeper.Tests.UnitTests;

/// <summary>
/// Verifies GalleryApiRepository path building and JSON mapping without a live DB.
/// </summary>
public sealed class GalleryApiRepositoryUnitTests
{
    [Fact]
    public async Task GetPhotos_Map_Timeline_Statistics_Deserialize()
    {
        var handler = new StubHandler();
        handler.Map["GET /api/common/gallery?page=1&page_size=20&sort=capture_datetime_desc&service_name=MemoryKeeper"] =
            """{"items":[{"file_id":"11111111-1111-1111-1111-111111111111","filename":"a.jpg","preview_url":"/p","thumbnail_url":"/t","capture_datetime":"2024-01-02T03:04:05Z","country":"Korea","city":"Seoul","place_name":"Namsan","camera_model":"X","favorite":true,"memo":"trip","metadata_revision":5,"incomplete":true,"place_revision":2,"has_gps":true,"has_ai_tag":false,"service_name":"MemoryKeeper"}],"page":1,"page_size":20,"total":1,"sort":"capture_datetime_desc"}""";
        handler.Map["GET /api/common/gallery/map?service_name=MemoryKeeper"] =
            """{"items":[{"file_id":"11111111-1111-1111-1111-111111111111","latitude":37.5,"longitude":127.0,"place_name":"Namsan","thumbnail":"/t","year":2024,"service_name":"MemoryKeeper"}],"total":1}""";
        handler.Map["GET /api/common/gallery/timeline?service_name=MemoryKeeper"] =
            """{"items":[{"year":2024,"count":3}],"total":1}""";
        handler.Map["GET /api/common/gallery/statistics?service_name=MemoryKeeper"] =
            """{"total_photos":3,"gps_count":2,"ai_tag_count":1,"by_camera":[{"name":"X","count":3}],"by_country":[{"name":"Korea","count":3}],"by_year":[{"name":"2024","count":3}],"by_service":[{"name":"MemoryKeeper","count":3}]}""";
        handler.Map["GET /api/common/gallery/11111111-1111-1111-1111-111111111111"] =
            """{"file_id":"11111111-1111-1111-1111-111111111111","filename":"a.jpg","extension":"jpg","mime_type":"image/jpeg","file_size":10,"width":100,"height":200,"favorite":true,"memo":"가족 여행","metadata_revision":5,"incomplete":false,"place_revision":9,"service_name":"MemoryKeeper","storage_path":"/o","preview_url":"/p","thumbnail_url":"/t","original_url":"/o","metadata":{"iso":100},"ai_tags":[{"tag":"사람","source":"AI","tag_type":"AI"}],"user_tags":[{"tag":"가족","source":"USER","tag_type":"USER","tag_id":42}],"history_count":0}""";

        using var provider = BuildProvider(handler);
        var repo = provider.GetRequiredService<IGalleryApiRepository>();

        var photos = await repo.GetPhotosAsync();
        Assert.Equal(1, photos.TotalCount);
        Assert.Single(photos.Items);
        Assert.Equal("a.jpg", photos.Items[0].Filename);
        Assert.Equal("Korea", photos.Items[0].Country);
        Assert.Equal(5, photos.Items[0].MetadataRevision);
        Assert.Equal(2, photos.Items[0].PlaceRevision);
        Assert.True(photos.Items[0].Incomplete);

        var map = await repo.GetMapAsync();
        Assert.Equal(1, map.Total);
        Assert.Equal(37.5, map.Items[0].Latitude);

        var timeline = await repo.GetTimelineAsync();
        Assert.Equal(1, timeline.Total);
        Assert.Equal(2024, timeline.Items[0].Year);

        var stats = await repo.GetStatisticsAsync();
        Assert.Equal(3, stats.TotalPhotos);
        Assert.Equal(2, stats.GpsCount);
        Assert.Single(stats.ByCamera);

        var detail = await repo.GetPhotoAsync(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        Assert.Equal("a.jpg", detail.Filename);
        Assert.Equal(100, detail.Width);
        Assert.Equal("가족 여행", detail.Memo);
        Assert.Equal(5, detail.MetadataRevision);
        Assert.Equal(9, detail.PlaceRevision);
        Assert.Equal(42, Assert.Single(detail.UserTags).TagId);
        Assert.Single(detail.AiTags);
        Assert.All(handler.AuthorizationHeaders, header =>
        {
            Assert.Equal("Bearer", header?.Scheme);
            Assert.Equal("gallery-test-token", header?.Parameter);
        });
    }

    [Fact]
    public async Task Empty_Gallery_Deserializes()
    {
        var handler = new StubHandler();
        handler.Map["GET /api/common/gallery?page=1&page_size=20&sort=capture_datetime_desc&service_name=MemoryKeeper"] =
            """{"items":[],"page":1,"page_size":20,"total":0,"sort":"capture_datetime_desc"}""";
        using var provider = BuildProvider(handler);
        var repo = provider.GetRequiredService<IGalleryApiRepository>();

        var result = await repo.GetPhotosAsync();

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task Gallery_Backend_Error_Is_Classified()
    {
        var handler = new StubHandler();
        var key = "GET /api/common/gallery?page=1&page_size=20&sort=capture_datetime_desc&service_name=MemoryKeeper";
        handler.Map[key] = "unavailable";
        handler.StatusCodes[key] = HttpStatusCode.ServiceUnavailable;
        using var provider = BuildProvider(handler);
        var repo = provider.GetRequiredService<IGalleryApiRepository>();

        var error = await Assert.ThrowsAsync<ApiException>(() => repo.GetPhotosAsync());

        Assert.Equal(ApiErrorCategory.BackendUnavailable, error.Category);
    }

    [Fact]
    public async Task Catalog_Recovers_Gps_And_Region_From_Detail_When_Map_Row_Is_Missing()
    {
        var handler = new StubHandler();
        const string fileId = "22222222-2222-2222-2222-222222222222";
        const string placeId = "33333333-3333-3333-3333-333333333333";
        handler.Map["GET /api/common/gallery/search?service_name=MemoryKeeper&page=1&page_size=200&sort=capture_datetime_desc"] =
            $$"""{"items":[{"file_id":"{{fileId}}","filename":"20260815_140628.jpg","thumbnail_url":"/api/common/gallery/{{fileId}}/thumbnail","preview_url":"/api/common/gallery/{{fileId}}/preview","capture_datetime":"2026-08-15T14:06:28+09:00","country":"대한민국","city":"구례군","place_name":"원기교","memorykeeper_place_id":"{{placeId}}","place_display_name":"피아골","has_gps":true,"service_name":"MemoryKeeper"}],"page":1,"page_size":200,"total":1,"sort":"capture_datetime_desc"}""";
        handler.Map["GET /api/common/gallery/map?service_name=MemoryKeeper"] =
            """{"items":[],"total":0}""";
        handler.Map["GET /api/memorykeeper/places?limit=500&offset=0"] =
            $$"""{"items":[{"id":"{{placeId}}","display_name":"피아골","country":"대한민국","province":"전라남도","city":"구례군","district":"토지면","latitude":35.22742,"longitude":127.59052,"radius_m":100,"active":true,"favorite":false,"revision":1}],"total":1,"limit":500,"offset":0}""";
        handler.Map[$"GET /api/common/gallery/{fileId}"] =
            $$"""{"file_id":"{{fileId}}","filename":"20260815_140628.jpg","service_name":"MemoryKeeper","thumbnail_url":"/api/common/gallery/{{fileId}}/thumbnail","preview_url":"/api/common/gallery/{{fileId}}/preview","metadata":{"gps_lat":35.22742,"gps_lon":127.59052,"country":"대한민국","province":"전라남도","city":"구례군","district":"토지면","place_name":"원기교"},"ai_tags":[],"user_tags":[],"history_count":0}""";

        using var provider = BuildProvider(handler);
        var catalog = provider.GetRequiredService<IGalleryPhotoCatalog>();

        var snapshot = await catalog.QueryAsync();

        var photo = Assert.Single(snapshot.Photos);
        Assert.Equal(fileId, photo.FileId);
        Assert.Contains(fileId, photo.ThumbnailUrl);
        var location = Assert.Single(snapshot.LocationMetadataByFileId).Value;
        Assert.Equal(35.22742, location.Latitude!.Value, 5);
        Assert.Equal(127.59052, location.Longitude!.Value, 5);
        Assert.Equal("대한민국", location.Country);
        Assert.Equal("전라남도", location.Province);
        Assert.Equal("구례군", location.City);
        Assert.Equal("토지면", location.District);
        Assert.Equal("원기교", location.PlaceName);
        var registeredPlace = Assert.Single(snapshot.RegisteredPlacesById).Value;
        Assert.Equal("대한민국", registeredPlace.Country);
        Assert.Equal("전라남도", registeredPlace.Province);
        Assert.Equal("구례군", registeredPlace.City);
    }

    private static ServiceProvider BuildProvider(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddTcBackendApiClient(o =>
        {
            o.ApiBaseUrl = "http://localhost:8000";
            o.AuthToken = "gallery-test-token";
            o.Timeout = 10;
            o.RetryCount = 0;
            o.ServiceName = "MemoryKeeper";
            o.Version = "1.0.0";
        });
        services.PostConfigure<TcBackendOptions>(o =>
        {
            o.ApiBaseUrl = "http://localhost:8000";
            o.AuthToken = "gallery-test-token";
        });
        services.AddHttpClient(BaseApiClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddSingleton<IGalleryApiRepository, GalleryApiRepository>();
        services.AddSingleton<IMemoryKeeperPlaceApiRepository, MemoryKeeperPlaceApiRepository>();
        services.AddSingleton<IGalleryPhotoCatalog, GalleryPhotoCatalog>();
        return services.BuildServiceProvider();
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public Dictionary<string, string> Map { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, HttpStatusCode> StatusCodes { get; } = new(StringComparer.Ordinal);

        public List<System.Net.Http.Headers.AuthenticationHeaderValue?> AuthorizationHeaders { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            AuthorizationHeaders.Add(request.Headers.Authorization);
            var key = $"{request.Method.Method} {path}";
            if (!Map.TryGetValue(key, out var body))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent($"missing stub: {key}", Encoding.UTF8, "text/plain"),
                });
            }

            var statusCode = StatusCodes.TryGetValue(key, out var configuredStatus)
                ? configuredStatus
                : HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
