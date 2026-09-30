using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class GallerySearchSupportTests
{
    [Fact]
    public void HierarchySearch_UsesRegionScopeThatProducedTreeCount()
    {
        var hierarchy = KyotoHierarchy();

        var scopes = GalleryHierarchySearchPlanner.Resolve(hierarchy, "교토");

        var scope = Assert.Single(scopes);
        Assert.Equal(2025, scope.Year);
        Assert.Equal("일본", scope.Country);
        Assert.Equal("교토", scope.Region);
        Assert.Equal(39, scope.Count);
        Assert.Null(scope.PlaceId);
        Assert.Null(scope.LocationKey);
        var query = scope.ToQuery(50, "next+/=");
        Assert.Equal(50, query.Limit);
        Assert.Equal("next+/=", query.Cursor);
        Assert.Equal("교토", query.Region);
    }

    [Fact]
    public void HierarchySearch_AndTreePlaceSelection_ProduceIntersectionScope()
    {
        var searchScopes = GalleryHierarchySearchPlanner.Resolve(KyotoHierarchy(), "교토");
        var placeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var filterScopes = new[]
        {
            new GalleryHierarchySearchScope(
                Year: 2025,
                Country: "일본",
                Region: "교토",
                LocationKey: "registered:11111111-1111-1111-1111-111111111111",
                PlaceId: placeId,
                Count: 17),
        };

        var result = GalleryHierarchySearchPlanner.Intersect(searchScopes, filterScopes);

        var scope = Assert.Single(result);
        Assert.Equal(placeId, scope.PlaceId);
        Assert.Equal("registered:11111111-1111-1111-1111-111111111111", scope.LocationKey);
        Assert.Equal(17, scope.Count);
    }

    [Fact]
    public void HierarchySearch_PreservesEveryExactRegionBehindCanonicalAlias()
    {
        var hierarchy = new FastGalleryHierarchyDto
        {
            Items =
            [
                new FastGalleryHierarchyNodeDto
                {
                    Year = 2025,
                    Children =
                    [
                        new FastGalleryHierarchyNodeDto
                        {
                            Country = "일본",
                            Children =
                            [
                                new FastGalleryHierarchyNodeDto { Region = "Osaka", Count = 20 },
                                new FastGalleryHierarchyNodeDto { Region = "오사카", Count = 19 },
                            ],
                        },
                    ],
                },
            ],
        };

        var scopes = GalleryHierarchySearchPlanner.Resolve(hierarchy, "오사카");

        Assert.Equal(2, scopes.Count);
        Assert.Equal(39, scopes.Sum(scope => scope.Count));
        Assert.Contains(scopes, scope => scope.Region == "Osaka");
        Assert.Contains(scopes, scope => scope.Region == "오사카");
    }

    [Fact]
    public void InMemorySearchPagination_TraversesAllResultsBeyondPageSize()
    {
        var source = Enumerable.Range(1, 125).ToList();

        var first = GalleryInMemoryPagination.TakeNextPage(source, 0, 50);
        var second = GalleryInMemoryPagination.TakeNextPage(source, first.Count, 50);
        var third = GalleryInMemoryPagination.TakeNextPage(source, first.Count + second.Count, 50);

        Assert.Equal(50, first.Count);
        Assert.Equal(50, second.Count);
        Assert.Equal(25, third.Count);
        Assert.True(GalleryInMemoryPagination.HasMore(source.Count, first.Count));
        Assert.False(GalleryInMemoryPagination.HasMore(source.Count, first.Count + second.Count + third.Count));
        Assert.Equal(source, first.Concat(second).Concat(third));
    }

    [Fact]
    public void ContentState_SearchLoadingNeverFallsThroughToGalleryEmpty()
    {
        var state = GalleryContentStateResolver.Resolve(
            isBusy: false,
            isSearchLoading: true,
            hasSearchText: true,
            hasSelection: true,
            itemCount: 0,
            hasError: false);

        Assert.Equal(GalleryContentState.SearchLoading, state);
    }

    [Fact]
    public void ContentState_DistinguishesSearchNoResultsFromActualEmptyGallery()
    {
        var searchEmpty = GalleryContentStateResolver.Resolve(false, false, true, true, 0, false);
        var galleryEmpty = GalleryContentStateResolver.Resolve(false, false, false, true, 0, false);

        Assert.Equal(GalleryContentState.SearchNoResults, searchEmpty);
        Assert.Equal(GalleryContentState.GalleryEmpty, galleryEmpty);
    }

    [Fact]
    public void ContentState_ClearingSearchRestoresNormalResultsState()
    {
        var state = GalleryContentStateResolver.Resolve(
            isBusy: false,
            isSearchLoading: false,
            hasSearchText: false,
            hasSelection: true,
            itemCount: 50,
            hasError: false);

        Assert.Equal(GalleryContentState.Results, state);
    }

    [Fact]
    public void ContentState_LoadMoreKeepsExistingResultsVisible()
    {
        var state = GalleryContentStateResolver.Resolve(
            isBusy: true,
            isSearchLoading: false,
            hasSearchText: false,
            hasSelection: true,
            itemCount: 50,
            hasError: false);

        Assert.Equal(GalleryContentState.Results, state);
    }

    [Fact]
    public void ContentState_ErrorDoesNotMasqueradeAsAnEmptyGallery()
    {
        var state = GalleryContentStateResolver.Resolve(false, false, false, true, 0, true);

        Assert.Equal(GalleryContentState.Error, state);
    }

    private static FastGalleryHierarchyDto KyotoHierarchy()
    {
        var placeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        return new FastGalleryHierarchyDto
        {
            Items =
            [
                new FastGalleryHierarchyNodeDto
                {
                    Year = 2025,
                    Count = 945,
                    Children =
                    [
                        new FastGalleryHierarchyNodeDto
                        {
                            Country = "일본",
                            Count = 139,
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
                                            MemorykeeperPlaceId = placeId,
                                            LocationKey = "registered:11111111-1111-1111-1111-111111111111",
                                        },
                                        new FastGalleryHierarchyNodeDto { DisplayName = "기요미즈데라", Count = 10 },
                                        new FastGalleryHierarchyNodeDto { DisplayName = "료안지", Count = 10 },
                                        new FastGalleryHierarchyNodeDto { DisplayName = "유즈야 료칸", Count = 2 },
                                    ],
                                },
                            ],
                        },
                    ],
                },
            ],
        };
    }
}
