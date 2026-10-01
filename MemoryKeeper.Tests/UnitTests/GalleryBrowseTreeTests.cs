using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class GalleryBrowseTreeTests
{
    [Fact]
    public void YearTree_PreservesCountryRegionAndAuthoritativePlaceScopes()
    {
        var placeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var tree = GalleryBrowseTree.ForYear(Hierarchy(placeId), 2025);

        Assert.NotNull(tree);
        Assert.Equal(945, tree!.PhotoCount);
        var country = Assert.Single(tree.Roots, node => node.Kind == GalleryBrowseTreeNodeKind.Country);
        var region = Assert.Single(country.Children);
        var place = Assert.Single(region.Children);

        Assert.Equal("일본", country.DisplayName);
        Assert.Equal(53, country.PhotoCount);
        Assert.Equal(2025, GalleryBrowseScopeQueryMapper.ToQuery(country.Scope).Year);
        Assert.Equal("Japan", GalleryBrowseScopeQueryMapper.ToQuery(country.Scope).Country);

        Assert.Equal("교토", region.DisplayName);
        Assert.Equal(39, region.PhotoCount);
        Assert.Equal(
            ["Kyoto"],
            GalleryBrowseScopeQueryMapper.ToQueries(region.Scope).Select(query => query.Region));

        Assert.Equal("교토 타워", place.DisplayName);
        Assert.Equal(12, place.PhotoCount);
        Assert.Empty(place.Children);
        var placeQuery = GalleryBrowseScopeQueryMapper.ToQuery(place.Scope);
        Assert.Equal("registered:11111111-1111-1111-1111-111111111111", placeQuery.LocationKey);
        Assert.Null(placeQuery.PlaceId);
    }

    [Fact]
    public void YearTree_KeepsUnclassifiedScopesOutsideCountryHierarchy()
    {
        var tree = GalleryBrowseTree.ForYear(
            Hierarchy(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            2025)!;

        var placeUnclassified = Assert.Single(
            tree.Roots,
            node => node.Kind == GalleryBrowseTreeNodeKind.Unclassified);
        var dateUnclassified = Assert.Single(
            tree.Roots,
            node => node.Kind == GalleryBrowseTreeNodeKind.DateUnclassified);

        Assert.Empty(placeUnclassified.Children);
        Assert.Empty(dateUnclassified.Children);
        Assert.True(GalleryBrowseScopeQueryMapper.ToQuery(placeUnclassified.Scope).Unclassified is true);
        Assert.Equal(2025, GalleryBrowseScopeQueryMapper.ToQuery(placeUnclassified.Scope).Year);
        Assert.True(GalleryBrowseScopeQueryMapper.ToQuery(dateUnclassified.Scope).DateUnclassified is true);
    }

    [Fact]
    public void Search_KeepsSamePlaceNameSeparatedByYearAndParentPath()
    {
        var hierarchy = Hierarchy(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var older = Hierarchy(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            year: 2024,
            yearCount: 52,
            countryCount: 52,
            regionCount: 52).Roots[0];
        hierarchy = new FastGalleryHierarchyDto { Items = [hierarchy.Roots[0], older] };

        var results = GalleryBrowseCatalog.Search(hierarchy, "교토 타워");

        Assert.Equal(2, results.Count);
        Assert.Contains(results, result => result.Context.Contains("2025년", StringComparison.Ordinal));
        Assert.Contains(results, result => result.Context.Contains("2024년", StringComparison.Ordinal));
        Assert.NotEqual(results[0].Scope, results[1].Scope);
    }

    [Fact]
    public void SameNamedPlaces_RemainDistinctUnderDifferentParents()
    {
        var first = Hierarchy(Guid.Parse("11111111-1111-1111-1111-111111111111")).Roots[0];
        var secondPlaceId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var hierarchy = new FastGalleryHierarchyDto
        {
            Items =
            [
                new FastGalleryHierarchyNodeDto
                {
                    Year = 2025,
                    Count = 957,
                    Children =
                    [
                        first.ChildNodes[0],
                        new FastGalleryHierarchyNodeDto
                        {
                            Country = "대한민국",
                            Count = 12,
                            Children =
                            [
                                new FastGalleryHierarchyNodeDto
                                {
                                    Region = "강릉",
                                    Count = 12,
                                    Children =
                                    [
                                        new FastGalleryHierarchyNodeDto
                                        {
                                            DisplayName = "교토 타워",
                                            Count = 12,
                                            MemorykeeperPlaceId = secondPlaceId,
                                            LocationKey = $"registered:{secondPlaceId:D}",
                                        },
                                    ],
                                },
                            ],
                        },
                    ],
                },
            ],
        };

        var tree = GalleryBrowseTree.ForYear(hierarchy, 2025)!;
        var places = tree.Roots
            .SelectMany(country => country.Children)
            .SelectMany(region => region.Children)
            .Where(place => place.DisplayName == "교토 타워")
            .ToArray();

        Assert.Equal(2, places.Length);
        Assert.Equal(2, places.Select(place => place.Key).Distinct().Count());
        Assert.Equal(2, places.Select(place => place.Context).Distinct().Count());
    }

    [Fact]
    public void FindPath_ReturnsExpandedAncestorsAndTheCurrentLeaf()
    {
        var tree = GalleryBrowseTree.ForYear(
            Hierarchy(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            2025)!;
        var place = tree.Roots
            .Single(node => node.Kind == GalleryBrowseTreeNodeKind.Country)
            .Children.Single()
            .Children.Single();

        var path = GalleryBrowseTree.FindPath(tree, place.Scope);

        Assert.Equal(
            [
                GalleryBrowseTreeNodeKind.Country,
                GalleryBrowseTreeNodeKind.Region,
                GalleryBrowseTreeNodeKind.Place,
            ],
            path.Select(node => node.Kind));
        Assert.Equal(place.Key, path[^1].Key);
    }

    [Fact]
    public void Focus_ExpandsOnlyCurrentAncestorsAndHighlightsCurrentNode()
    {
        var currentPlaceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var unrelatedPlaceId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var hierarchy = new FastGalleryHierarchyDto
        {
            Items =
            [
                new FastGalleryHierarchyNodeDto
                {
                    Year = 2025,
                    Count = 22,
                    Children =
                    [
                        new FastGalleryHierarchyNodeDto
                        {
                            Country = "대한민국",
                            Count = 10,
                            Children =
                            [
                                Region("인제", 10, Place(currentPlaceId, "원대리 자작나무숲", 10)),
                            ],
                        },
                        new FastGalleryHierarchyNodeDto
                        {
                            Country = "Japan",
                            Count = 12,
                            Children =
                            [
                                Region("Kyoto", 12, Place(unrelatedPlaceId, "금각사", 12)),
                            ],
                        },
                    ],
                },
            ],
        };
        var tree = GalleryBrowseTree.ForYear(hierarchy, 2025)!;
        var current = tree.Roots
            .Single(node => node.DisplayName == "대한민국")
            .Children.Single()
            .Children.Single();
        var unrelated = tree.Roots.Single(node => node.DisplayName == "일본");

        var focus = GalleryBrowseTree.CreateFocus(tree, current.Scope);
        var currentCountry = tree.Roots.Single(node => node.DisplayName == "대한민국");
        var currentRegion = currentCountry.Children.Single();

        Assert.Equal(current.Key, focus.CurrentNodeKey);
        Assert.Equal(2, focus.ExpandedNodeKeys.Count);
        Assert.Contains(currentCountry.Key, focus.ExpandedNodeKeys);
        Assert.Contains(currentRegion.Key, focus.ExpandedNodeKeys);
        Assert.Contains(current.Key, GalleryBrowseTree.FindPath(tree, current.Scope).Select(node => node.Key));
        Assert.DoesNotContain(unrelated.Key, focus.ExpandedNodeKeys);
        Assert.DoesNotContain(unrelated.Children.Single().Key, focus.ExpandedNodeKeys);
    }

    [Fact]
    public void Focus_MatchesCanonicalRegionFromLegacyHierarchyScope()
    {
        var hierarchy = new FastGalleryHierarchyDto
        {
            Items =
            [
                new FastGalleryHierarchyNodeDto
                {
                    Year = 2025,
                    Count = 51,
                    Children =
                    [
                        new FastGalleryHierarchyNodeDto
                        {
                            Country = "Japan",
                            Count = 39,
                            Children =
                            [
                                Region(
                                    "Kyoto",
                                    39,
                                    Place(Guid.NewGuid(), "금각사", 39)),
                            ],
                        },
                        new FastGalleryHierarchyNodeDto
                        {
                            Country = "대한민국",
                            Count = 12,
                            Children =
                            [
                                Region(
                                    "강릉",
                                    12,
                                    Place(Guid.NewGuid(), "경포대", 12)),
                            ],
                        },
                    ],
                },
            ],
        };
        var tree = GalleryBrowseTree.ForYear(hierarchy, 2025)!;
        var japan = tree.Roots.Single(node => node.DisplayName == "일본");
        var kyoto = japan.Children.Single();
        var unrelated = tree.Roots.Single(node => node.DisplayName == "대한민국");
        var legacyRegionScope = GalleryBrowseScope.ForHierarchy(
            2025,
            "Japan",
            region: "교토");

        var focus = GalleryBrowseTree.CreateFocus(tree, legacyRegionScope);

        Assert.Equal(kyoto.Key, focus.CurrentNodeKey);
        Assert.Equal(2, focus.ExpandedNodeKeys.Count);
        Assert.Contains(japan.Key, focus.ExpandedNodeKeys);
        Assert.Contains(kyoto.Key, focus.ExpandedNodeKeys);
        Assert.DoesNotContain(unrelated.Key, focus.ExpandedNodeKeys);
    }

    [Fact]
    public void YearTree_UsesTheSameCanonicalRegionProjectionAsPcGallery()
    {
        var osakaCastleId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var hotelId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var hierarchy = new FastGalleryHierarchyDto
        {
            Items =
            [
                new FastGalleryHierarchyNodeDto
                {
                    Year = 2025,
                    Count = 98,
                    Children =
                    [
                        new FastGalleryHierarchyNodeDto
                        {
                            Country = "Japan",
                            Count = 98,
                            Children =
                            [
                                Region("오사카", 23, Place(osakaCastleId, "오사카 성", 23)),
                                Region("Osaka", 75, Place(hotelId, "홀리데이 인 오사카 난바", 75)),
                            ],
                        },
                    ],
                },
            ],
        };

        var tree = GalleryBrowseTree.ForYear(hierarchy, 2025)!;
        var osaka = Assert.Single(Assert.Single(tree.Roots).Children);

        Assert.Equal("오사카", osaka.DisplayName);
        Assert.Equal(98, osaka.PhotoCount);
        Assert.Equal(98, osaka.Children.Sum(child => child.PhotoCount));
        Assert.Equal(2, osaka.Children.Count);
        Assert.Equal(
            ["Osaka", "오사카"],
            GalleryBrowseScopeQueryMapper.ToQueries(osaka.Scope)
                .Select(query => query.Region)
                .OrderBy(region => region, StringComparer.Ordinal));
    }

    [Fact]
    public void CanonicalRegionProjection_DoesNotMergeSameDisplayAcrossCountries()
    {
        var hierarchy = new FastGalleryHierarchyDto
        {
            Items =
            [
                new FastGalleryHierarchyNodeDto
                {
                    Year = 2025,
                    Count = 7,
                    Children =
                    [
                        new FastGalleryHierarchyNodeDto
                        {
                            Country = "Japan",
                            Count = 3,
                            Children = [Region("Osaka", 3, Place(Guid.NewGuid(), "첫 장소", 3))],
                        },
                        new FastGalleryHierarchyNodeDto
                        {
                            Country = "Other",
                            Count = 4,
                            Children = [Region("오사카", 4, Place(Guid.NewGuid(), "둘째 장소", 4))],
                        },
                    ],
                },
            ],
        };

        var tree = GalleryBrowseTree.ForYear(hierarchy, 2025)!;

        Assert.Equal(2, tree.Roots.Count);
        Assert.All(tree.Roots, country => Assert.Single(country.Children));
        Assert.Equal(2, tree.Roots.SelectMany(country => country.Children).Count());
    }

    private static FastGalleryHierarchyNodeDto Region(
        string name,
        int count,
        params FastGalleryHierarchyNodeDto[] places) => new()
    {
        Region = name,
        Count = count,
        Children = places,
    };

    private static FastGalleryHierarchyNodeDto Place(Guid id, string name, int count) => new()
    {
        MemorykeeperPlaceId = id,
        LocationKey = $"registered:{id:D}",
        DisplayName = name,
        Count = count,
    };

    private static FastGalleryHierarchyDto Hierarchy(
        Guid placeId,
        int year = 2025,
        int yearCount = 945,
        int countryCount = 53,
        int regionCount = 39) => new()
    {
        Items =
        [
            new FastGalleryHierarchyNodeDto
            {
                Year = year,
                Count = yearCount,
                DateUnclassifiedCount = 7,
                Children =
                [
                    new FastGalleryHierarchyNodeDto
                    {
                        Country = "Japan",
                        Count = countryCount,
                        Children =
                        [
                            new FastGalleryHierarchyNodeDto
                            {
                                Region = "Kyoto",
                                Count = regionCount,
                                Children =
                                [
                                    new FastGalleryHierarchyNodeDto
                                    {
                                        DisplayName = "교토 타워",
                                        Count = 12,
                                        MemorykeeperPlaceId = placeId,
                                        LocationKey = $"registered:{placeId:D}",
                                    },
                                ],
                            },
                        ],
                    },
                    new FastGalleryHierarchyNodeDto
                    {
                        Count = 18,
                    },
                ],
            },
        ],
    };
}
