using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class GalleryPlaceHierarchyProjectionTests
{
    [Fact]
    public void Build_GroupsCountriesAndPlacesAcrossYearsAndSumsPhotoCounts()
    {
        var seoulId = Guid.NewGuid();
        var osakaId = Guid.NewGuid();
        var hierarchy = Hierarchy(
            Year(2025,
                Country("대한민국", 4, Place(seoulId, "서울", 3)),
                Country("일본", 2, Place(osakaId, "오사카", 2))),
            Year(2024,
                Country("South Korea", 2, Place(seoulId, "서울", 2))));

        var result = GalleryPlaceHierarchyProjection.Build(hierarchy);

        var korea = Assert.Single(result, item => item.DisplayName == "대한민국");
        Assert.True(korea.IsDomestic);
        Assert.Equal(6, korea.PhotoCount);
        Assert.Equal(5, Assert.Single(korea.Places).PhotoCount);

        var japan = Assert.Single(result, item => item.DisplayName == "일본");
        Assert.False(japan.IsDomestic);
        Assert.Equal(2, japan.PhotoCount);
        Assert.Equal(osakaId, Assert.Single(japan.Places).PlaceId);
    }

    [Fact]
    public void Build_UsesExistingUnclassifiedLabelAndKeepsSamePlaceNameInDifferentCountries()
    {
        var hierarchy = Hierarchy(
            Year(2025,
                Country(null, 3, Place(Guid.NewGuid(), "중앙 공원", 2)),
                Country("일본", 1, Place(Guid.NewGuid(), "중앙 공원", 1)),
                Country("미국", 4, Place(Guid.NewGuid(), "중앙 공원", 4))));

        var result = GalleryPlaceHierarchyProjection.Build(hierarchy);

        var unclassified = Assert.Single(result, item => item.IsUnclassified);
        Assert.Equal(LibraryConstants.UnclassifiedTitle, unclassified.DisplayName);
        Assert.Null(unclassified.CountryFilter);
        Assert.Equal(3, unclassified.PhotoCount);
        Assert.Equal(3, result.Sum(country => country.Places.Count(place => place.DisplayName == "중앙 공원")));
    }

    private static FastGalleryHierarchyDto Hierarchy(params FastGalleryHierarchyNodeDto[] years) =>
        new() { Years = years };

    private static FastGalleryHierarchyNodeDto Year(
        int year,
        params FastGalleryHierarchyNodeDto[] countries) =>
        new() { Year = year, Countries = countries };

    private static FastGalleryHierarchyNodeDto Country(
        string? country,
        int count,
        params FastGalleryHierarchyNodeDto[] places) =>
        new()
        {
            Country = country,
            Count = count,
            Regions = [new FastGalleryHierarchyNodeDto { Region = "region", Places = places }],
        };

    private static FastGalleryHierarchyNodeDto Place(Guid id, string name, int count) =>
        new()
        {
            MemorykeeperPlaceId = id,
            DisplayName = name,
            Count = count,
        };
}
