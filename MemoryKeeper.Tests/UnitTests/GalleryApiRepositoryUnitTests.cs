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
            """{"items":[{"file_id":"11111111-1111-1111-1111-111111111111","filename":"a.jpg","preview_url":"/p","thumbnail_url":"/t","capture_datetime":"2024-01-02T03:04:05Z","country":"Korea","city":"Seoul","place_name":"Namsan","camera_model":"X","favorite":true,"has_gps":true,"has_ai_tag":false,"service_name":"MemoryKeeper"}],"page":1,"page_size":20,"total":1,"sort":"capture_datetime_desc"}""";
        handler.Map["GET /api/common/gallery/map?service_name=MemoryKeeper"] =
            """{"items":[{"file_id":"11111111-1111-1111-1111-111111111111","latitude":37.5,"longitude":127.0,"place_name":"Namsan","thumbnail":"/t","year":2024,"service_name":"MemoryKeeper"}],"total":1}""";
        handler.Map["GET /api/common/gallery/timeline?service_name=MemoryKeeper"] =
            """{"items":[{"year":2024,"count":3}],"total":1}""";
        handler.Map["GET /api/common/gallery/statistics?service_name=MemoryKeeper"] =
            """{"total_photos":3,"gps_count":2,"ai_tag_count":1,"by_camera":[{"name":"X","count":3}],"by_country":[{"name":"Korea","count":3}],"by_year":[{"name":"2024","count":3}],"by_service":[{"name":"MemoryKeeper","count":3}]}""";
        handler.Map["GET /api/common/gallery/11111111-1111-1111-1111-111111111111"] =
            """{"file_id":"11111111-1111-1111-1111-111111111111","filename":"a.jpg","extension":"jpg","mime_type":"image/jpeg","file_size":10,"width":100,"height":200,"favorite":false,"service_name":"MemoryKeeper","storage_path":"/o","preview_url":"/p","thumbnail_url":"/t","original_url":"/o","metadata":{"iso":100},"ai_tags":[],"user_tags":[],"history_count":0}""";

        using var provider = BuildProvider(handler);
        var repo = provider.GetRequiredService<IGalleryApiRepository>();

        var photos = await repo.GetPhotosAsync();
        Assert.Equal(1, photos.TotalCount);
        Assert.Single(photos.Items);
        Assert.Equal("a.jpg", photos.Items[0].Filename);
        Assert.Equal("Korea", photos.Items[0].Country);

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
    }

    private static ServiceProvider BuildProvider(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.Configure<TcBackendOptions>(o =>
        {
            o.ApiBaseUrl = "http://localhost:8000";
            o.Timeout = 10;
            o.RetryCount = 0;
            o.ServiceName = "MemoryKeeper";
            o.Version = "1.0.0";
        });
        services.AddHttpClient(BaseApiClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddSingleton<BaseApiClient>();
        services.AddSingleton<IGalleryApiRepository, GalleryApiRepository>();
        return services.BuildServiceProvider();
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public Dictionary<string, string> Map { get; } = new(StringComparer.Ordinal);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            var key = $"{request.Method.Method} {path}";
            if (!Map.TryGetValue(key, out var body))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent($"missing stub: {key}", Encoding.UTF8, "text/plain"),
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
