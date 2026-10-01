using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class GalleryBrowseCatalogTests
{
    [Fact]
    public void YearCatalog_UsesHierarchyAggregatesWithoutPhotoScan()
    {
        var catalog = GalleryBrowseCatalog.ForYear(Hierarchy(), 2025);

        Assert.NotNull(catalog);
        Assert.Equal(63, catalog!.PhotoCount);
        Assert.Contains(catalog.Places, item => item.DisplayName == "교토" && item.PhotoCount == 39);
        Assert.Contains(catalog.Places, item => item.DisplayName == "금각사" && item.PhotoCount == 17);
        Assert.Contains(catalog.Places, item => item.DisplayName == "미분류" && item.PhotoCount == 4);
        Assert.NotNull(catalog.DateUnclassified);
        Assert.Equal(3, catalog.DateUnclassified!.PhotoCount);
    }

    [Fact]
    public void Search_ExactPlaceReturnsQueryableYearAndPlaceScope()
    {
        var result = Assert.Single(GalleryBrowseCatalog.Search(Hierarchy(), "금각사"));

        Assert.Equal(17, result.PhotoCount);
        var query = GalleryBrowseScopeQueryMapper.ToQuery(result.Scope);
        Assert.Equal(2025, query.Year);
        Assert.Equal("일본", query.Country);
        Assert.Equal("교토", query.Region);
        Assert.Equal("registered:11111111-1111-1111-1111-111111111111", query.LocationKey);
    }

    [Fact]
    public void Search_YearAndPlaceTokensStayOnSameHierarchyPath()
    {
        var results = GalleryBrowseCatalog.Search(Hierarchy(), "2025 교토");

        Assert.NotEmpty(results);
        Assert.All(results, item => Assert.Contains("2025년", item.Context, StringComparison.Ordinal));
        Assert.DoesNotContain(results, item => item.DisplayName == "부산");
    }

    [Fact]
    public void Search_BlankTextReturnsNoFacetWithoutPhotoRequest()
    {
        Assert.Empty(GalleryBrowseCatalog.Search(Hierarchy(), "  "));
    }

    [Fact]
    public void Search_SamePlaceAcrossYears_UsesNumericYearDescendingInsteadOfApiOrCountOrder()
    {
        var hierarchy = new FastGalleryHierarchyDto
        {
            Items =
            [
                PlaceYear(2023, 400),
                PlaceYear(2026, 1),
                PlaceYear(2024, 300),
                PlaceYear(2025, 2),
                PlaceYear(2025, 3),
            ],
        };

        var results = GalleryBrowseCatalog.Search(hierarchy, "원대리 자작나무숲");

        Assert.Equal([2026, 2025, 2024, 2023], results.Select(result => ScopeYear(result.Scope)));
        Assert.Equal([1, 5, 300, 400], results.Select(result => result.PhotoCount));
    }

    private static int ScopeYear(GalleryBrowseScope scope) =>
        Assert.IsType<GalleryBrowseScope.HierarchyScope>(scope).Year!.Value;

    private static FastGalleryHierarchyNodeDto PlaceYear(int year, int count) => new()
    {
        Year = year,
        Count = count,
        Children =
        [
            new FastGalleryHierarchyNodeDto
            {
                Country = "대한민국",
                Count = count,
                Children =
                [
                    new FastGalleryHierarchyNodeDto
                    {
                        Region = "인제",
                        Count = count,
                        Children =
                        [
                            new FastGalleryHierarchyNodeDto
                            {
                                DisplayName = "원대리 자작나무숲",
                                Count = count,
                                MemorykeeperPlaceId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                                LocationKey = "registered:one",
                            },
                        ],
                    },
                ],
            },
        ],
    };

    private static FastGalleryHierarchyDto Hierarchy() => new()
    {
        Items =
        [
            new FastGalleryHierarchyNodeDto
            {
                Year = 2025,
                Count = 63,
                DateUnclassifiedCount = 3,
                Children =
                [
                    new FastGalleryHierarchyNodeDto
                    {
                        Country = "일본",
                        Count = 39,
                        Children =
                        [
                            new FastGalleryHierarchyNodeDto
                            {
                                Region = "교토",
                                Count = 39,
                                Children =
                                [
                                    new FastGalleryHierarchyNodeDto
                                    {
                                        DisplayName = "금각사",
                                        Count = 17,
                                        MemorykeeperPlaceId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                                        LocationKey = "registered:11111111-1111-1111-1111-111111111111",
                                    },
                                    new FastGalleryHierarchyNodeDto
                                    {
                                        DisplayName = "기요미즈데라",
                                        Count = 22,
                                        MemorykeeperPlaceId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                                        LocationKey = "registered:22222222-2222-2222-2222-222222222222",
                                    },
                                ],
                            },
                        ],
                    },
                    new FastGalleryHierarchyNodeDto
                    {
                        Country = "대한민국",
                        Count = 20,
                        Children =
                        [
                            new FastGalleryHierarchyNodeDto { Region = "부산", Count = 20 },
                        ],
                    },
                    new FastGalleryHierarchyNodeDto
                    {
                        Count = 4,
                    },
                ],
            },
        ],
    };
}
