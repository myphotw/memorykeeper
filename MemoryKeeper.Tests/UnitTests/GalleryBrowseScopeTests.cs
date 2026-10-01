using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class GalleryBrowseScopeTests
{
    [Fact]
    public void CanonicalRegion_MapsToEveryExactBackendRegionWithoutInventingAContract()
    {
        var scope = GalleryBrowseScope.ForCanonicalRegion(
            2025,
            "Japan",
            "오사카",
            ["오사카", "Osaka", "Osaka"]);

        var queries = GalleryBrowseScopeQueryMapper.ToQueries(scope, limit: 50);

        Assert.Equal(2, queries.Count);
        Assert.Equal(
            ["Osaka", "오사카"],
            queries.Select(query => query.Region));
        Assert.All(queries, query =>
        {
            Assert.Equal(2025, query.Year);
            Assert.Equal("Japan", query.Country);
            Assert.Equal(50, query.Limit);
        });
        Assert.Throws<InvalidOperationException>(() => GalleryBrowseScopeQueryMapper.ToQuery(scope));
    }

    [Fact]
    public void DefaultFeed_MapsToUnfilteredFastGalleryQuery()
    {
        var query = GalleryBrowseScopeQueryMapper.ToQuery(GalleryBrowseScope.DefaultFeed, limit: 50);

        Assert.Equal(50, query.Limit);
        Assert.Null(query.Cursor);
        Assert.Null(query.Year);
        Assert.Null(query.Country);
        Assert.Null(query.Region);
        Assert.Null(query.LocationKey);
        Assert.Null(query.PlaceId);
        Assert.Null(query.PhotoCategory);
    }

    [Fact]
    public void Year_MapsToYearFilterWithoutUiTypes()
    {
        var scope = GalleryBrowseScope.ForYear(2026);

        var query = GalleryBrowseScopeQueryMapper.ToQuery(scope, limit: 50, cursor: "next");

        Assert.Equal(2026, query.Year);
        Assert.Equal(50, query.Limit);
        Assert.Equal("next", query.Cursor);
    }

    [Fact]
    public void YearAndPlace_MapsToExactFastGalleryHierarchyFilter()
    {
        var placeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var scope = GalleryBrowseScope.ForHierarchy(
            year: 2025,
            country: "일본",
            region: "교토",
            locationKey: "registered:11111111-1111-1111-1111-111111111111",
            placeId: placeId);

        var query = GalleryBrowseScopeQueryMapper.ToQuery(scope, limit: 50, cursor: "next");

        Assert.Equal(2025, query.Year);
        Assert.Equal("일본", query.Country);
        Assert.Equal("교토", query.Region);
        Assert.Equal("registered:11111111-1111-1111-1111-111111111111", query.LocationKey);
        Assert.Null(query.PlaceId);
        Assert.Equal("next", query.Cursor);
    }

    [Fact]
    public void UnclassifiedScopes_KeepPlaceAndDateMeaningSeparate()
    {
        var place = GalleryBrowseScopeQueryMapper.ToQuery(GalleryBrowseScope.ForUnclassified());
        var date = GalleryBrowseScopeQueryMapper.ToQuery(GalleryBrowseScope.ForDateUnclassified(2025));

        Assert.True(place.Unclassified is true);
        Assert.Null(place.DateUnclassified);
        Assert.Null(place.Year);
        Assert.True(date.DateUnclassified is true);
        Assert.Null(date.Unclassified);
        Assert.Equal(2025, date.Year);
    }

    [Fact]
    public void YearCatalog_UsesNonEmptyHierarchyYearsNewestFirst()
    {
        var hierarchy = new FastGalleryHierarchyDto
        {
            Items =
            [
                new FastGalleryHierarchyNodeDto { Year = 2024, Count = 2 },
                new FastGalleryHierarchyNodeDto { Year = 2026, Count = 1 },
                new FastGalleryHierarchyNodeDto { Year = 2025, Count = 0 },
                new FastGalleryHierarchyNodeDto { Year = 2024, Count = 3 },
                new FastGalleryHierarchyNodeDto { Count = 10 },
            ],
        };

        var years = FastGalleryYearCatalog.FromHierarchy(hierarchy);

        Assert.Equal(new[] { 2026, 2024 }, years);
    }
}
