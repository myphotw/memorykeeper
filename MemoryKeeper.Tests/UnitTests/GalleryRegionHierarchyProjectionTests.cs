using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class GalleryRegionHierarchyProjectionTests
{
    [Fact]
    public void Build_MergesKoreanAndEnglishAliasesWithKoreanDisplayAndSummedCount()
    {
        var source = new[]
        {
            Region("오사카", 19),
            Region("Osaka", 65),
            Region("교토", 12),
            Region("Kyoto", 20),
        };

        var result = GalleryRegionHierarchyProjection.Build(source);

        var osaka = Assert.Single(result, item => item.CanonicalIdentity == "오사카");
        Assert.Equal("오사카", osaka.DisplayName);
        Assert.Equal(84, osaka.PhotoCount);
        Assert.Equal(new[] { "오사카", "Osaka" }, osaka.SourceRegions);

        var kyoto = Assert.Single(result, item => item.CanonicalIdentity == "교토");
        Assert.Equal("교토", kyoto.DisplayName);
        Assert.Equal(32, kyoto.PhotoCount);
        Assert.Equal(new[] { "교토", "Kyoto" }, kyoto.SourceRegions);

        Assert.Equal("오사카", source[0].Region);
        Assert.Equal("Osaka", source[1].Region);
    }

    [Fact]
    public void Build_UsesTrustedAliasWithoutChangingRawQueryIdentity()
    {
        var result = GalleryRegionHierarchyProjection.Build(
            [Region("Kyoto", 32), Region("Tajiri", 2)]);

        var kyoto = Assert.Single(result, item => item.CanonicalIdentity == "교토");
        Assert.Equal("교토", kyoto.DisplayName);
        Assert.Equal(32, kyoto.PhotoCount);
        Assert.Equal(new[] { "Kyoto" }, kyoto.SourceRegions);

        var tajiri = Assert.Single(result, item => item.CanonicalIdentity == "다지리");
        Assert.Equal("다지리", tajiri.DisplayName);
        Assert.Equal(2, tajiri.PhotoCount);
        Assert.Equal(new[] { "Tajiri" }, tajiri.SourceRegions);
    }

    [Fact]
    public void Build_KeepsUnknownEnglishRegionWithoutInventingTranslation()
    {
        var result = GalleryRegionHierarchyProjection.Build([Region("Unknown Harbor", 7)]);

        var region = Assert.Single(result);
        Assert.Equal("Unknown Harbor", region.CanonicalIdentity);
        Assert.Equal("Unknown Harbor", region.DisplayName);
        Assert.Equal(new[] { "Unknown Harbor" }, region.SourceRegions);
    }

    [Fact]
    public void Build_DoesNotMergeDifferentRegionsByStringSimilarity()
    {
        var result = GalleryRegionHierarchyProjection.Build(
            [Region("York", 3), Region("New York", 4), Region("Tajiri", 2)]);

        Assert.Equal(3, result.Count);
        Assert.Equal(3, Assert.Single(result, item => item.DisplayName == "York").PhotoCount);
        Assert.Equal(4, Assert.Single(result, item => item.DisplayName == "뉴욕").PhotoCount);
    }

    [Fact]
    public void Build_IsAppliedPerCountryBranchSoAliasesDoNotCrossCountries()
    {
        var japan = GalleryRegionHierarchyProjection.Build([Region("Osaka", 65)]);
        var unrelatedCountry = GalleryRegionHierarchyProjection.Build([Region("오사카", 7)]);

        Assert.Equal(65, Assert.Single(japan).PhotoCount);
        Assert.Equal(7, Assert.Single(unrelatedCountry).PhotoCount);
    }

    [Fact]
    public void Build_DoesNotTranslateOrMutateRegisteredPlaceNames()
    {
        var placeId = Guid.NewGuid();
        var registeredPlace = new FastGalleryHierarchyNodeDto
        {
            MemorykeeperPlaceId = placeId,
            LocationKey = $"registered:v1:{placeId:D}",
            DisplayName = "Sun Cruise Resort",
            Count = 2,
        };
        var region = new FastGalleryHierarchyNodeDto
        {
            Region = "Kyoto",
            Count = 2,
            Places = [registeredPlace],
        };

        var result = GalleryRegionHierarchyProjection.Build([region]);

        Assert.Equal("교토", Assert.Single(result).DisplayName);
        Assert.Equal("Sun Cruise Resort", registeredPlace.DisplayName);
        Assert.Equal($"registered:v1:{placeId:D}", registeredPlace.LocationKey);
    }

    private static FastGalleryHierarchyNodeDto Region(string name, int count) => new()
    {
        Region = name,
        Count = count,
    };
}
