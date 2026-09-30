using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Application;

public enum GalleryContentState
{
    Initial,
    Loading,
    SearchLoading,
    Results,
    SearchNoResults,
    GalleryEmpty,
    Error,
}

public static class GalleryContentStateResolver
{
    public static GalleryContentState Resolve(
        bool isBusy,
        bool isSearchLoading,
        bool hasSearchText,
        bool hasSelection,
        int itemCount,
        bool hasError)
    {
        if (isSearchLoading)
        {
            return GalleryContentState.SearchLoading;
        }

        if (itemCount > 0)
        {
            return GalleryContentState.Results;
        }

        if (isBusy)
        {
            return GalleryContentState.Loading;
        }

        if (hasError)
        {
            return GalleryContentState.Error;
        }

        if (hasSearchText)
        {
            return GalleryContentState.SearchNoResults;
        }

        return hasSelection ? GalleryContentState.GalleryEmpty : GalleryContentState.Initial;
    }
}

public static class GalleryInMemoryPagination
{
    public static IReadOnlyList<T> TakeNextPage<T>(
        IReadOnlyList<T> source,
        int consumedCount,
        int pageSize)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (consumedCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(consumedCount));
        }

        if (pageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        }

        return source.Skip(consumedCount).Take(pageSize).ToList();
    }

    public static bool HasMore(int totalCount, int consumedCount) =>
        consumedCount < totalCount;
}

public sealed record GalleryHierarchySearchScope(
    int? Year = null,
    string? Country = null,
    string? Region = null,
    string? LocationKey = null,
    Guid? PlaceId = null,
    bool? Unclassified = null,
    bool? DateUnclassified = null,
    bool? Favorite = null,
    string? PhotoCategory = null,
    int Count = 0)
{
    public FastGalleryPhotoQuery ToQuery(int limit, string? cursor = null) => new()
    {
        Limit = limit,
        Cursor = cursor,
        Year = Year,
        Country = Country,
        Region = Region,
        LocationKey = LocationKey,
        PlaceId = string.IsNullOrWhiteSpace(LocationKey) ? PlaceId : null,
        Unclassified = Unclassified,
        DateUnclassified = DateUnclassified,
        Favorite = Favorite,
        PhotoCategory = PhotoCategory,
    };
}

/// <summary>
/// Resolves a hierarchy label search to the same Fast Gallery scopes that produced the tree counts.
/// This keeps a region such as "교토" on the hierarchy/photo endpoint instead of treating it as a
/// filename/tag keyword on the Common Gallery endpoint.
/// </summary>
public static class GalleryHierarchySearchPlanner
{
    public static IReadOnlyList<GalleryHierarchySearchScope> Resolve(
        FastGalleryHierarchyDto hierarchy,
        string searchText)
    {
        ArgumentNullException.ThrowIfNull(hierarchy);
        var term = searchText?.Trim();
        if (string.IsNullOrWhiteSpace(term))
        {
            return [];
        }

        var scopes = new List<GalleryHierarchySearchScope>();
        foreach (var year in hierarchy.Roots)
        {
            if (Matches(term, year.Year?.ToString()))
            {
                scopes.Add(new GalleryHierarchySearchScope(Year: year.Year, Count: year.Count));
                continue;
            }

            foreach (var country in year.ChildNodes)
            {
                if (Matches(term, country.Country, country.DisplayName))
                {
                    scopes.Add(new GalleryHierarchySearchScope(
                        Year: year.Year,
                        Country: country.Country,
                        Count: country.Count));
                    continue;
                }

                var regionNodes = country.ChildNodes;
                foreach (var region in GalleryRegionHierarchyProjection.Build(regionNodes))
                {
                    if (Matches(
                            term,
                            region.CanonicalIdentity,
                            region.DisplayName,
                            region.SourceRegions.FirstOrDefault(source => Matches(term, source))))
                    {
                        foreach (var sourceRegion in region.SourceRegions)
                        {
                            var sourceCount = regionNodes
                                .Where(node => string.Equals(node.Region, sourceRegion, StringComparison.Ordinal))
                                .Sum(node => Math.Max(0, node.Count));
                            scopes.Add(new GalleryHierarchySearchScope(
                                Year: year.Year,
                                Country: country.Country,
                                Region: sourceRegion,
                                Count: sourceCount));
                        }
                        continue;
                    }

                    var matchingRegionNodes = regionNodes.Where(node =>
                        region.SourceRegions.Contains(node.Region ?? string.Empty, StringComparer.Ordinal));
                    foreach (var (sourceRegion, place) in matchingRegionNodes.SelectMany(regionNode =>
                                 regionNode.ChildNodes.Select(place => (regionNode.Region, Place: place))))
                    {
                        if (!Matches(term, place.DisplayName))
                        {
                            continue;
                        }

                        scopes.Add(new GalleryHierarchySearchScope(
                            Year: year.Year,
                            Country: country.Country,
                            Region: sourceRegion,
                            LocationKey: place.LocationKey,
                            PlaceId: place.MemorykeeperPlaceId ?? place.PlaceId,
                            Count: place.Count));
                    }
                }
            }
        }

        return Distinct(scopes);
    }

