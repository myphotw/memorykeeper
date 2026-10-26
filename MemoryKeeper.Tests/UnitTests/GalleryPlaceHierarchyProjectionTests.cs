using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class GalleryPlaceHierarchyProjectionTests
{
    [Fact]
    public void Build_UsesCanonicalMultiLevelRegionsAndExactRegisteredPhotoCount()
    {
        var placeId = Guid.NewGuid();
        var hierarchy = Hierarchy(
            Year(2025, Country("대한민국", Region("가평군", Place(placeId, "모닝 게스트하우스", 7)))));
        var paths = new[]
        {
            Path(placeId, "모닝 게스트하우스", 1, "대한민국", "가평군", ["대한민국", "경기도", "가평군"]),
        };

        var korea = Assert.Single(GalleryPlaceHierarchyProjection.Build(hierarchy, paths));
        var province = Assert.Single(korea.Regions);
        var county = Assert.Single(province.Regions);
        var place = Assert.Single(county.Places);

        Assert.Equal("경기도", province.DisplayName);
        Assert.Equal("가평군", county.DisplayName);
        Assert.Contains("가평군", province.RegionFilters);
        Assert.Equal(placeId, place.PlaceId);
        Assert.Equal(1, place.PhotoCount);
        Assert.Null(place.LocationKey);
        Assert.True(place.IsRegisteredPlace);
        Assert.Empty(korea.Places);
    }

    [Fact]
    public void Build_ExcludesConfirmedZeroPhotoPlacesWithoutTreatingMissingHierarchyAsZero()
    {
        var linkedId = Guid.NewGuid();
        var zeroId = Guid.NewGuid();
        var hierarchy = Hierarchy(
            Year(2025, Country(
                "대한민국",
                Region("가평군", Place(linkedId, "연결 장소", 1), Place(zeroId, "빈 장소", 4)))));
        var paths = new[]
        {
            Path(linkedId, "연결 장소", 1, "대한민국", "가평군", ["대한민국", "경기도", "가평군"]),
            Path(zeroId, "빈 장소", 0, "대한민국", "가평군", ["대한민국", "경기도", "가평군"]),
        };

        var projected = GalleryPlaceHierarchyProjection.Build(hierarchy, paths);
        var places = DescendantPlaces(projected);

        Assert.Contains(places, place => place.PlaceId == linkedId);
        Assert.DoesNotContain(places, place => place.PlaceId == zeroId);

        var fallbackPlaces = DescendantPlaces(GalleryPlaceHierarchyProjection.Build(hierarchy));
        Assert.Contains(fallbackPlaces, place => place.PlaceId == zeroId && place.PhotoCount == 4);
    }

    [Fact]
    public void Build_ShowsOneLeafForMultipleCanonicalPathsAndSumsEachDistinctPathOnce()
    {
        var placeId = Guid.NewGuid();
        var hierarchy = Hierarchy(
            Year(2025, Country("몰디브", Region("말레", Place(placeId, "퍼블릭 비치", 5)))));
        var male = Path(
            placeId,
            "퍼블릭 비치",
            4,
            "몰디브",
            "말레",
            ["몰디브", "말레"],
            locationKey: "registered:male",
            hierarchyComplete: true);
        var lot = Path(
            placeId,
            "퍼블릭 비치",
            1,
            "몰디브",
            "Lot",
            ["몰디브", "Lot"],
            locationKey: "registered:lot",
            hierarchyComplete: false);

        var result = GalleryPlaceHierarchyProjection.Build(hierarchy, [male, male, lot]);
        var place = Assert.Single(DescendantPlaces(result));

        Assert.Equal(placeId, place.PlaceId);
        Assert.Equal(5, place.PhotoCount);
        Assert.Equal("말레", Assert.Single(Assert.Single(result).Regions).DisplayName);
    }

    [Fact]
    public void Build_KeepsSameDisplayNameWithDifferentPlaceIdsDistinct()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var hierarchy = Hierarchy(
            Year(2025, Country("대한민국", Region(
                "가평군",
                Place(firstId, "중앙 공원", 1),
                Place(secondId, "중앙 공원", 2)))));
        var paths = new[]
        {
            Path(firstId, "중앙 공원", 1, "대한민국", "가평군", ["대한민국", "경기도", "가평군"]),
            Path(secondId, "중앙 공원", 2, "대한민국", "가평군", ["대한민국", "경기도", "가평군"]),
        };

        var places = DescendantPlaces(GalleryPlaceHierarchyProjection.Build(hierarchy, paths));

        Assert.Equal(2, places.Count);
        Assert.Equal(2, places.Select(place => place.PlaceId).Distinct().Count());
    }

    [Fact]
    public void Build_FastFallbackStillGroupsCountriesRegionsAndPlacesAcrossYears()
    {
        var seoulId = Guid.NewGuid();
        var hierarchy = Hierarchy(
            Year(2025, Country("대한민국", Region("서울", Place(seoulId, "서울숲", 3)))),
            Year(2024, Country("South Korea", Region("서울", Place(seoulId, "서울숲", 2)))));

        var korea = Assert.Single(GalleryPlaceHierarchyProjection.Build(hierarchy));
        var region = Assert.Single(korea.Regions);
        var place = Assert.Single(region.Places);

        Assert.True(korea.IsDomestic);
        Assert.Equal(5, korea.PhotoCount);
        Assert.Equal("서울", region.DisplayName);
        Assert.Equal(5, place.PhotoCount);
        Assert.Equal($"registered:v1:{seoulId:D}", place.LocationKey);
    }

    [Fact]
    public void Build_FastFallbackKeepsRawAndUnclassifiedLeavesDiscoverable()
    {
        var rawId = Guid.NewGuid();
        var hierarchy = Hierarchy(
            Year(2025, Country(
                null,
                Region("미분류 지역", RawPlace(rawId, "원본 위치", 2)))));

        var country = Assert.Single(GalleryPlaceHierarchyProjection.Build(hierarchy));
        var place = Assert.Single(Assert.Single(country.Regions).Places);

        Assert.True(country.IsUnclassified);
        Assert.Null(country.CountryFilter);
        Assert.Equal(LibraryConstants.UnclassifiedTitle, country.DisplayName);
        Assert.False(place.IsRegisteredPlace);
        Assert.Equal($"raw:v1:{rawId:D}", place.LocationKey);
    }

    private static IReadOnlyList<GalleryPlaceProjectionItem> DescendantPlaces(
        IReadOnlyList<GalleryCountryProjectionItem> countries) =>
        countries.SelectMany(country => country.Places.Concat(DescendantPlaces(country.Regions))).ToList();

    private static IEnumerable<GalleryPlaceProjectionItem> DescendantPlaces(
        IReadOnlyList<GalleryPlaceRegionProjectionItem> regions) =>
        regions.SelectMany(region => region.Places.Concat(DescendantPlaces(region.Regions)));

    private static FastGalleryHierarchyDto Hierarchy(params FastGalleryHierarchyNodeDto[] years) =>
        new() { Years = years };

    private static FastGalleryHierarchyNodeDto Year(
        int year,
        params FastGalleryHierarchyNodeDto[] countries) =>
        new() { Year = year, Countries = countries };

    private static FastGalleryHierarchyNodeDto Country(
        string? country,
        params FastGalleryHierarchyNodeDto[] regions) =>
        new()
        {
            Country = country,
            Count = regions.Sum(region => region.Count),
            Regions = regions,
        };

    private static FastGalleryHierarchyNodeDto Region(
        string region,
        params FastGalleryHierarchyNodeDto[] places) =>
        new()
        {
            Region = region,
            Count = places.Sum(place => place.Count),
            Places = places,
        };

    private static FastGalleryHierarchyNodeDto Place(Guid id, string name, int count) =>
        new()
        {
            MemorykeeperPlaceId = id,
            LocationKey = $"registered:v1:{id:D}",
            DisplayName = name,
            Count = count,
        };

    private static FastGalleryHierarchyNodeDto RawPlace(Guid id, string name, int count) =>
        new()
        {
            PlaceId = id,
            LocationKey = $"raw:v1:{id:D}",
            DisplayName = name,
            Count = count,
        };

    private static MemoryKeeperPlaceHierarchyPathApiDto Path(
        Guid id,
        string name,
        int count,
        string country,
        string region,
        string[] locationPath,
        string? locationKey = null,
        bool hierarchyComplete = true) => new()
    {
        MemorykeeperPlaceId = id,
        LocationKey = locationKey ?? $"registered:v1:{id:D}:{region}",
        DisplayName = name,
        Country = country,
        Region = region,
        LocationPath = locationPath,
        FullPath = $"{string.Join(" > ", locationPath)} > {name}",
        PhotoCount = count,
        LocationHierarchyComplete = hierarchyComplete,
    };
}
