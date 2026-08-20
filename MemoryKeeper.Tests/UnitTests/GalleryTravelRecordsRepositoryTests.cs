using MemoryKeeper.Application.DTOs.Gallery;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Infrastructure.Repositories.Api;
using Microsoft.Extensions.Logging.Abstractions;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class GalleryTravelRecordsRepositoryTests
{
    [Fact]
    public async Task GetPlaceAggregatesAsync_UsesCanonicalCatalogMetadataCoordinatesAndThumbnail()
    {
        var firstId = Guid.NewGuid().ToString();
        var secondId = Guid.NewGuid().ToString();
        var snapshot = new GalleryPhotoCatalogSnapshot
        {
            ApiBaseUrl = "https://backend.example",
            Photos =
            [
                new PhotoDto
                {
                    FileId = firstId,
                    Filename = "first.jpg",
                    PlaceName = "경복궁",
                    Country = null,
                    CaptureDatetime = new DateTimeOffset(2025, 5, 1, 10, 0, 0, TimeSpan.Zero),
                },
                new PhotoDto
                {
                    FileId = secondId,
                    Filename = "second.jpg",
                    PlaceName = "경복궁",
                    Country = "대한민국",
                    ThumbnailUrl = $"/api/common/gallery/{secondId}/thumbnail",
                    Favorite = true,
                    CaptureDatetime = new DateTimeOffset(2025, 5, 2, 10, 0, 0, TimeSpan.Zero),
                },
            ],
            MapMarkers =
            [
                new MapMarkerDto
                {
                    FileId = secondId,
                    Latitude = 37.5796,
                    Longitude = 126.9770,
                    PlaceName = "경복궁",
                },
            ],
        };
        var repository = new GalleryTravelRecordsRepository(
            new FixedCatalog(snapshot),
            NullLogger<GalleryTravelRecordsRepository>.Instance);

        var result = await repository.GetPlaceAggregatesAsync();

        var place = Assert.Single(result);
        Assert.Equal("대한민국", place.Country);
        Assert.Equal(37.5796, place.Latitude, 4);
        Assert.Equal(126.9770, place.Longitude, 4);
        Assert.Equal(2, place.PhotoCount);
        Assert.Equal(2, place.VisitDates.Count);
        Assert.Equal($"https://backend.example/api/common/gallery/{secondId}/thumbnail", place.AbsoluteLibraryPath);
    }

    [Fact]
    public async Task GetPlaceAggregatesAsync_Uses_Detail_Location_When_Map_Marker_Is_Missing()
    {
        var fileId = Guid.NewGuid().ToString();
        var snapshot = new GalleryPhotoCatalogSnapshot
        {
            ApiBaseUrl = "https://backend.example",
            Photos =
            [
                new PhotoDto
                {
                    FileId = fileId,
                    Filename = "20260815_140628.jpg",
                    ThumbnailUrl = $"/api/common/gallery/{fileId}/thumbnail",
                    Country = "대한민국",
                    City = "구례군",
                    PlaceName = "원기교",
                    HasGps = true,
                    CaptureDatetime = new DateTimeOffset(2026, 8, 15, 14, 6, 28, TimeSpan.FromHours(9)),
                },
            ],
            MapMarkers = [],
            LocationMetadataByFileId = new Dictionary<string, GalleryPhotoLocationMetadataDto>
            {
                [fileId] = new()
                {
                    Latitude = 35.22742,
                    Longitude = 127.59052,
                    Country = "대한민국",
                    Province = "전라남도",
                    City = "구례군",
                    District = "토지면",
                    PlaceName = "원기교",
                },
            },
        };
        var repository = new GalleryTravelRecordsRepository(
            new FixedCatalog(snapshot),
            NullLogger<GalleryTravelRecordsRepository>.Instance);

        var place = Assert.Single(await repository.GetPlaceAggregatesAsync());

        Assert.Equal("원기교", place.PlaceName);
        Assert.Equal("대한민국", place.Country);
        Assert.Equal(35.22742, place.Latitude, 5);
        Assert.Equal(127.59052, place.Longitude, 5);
    }

    private sealed class FixedCatalog : IGalleryPhotoCatalog
    {
        private readonly GalleryPhotoCatalogSnapshot _snapshot;

        public FixedCatalog(GalleryPhotoCatalogSnapshot snapshot) => _snapshot = snapshot;

        public Task<GalleryPhotoCatalogSnapshot> QueryAsync(
            int? year = null,
            string? country = null,
            string? keyword = null,
            CancellationToken cancellationToken = default) => Task.FromResult(_snapshot);
    }
}
