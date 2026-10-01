using MemoryKeeper.Application;
using MemoryKeeper.Mobile.Models;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class MobileGalleryContextTests
{
    [Fact]
    public void RecentContext_UsesDefaultFeedWithoutInventingAParentCount()
    {
        var context = MobileGalleryContext.Recent;

        Assert.Equal(MobileGalleryContextKind.Recent, context.Kind);
        Assert.Same(GalleryBrowseScope.DefaultFeed, context.Scope);
        Assert.Equal("MemoryKeeper", context.Title);
        Assert.Equal("최근 사진", context.Summary);
        Assert.Null(context.PhotoCount);
    }

    [Fact]
    public void YearContext_UsesTheSelectedYearScopeAndItsOwnCount()
    {
        var option = new MobileGalleryBrowseOption(
            "2025년",
            GalleryBrowseScope.ForYear(2025),
            945);

        var context = MobileGalleryContext.FromOption(option);
        var query = GalleryBrowseScopeQueryMapper.ToQuery(context.Scope);

        Assert.Equal(MobileGalleryContextKind.Year, context.Kind);
        Assert.Equal("2025년", context.Title);
        Assert.Equal("전체 사진 · 945장", context.Summary);
        Assert.Equal(2025, query.Year);
    }

    [Fact]
    public void PlaceContext_UsesTheChildScopeTitleContextAndCount()
    {
        var scope = GalleryBrowseScope.ForHierarchy(
            year: 2025,
            country: "일본",
            region: "교토");
        var option = new MobileGalleryBrowseOption(
            "교토",
            scope,
            39,
            "2025년 · 일본");

        var context = MobileGalleryContext.FromOption(option);
        var query = GalleryBrowseScopeQueryMapper.ToQuery(context.Scope);

        Assert.Equal(MobileGalleryContextKind.Place, context.Kind);
        Assert.Equal("교토", context.Title);
        Assert.Equal("2025년 · 일본 · 39장", context.Summary);
        Assert.Equal(39, context.PhotoCount);
        Assert.Equal(2025, query.Year);
        Assert.Equal("일본", query.Country);
        Assert.Equal("교토", query.Region);
        Assert.DoesNotContain("945", context.Summary, StringComparison.Ordinal);
        Assert.False(context.IsPlaceLeaf);
    }

    [Fact]
    public void RegisteredPlaceContext_IsPlaceLeafButRegionAndCountryAreNot()
    {
        var placeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var place = MobileGalleryContext.FromOption(new MobileGalleryBrowseOption(
            "금각사",
            GalleryBrowseScope.ForHierarchy(
                2025,
                "Japan",
                "Kyoto",
                "registered:one",
                placeId),
            17,
            "2025년 · 일본 · 교토"));
        var country = MobileGalleryContext.FromOption(new MobileGalleryBrowseOption(
            "일본",
            GalleryBrowseScope.ForHierarchy(2025, country: "Japan"),
            53,
            "2025년"));

        Assert.True(place.IsPlaceLeaf);
        Assert.True(place.HasHierarchyYear);
        Assert.Equal("2025년 · 일본 · 교토", place.BreadcrumbText);
        Assert.Equal("일본 · 교토", place.HierarchyBreadcrumbText);
        Assert.Equal("2025년", place.YearText);
        Assert.Equal("2025년 ⌄", place.YearSelectorText);
        Assert.Equal("17장", place.PhotoCountText);
        Assert.False(country.IsPlaceLeaf);
        Assert.True(country.HasHierarchyYear);
    }

    [Fact]
    public void CanonicalRegionContext_SupportsHierarchyNavigationButNotPlaceYearSelection()
    {
        var context = MobileGalleryContext.FromOption(new MobileGalleryBrowseOption(
            "오사카",
            GalleryBrowseScope.ForCanonicalRegion(
                2025,
                "Japan",
                "오사카",
                ["Osaka", "오사카"]),
            98,
            "2025년 · 일본"));

        Assert.True(context.HasHierarchyYear);
        Assert.False(context.IsPlaceLeaf);
        Assert.Equal("2025년", context.YearText);
        Assert.Equal("일본", context.HierarchyBreadcrumbText);
        Assert.Equal("98장", context.PhotoCountText);
    }

    [Fact]
    public void SearchAndHierarchyOptions_CreateTheSameCurrentContext()
    {
        var placeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var scope = GalleryBrowseScope.ForHierarchy(
            2025,
            "Japan",
            "Kyoto",
            "registered:one",
            placeId);
        var searchOption = new MobileGalleryBrowseOption(
            "금각사",
            scope,
            17,
            "2025년 · 일본 · 교토");
        var hierarchyOption = new MobileGalleryBrowseOption(
            "금각사",
            scope,
            17,
            "2025년 · 일본 · 교토");

        Assert.Equal(
            MobileGalleryContext.FromOption(searchOption),
            MobileGalleryContext.FromOption(hierarchyOption));
    }

    [Fact]
    public void UnclassifiedContexts_KeepPlaceAndDateQueriesDistinct()
    {
        var place = MobileGalleryContext.FromOption(new MobileGalleryBrowseOption(
            "장소 미분류",
            GalleryBrowseScope.ForUnclassified(),
            1018));
        var date = MobileGalleryContext.FromOption(new MobileGalleryBrowseOption(
            "날짜 미분류",
            GalleryBrowseScope.ForDateUnclassified(2025),
            7,
            "2025년"));

        var placeQuery = GalleryBrowseScopeQueryMapper.ToQuery(place.Scope);
        var dateQuery = GalleryBrowseScopeQueryMapper.ToQuery(date.Scope);

        Assert.Equal(MobileGalleryContextKind.Unclassified, place.Kind);
        Assert.True(placeQuery.Unclassified is true);
        Assert.Null(placeQuery.DateUnclassified);
        Assert.Equal(MobileGalleryContextKind.DateUnclassified, date.Kind);
        Assert.Equal("2025년 · 7장", date.Summary);
        Assert.True(dateQuery.DateUnclassified is true);
        Assert.Null(dateQuery.Unclassified);
    }

    [Fact]
    public void NavigationHistory_ReturnsFromPlaceToYearThenRecent()
    {
        var history = new MobileGalleryNavigationHistory();
        var recent = MobileGalleryContext.Recent;
        var year = MobileGalleryContext.FromOption(new MobileGalleryBrowseOption(
            "2025년",
            GalleryBrowseScope.ForYear(2025),
            945));
        var place = MobileGalleryContext.FromOption(new MobileGalleryBrowseOption(
            "교토",
            GalleryBrowseScope.ForHierarchy(year: 2025, country: "일본", region: "교토"),
            39,
            "2025년 · 일본"));

        history.Remember(recent, MobileGalleryViewMode.Gallery, year);
        history.Remember(year, MobileGalleryViewMode.PlaceSelection, place);

        Assert.True(history.TryPop(out var yearDestination));
        Assert.Equal(year, yearDestination.Context);
        Assert.Equal(MobileGalleryViewMode.PlaceSelection, yearDestination.ReturnMode);
        Assert.True(history.TryPop(out var recentDestination));
        Assert.Equal(recent, recentDestination.Context);
        Assert.Equal(MobileGalleryViewMode.Gallery, recentDestination.ReturnMode);
        Assert.False(history.TryPop(out _));
    }

    [Fact]
    public void SiblingYearNavigation_ReturnsToPriorGalleryInsteadOfSearchMode()
    {
        var history = new MobileGalleryNavigationHistory();
        var placeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var year2025 = MobileGalleryContext.FromOption(new MobileGalleryBrowseOption(
            "원대리 자작나무숲",
            GalleryBrowseScope.ForHierarchy(2025, "대한민국", "인제", "registered:one", placeId),
            188,
            "2025년 · 대한민국 · 인제"));
        var year2024 = MobileGalleryContext.FromOption(new MobileGalleryBrowseOption(
            "원대리 자작나무숲",
            GalleryBrowseScope.ForHierarchy(2024, "대한민국", "인제", "registered:one", placeId),
            130,
            "2024년 · 대한민국 · 인제"));

        history.Remember(year2025, MobileGalleryViewMode.Gallery, year2024);

        Assert.True(history.TryPop(out var entry));
        Assert.Equal(year2025, entry.Context);
        Assert.Equal(MobileGalleryViewMode.Gallery, entry.ReturnMode);
    }

    [Fact]
    public void SiblingYearOption_KeepsPlaceTitleAndUsesSelectedYearContext()
    {
        var placeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var option = new MobileGalleryBrowseOption(
            "2024년",
            GalleryBrowseScope.ForHierarchy(
                2024,
                "대한민국",
                "인제",
                "registered:one",
                placeId),
            130,
            "2024년 · 대한민국 · 인제",
            IsCurrent: false,
            GalleryTitle: "원대리 자작나무숲");

        var context = MobileGalleryContext.FromOption(option);

        Assert.True(context.IsPlaceLeaf);
        Assert.Equal(MobileGalleryContextKind.Place, context.Kind);
        Assert.Equal("원대리 자작나무숲", context.Title);
        Assert.Equal(2024, context.Year);
        Assert.Equal("대한민국 · 인제", context.HierarchyBreadcrumbText);
        Assert.Equal("130장", context.PhotoCountText);
        var scope = Assert.IsType<GalleryBrowseScope.HierarchyScope>(context.Scope);
        Assert.Equal("registered:one", scope.LocationKey);
    }
}
