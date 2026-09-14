using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class GalleryDailyHierarchyProjectionTests
{
    [Fact]
    public void Build_ZeroDailyCountProducesNoVirtualBranch()
    {
        var result = GalleryDailyHierarchyProjection.Build(new FastGalleryHierarchyNodeDto
        {
            Year = 2026,
            DailyCount = 0,
        });

        Assert.Null(result);
    }

    [Fact]
    public void Build_ReusesExistingDomesticCountryWithoutCreatingDuplicate()
    {
        var korea = new FastGalleryHierarchyNodeDto { Country = "South Korea", Count = 8 };
        var result = GalleryDailyHierarchyProjection.Build(new FastGalleryHierarchyNodeDto
        {
            Year = 2026,
            DailyCount = 3,
            Countries = [korea],
        });

        Assert.NotNull(result);
        Assert.Same(korea, result!.DomesticCountryNode);
        Assert.False(result.RequiresSyntheticDomesticCountry);
        Assert.Equal(3, result.PhotoCount);
    }

    [Fact]
    public void Build_WithoutDomesticCountryRequestsSingleUiContainer()
    {
        var result = GalleryDailyHierarchyProjection.Build(new FastGalleryHierarchyNodeDto
        {
            Year = 2025,
            DailyCount = 7,
            Countries = [new FastGalleryHierarchyNodeDto { Country = "일본", Count = 2 }],
        });

        Assert.NotNull(result);
        Assert.True(result!.RequiresSyntheticDomesticCountry);
        Assert.Null(result.DomesticCountryNode);
        Assert.Equal(2025, result.Year);
        Assert.Equal(7, result.PhotoCount);
    }
}