    public static IReadOnlyList<GalleryHierarchySearchScope> Intersect(
        IReadOnlyList<GalleryHierarchySearchScope> searchScopes,
        IReadOnlyList<GalleryHierarchySearchScope> filterScopes)
    {
        ArgumentNullException.ThrowIfNull(searchScopes);
        ArgumentNullException.ThrowIfNull(filterScopes);
        var result = new List<GalleryHierarchySearchScope>();
        foreach (var search in searchScopes)
        {
            foreach (var filter in filterScopes)
            {
                var intersection = TryIntersect(search, filter);
                if (intersection is not null)
                {
                    result.Add(intersection);
                }
            }
        }

        return Distinct(result);
    }

    private static GalleryHierarchySearchScope? TryIntersect(
        GalleryHierarchySearchScope search,
        GalleryHierarchySearchScope filter)
    {
        if (!Compatible(search.Year, filter.Year)
            || !CompatibleCountry(search.Country, filter.Country)
            || !Compatible(search.Region, filter.Region)
            || !Compatible(search.LocationKey, filter.LocationKey)
            || !Compatible(search.PlaceId, filter.PlaceId)
            || !Compatible(search.Unclassified, filter.Unclassified)
            || !Compatible(search.DateUnclassified, filter.DateUnclassified)
            || !Compatible(search.Favorite, filter.Favorite)
            || !Compatible(search.PhotoCategory, filter.PhotoCategory))
        {
            return null;
        }

        var count = search.Count <= 0 ? filter.Count
            : filter.Count <= 0 ? search.Count
            : Math.Min(search.Count, filter.Count);
        return new GalleryHierarchySearchScope(
            Year: filter.Year ?? search.Year,
            Country: PreferBackendCountry(search.Country, filter.Country),
            Region: filter.Region ?? search.Region,
            LocationKey: filter.LocationKey ?? search.LocationKey,
            PlaceId: filter.PlaceId ?? search.PlaceId,
            Unclassified: filter.Unclassified ?? search.Unclassified,
            DateUnclassified: filter.DateUnclassified ?? search.DateUnclassified,
            Favorite: filter.Favorite ?? search.Favorite,
            PhotoCategory: filter.PhotoCategory ?? search.PhotoCategory,
            Count: count);
    }

    private static bool Matches(string term, params string?[] candidates) =>
        candidates.Any(candidate =>
            !string.IsNullOrWhiteSpace(candidate)
            && candidate.Contains(term, StringComparison.CurrentCultureIgnoreCase));

    private static bool Compatible<T>(T? left, T? right) where T : struct =>
        !left.HasValue || !right.HasValue || EqualityComparer<T>.Default.Equals(left.Value, right.Value);

    private static bool Compatible(string? left, string? right) =>
        string.IsNullOrWhiteSpace(left)
        || string.IsNullOrWhiteSpace(right)
        || string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool CompatibleCountry(string? left, string? right) =>
        Compatible(left, right)
        || string.Equals(
            PlaceNormalizer.NormalizeCountry(left),
            PlaceNormalizer.NormalizeCountry(right),
            StringComparison.OrdinalIgnoreCase);

    private static string? PreferBackendCountry(string? searchCountry, string? filterCountry) =>
        !string.IsNullOrWhiteSpace(searchCountry) ? searchCountry : filterCountry;

    private static IReadOnlyList<GalleryHierarchySearchScope> Distinct(
        IEnumerable<GalleryHierarchySearchScope> scopes) =>
        scopes
            .GroupBy(scope => new
            {
                scope.Year,
                Country = scope.Country?.ToUpperInvariant(),
                Region = scope.Region?.ToUpperInvariant(),
                LocationKey = scope.LocationKey?.ToUpperInvariant(),
                scope.PlaceId,
                scope.Unclassified,
                scope.DateUnclassified,
                scope.Favorite,
                PhotoCategory = scope.PhotoCategory?.ToUpperInvariant(),
            })
            .Select(group => group.First() with { Count = group.Max(scope => scope.Count) })
            .ToList();
}
