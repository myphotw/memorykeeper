using System.Text.Json;
using MemoryKeeper.App.Services;
using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Infrastructure.Repositories.Api;
using MemoryKeeper.Infrastructure.Services.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class HomeDashboardImageTests
{
    private const string Origin = "https://backend.test";
    private const string FileId = "000000000000002a";

    [Theory]
    [InlineData("jpg", "image/jpeg")]
    [InlineData("mp4", "video/mp4")]
    public async Task FastGallery_PropagatesJpegDerivativeAndPreviewToAllHomeCards(string extension, string mimeType)
    {
        var page = JsonSerializer.Deserialize<FastGalleryPhotoPageDto>($$"""
            {"items":[{"file_id":"{{FileId}}","filename":"memory.{{extension}}",
            "extension":"{{extension}}","mime_type":"{{mimeType}}","favorite":true,
            "thumbnail_url":"/media/representative.jpg","preview_url":"/media/preview.jpg",
            "memorykeeper_place_id":"00000000-0000-0000-0000-000000000001",
            "place_display_name":"부산","effective_capture_datetime":"2026-01-02T00:00:00+09:00"}]}
            """)!;
        var api = new FastGalleryStub(page);

        var dashboard = await GalleryBackendBridge.GetFastHomeDashboardAsync(api, Origin);

        var photo = Assert.Single(dashboard.RecentImports);
        Assert.Equal("memory." + extension, photo.FileName);
        Assert.Equal(Origin + "/media/representative.jpg", photo.AbsoluteLibraryPath);
        Assert.Equal(Origin + "/media/preview.jpg", photo.FallbackAbsoluteLibraryPath);
        Assert.Equal(BackendFileIdCodec.ToGuid(FileId), photo.MediaId);
        Assert.Same(photo, Assert.Single(dashboard.Favorites));
        Assert.Equal(photo.AbsoluteLibraryPath, Assert.Single(dashboard.RecentVisits).AbsoluteLibraryPath);
        Assert.Equal(photo.FallbackAbsoluteLibraryPath, dashboard.RecentVisits[0].FallbackAbsoluteLibraryPath);
        Assert.Equal(photo.AbsoluteLibraryPath, Assert.Single(dashboard.HeroMemories).AbsoluteLibraryPath);
        Assert.Equal(photo.FallbackAbsoluteLibraryPath, dashboard.HeroMemories[0].FallbackAbsoluteLibraryPath);
        Assert.Equal(1, api.PageRequests);
    }

    [Fact]
    public async Task FastGallery_MissingThumbnailStillRetainsPreviewFallback()
    {
        var api = new FastGalleryStub(new FastGalleryPhotoPageDto
        {
            Items = [new FastGalleryPhotoDto { FileId = FileId, Filename = "video.mp4", PreviewUrl = "/preview.jpg" }],
        });
        var dashboard = await GalleryBackendBridge.GetFastHomeDashboardAsync(api, Origin);
        var photo = Assert.Single(dashboard.RecentImports);
        Assert.Equal(Origin + $"/api/common/gallery/{FileId}/thumbnail", photo.AbsoluteLibraryPath);
        Assert.Equal(Origin + "/preview.jpg", photo.FallbackAbsoluteLibraryPath);
    }

    [Theory]
    [InlineData("/thumb.jpg", "/thumb.jpg")]
    [InlineData(null, "/preview.jpg")]
    public async Task FastTravel_ToHome_PreservesRepresentativeAndFallbackWithoutPhotoSnapshot(string? thumbnail, string preferred)
    {
        // Do not inherit the developer PC's deployment environment or permit network I/O.
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<TcBackendOptions>(options => options.ApiBaseUrl = Origin);
        services.AddSingleton<IHttpClientFactory, NoHttpClientFactory>();
        services.AddSingleton<BaseApiClient>();
        using var provider = services.BuildServiceProvider();
        var api = new FastTravelStub(new FastTravelAggregatesDto
        {
            Places = [new FastTravelAggregateDto
            {
                MemorykeeperPlaceId = Guid.NewGuid(), PlaceDisplayName = "부산", Country = "대한민국",
                RepresentativeFileId = FileId, RepresentativeThumbnailUrl = thumbnail,
                RepresentativePreviewUrl = "/preview.jpg", PhotoCount = 30, VisitCount = 2,
                CaptureDates = [new DateOnly(2026, 1, 2), new DateOnly(2026, 3, 4)],
            }],
        });
        var repository = new FastGalleryTravelRecordsRepository(api, provider.GetRequiredService<BaseApiClient>(),
            NullLogger<FastGalleryTravelRecordsRepository>.Instance);
        var aggregates = await repository.GetPlaceAggregatesAsync();
        Assert.Empty(Assert.Single(aggregates).Photos);

        var dashboard = HomeDashboardProjection.ApplyAuthoritativePlaceAggregates(new HomeDashboardDto(), aggregates);

        var visit = Assert.Single(dashboard.RecentVisits);
        Assert.Equal(Origin + preferred, visit.AbsoluteLibraryPath);
        Assert.Equal(Origin + "/preview.jpg", visit.FallbackAbsoluteLibraryPath);
        Assert.Equal(BackendFileIdCodec.ToGuid(FileId), visit.RepresentativeMediaId);
        Assert.Equal(2, visit.VisitRecordCount);
        Assert.Equal(30, visit.PhotoCount);
        Assert.Equal(visit.AbsoluteLibraryPath, Assert.Single(dashboard.HeroMemories).AbsoluteLibraryPath);
        Assert.Equal(visit.FallbackAbsoluteLibraryPath, dashboard.HeroMemories[0].FallbackAbsoluteLibraryPath);
    }

    [Fact]
    public void AuthoritativeProjection_KeepsUnchangedDtoInstancesForInFlightThumbnailTargets()
    {
        var photo = new DashboardPhotoDto { MediaId = Guid.NewGuid(), AbsoluteLibraryPath = Origin + "/thumb.jpg" };
        var today = new TodayMemoryPhotoDto { MediaId = photo.MediaId, AbsoluteLibraryPath = photo.AbsoluteLibraryPath };
        var shell = new HomeDashboardDto
        {
            RecentImports = [photo], Favorites = [photo], TodayMemories = [today],
            PendingSummary = new PendingSummaryDto { RepresentativeMediaId = photo.MediaId },
        };

        var updated = HomeDashboardProjection.ApplyAuthoritativePlaceAggregates(shell, []);

        Assert.Same(photo, Assert.Single(updated.RecentImports));
        Assert.Same(photo, Assert.Single(updated.Favorites));
        Assert.Same(today, Assert.Single(updated.TodayMemories));
        Assert.Same(shell.PendingSummary, updated.PendingSummary);
    }

    private sealed class FastGalleryStub(FastGalleryPhotoPageDto page) : IFastGalleryApiRepository
    {
        public int PageRequests { get; private set; }
        public Task<FastGalleryPhotoPageDto> GetPhotosAsync(FastGalleryPhotoQuery query, CancellationToken cancellationToken = default)
        {
            Assert.Equal(50, query.Limit);
            Assert.Null(query.Cursor);
            PageRequests++;
            return Task.FromResult(page);
        }
        public Task<FastGallerySummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default) => Task.FromResult(new FastGallerySummaryDto());
        public Task<FastGalleryHierarchyDto> GetHierarchyAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Home must not load hierarchy.");
    }

    private sealed class FastTravelStub(FastTravelAggregatesDto aggregates) : IFastTravelApiRepository
    {
        public Task<FastTravelAggregatesDto> GetAggregatesAsync(CancellationToken cancellationToken = default) => Task.FromResult(aggregates);
        public Task<FastTravelMemoriesDto> GetMemoriesAsync(DateOnly referenceDate, int limit, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Not used by this projection.");
    }

    private sealed class NoHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("Projection tests must not perform HTTP requests.");
    }
}
