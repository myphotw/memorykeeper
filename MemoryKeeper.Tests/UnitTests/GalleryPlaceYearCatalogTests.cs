using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class GalleryPlaceYearCatalogTests
{
    private static readonly Guid PlaceId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void SameAuthoritativePlace_IsReturnedNewestFirstAndMarksCurrentYear()
    {
        var hierarchy = Hierarchy(
            Place(2024, PlaceId, "registered:one", "원대리 자작나무숲", 130),
            Place(2026, PlaceId, "registered:one", "원대리 자작나무숲", 12),
            Place(2025, PlaceId, "registered:one", "원대리 자작나무숲", 188));
        var current = GalleryBrowseScope.ForHierarchy(
            2025,
            "대한민국",
            "인제",
            "registered:one",
            PlaceId);

        var siblings = GalleryPlaceYearCatalog.FindSiblings(hierarchy, current);

        Assert.Equal([2026, 2025, 2024], siblings.Select(item => item.Year));
        Assert.Equal([12, 188, 130], siblings.Select(item => item.PhotoCount));
        Assert.Single(siblings, item => item.IsCurrent && item.Year == 2025);
        Assert.All(siblings, item => Assert.Equal("registered:one", Query(item).LocationKey));
    }

    [Fact]
    public void SameDisplayName_WithDifferentIdentity_IsNotMerged()
    {
        var otherPlaceId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var hierarchy = Hierarchy(
            Place(2025, PlaceId, "registered:one", "금각사", 17),
            Place(2024, otherPlaceId, "registered:two", "금각사", 22));
        var current = GalleryBrowseScope.ForHierarchy(
            2025,
            "일본",
            "교토",
            "registered:one",
            PlaceId);

        var sibling = Assert.Single(GalleryPlaceYearCatalog.FindSiblings(hierarchy, current));

        Assert.Equal(2025, sibling.Year);
        Assert.Equal(PlaceId, ((GalleryBrowseScope.HierarchyScope)sibling.Scope).PlaceId);
    }

    [Fact]
    public void LocationKey_WinsWhenBothCandidatesProvideOne()
    {
        var hierarchy = Hierarchy(
            Place(2025, PlaceId, "registered:one", "금각사", 17),
            Place(2024, PlaceId, "registered:different", "금각사", 22));
        var current = GalleryBrowseScope.ForHierarchy(
            2025,
            "일본",
            "교토",
            "registered:one",
            PlaceId);

        var sibling = Assert.Single(GalleryPlaceYearCatalog.FindSiblings(hierarchy, current));

        Assert.Equal(2025, sibling.Year);
    }

    [Fact]
    public void PlaceId_IsCompatibilityFallbackWhenOlderPayloadHasNoLocationKey()
    {
        var hierarchy = Hierarchy(
            Place(2025, PlaceId, "registered:one", "금각사", 17),
            Place(2024, PlaceId, null, "금각사", 22));
        var current = GalleryBrowseScope.ForHierarchy(
            2025,
            "일본",
            "교토",
            "registered:one",
            PlaceId);

        var siblings = GalleryPlaceYearCatalog.FindSiblings(hierarchy, current);

        Assert.Equal([2025, 2024], siblings.Select(item => item.Year));
        Assert.Equal(PlaceId, ((GalleryBrowseScope.HierarchyScope)siblings[1].Scope).PlaceId);
    }

    [Fact]
    public void CountryAndRegionScopes_AreNotTreatedAsPlaceIdentity()
    {
        var hierarchy = Hierarchy(Place(2025, PlaceId, "registered:one", "금각사", 17));

        Assert.Empty(GalleryPlaceYearCatalog.FindSiblings(
            hierarchy,
            GalleryBrowseScope.ForHierarchy(2025, country: "일본")));
        Assert.Empty(GalleryPlaceYearCatalog.FindSiblings(
            hierarchy,
            GalleryBrowseScope.ForHierarchy(2025, country: "일본", region: "교토")));
    }

    private static FastGalleryPhotoQuery Query(GalleryPlaceYearFacet facet) =>
        GalleryBrowseScopeQueryMapper.ToQuery(facet.Scope);

    private static FastGalleryHierarchyDto Hierarchy(params FastGalleryHierarchyNodeDto[] years) =>
        new() { Items = years };

    private static FastGalleryHierarchyNodeDto Place(
        int year,
        Guid placeId,
        string? locationKey,
        string name,
        int count) => new()
    {
        Year = year,
        Count = count,
        Children =
        [
            new FastGalleryHierarchyNodeDto
            {
                Country = year == 2024 ? "Japan" : "대한민국",
                Count = count,
                Children =
                [
                    new FastGalleryHierarchyNodeDto
                    {
                        Region = year == 2024 ? "Kyoto" : "인제",
                        Count = count,
                        Children =
                        [
                            new FastGalleryHierarchyNodeDto
                            {
                                MemorykeeperPlaceId = placeId,
                                LocationKey = locationKey,
                                DisplayName = name,
                                Count = count,
                            },
                        ],
                    },
                ],
            },
        ],
    };
}
