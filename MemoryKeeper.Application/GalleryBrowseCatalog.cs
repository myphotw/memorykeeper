using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Application;

public sealed record GalleryBrowseFacet(
    string DisplayName,
    string Context,
    int PhotoCount,
    GalleryBrowseScope Scope,
    int Depth);

public sealed record GalleryYearBrowseCatalog(
    int Year,
    int PhotoCount,
    IReadOnlyList<GalleryBrowseFacet> Places,
    GalleryBrowseFacet? DateUnclassified);

/// <summary>
/// Projects Fast Gallery hierarchy aggregates into lightweight browse/search facets.
/// It never scans photo pages or issues per-place requests.
/// </summary>
public static class GalleryBrowseCatalog
{
    public static GalleryYearBrowseCatalog? ForYear(FastGalleryHierarchyDto hierarchy, int year)
    {
        ArgumentNullException.ThrowIfNull(hierarchy);
        var yearNode = hierarchy.Roots.FirstOrDefault(node => node.Year == year);
        if (yearNode is null)
        {
            return null;
        }

        var places = BuildHierarchyFacets(yearNode)
            .Where(facet => facet.Depth >= 2)
            .GroupBy(facet => facet.Scope)
            .Select(group => group.First() with { PhotoCount = group.Sum(item => item.PhotoCount) })
            .OrderBy(facet => facet.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(facet => facet.Context, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        var dateUnclassified = yearNode.DateUnclassifiedCount > 0
            ? new GalleryBrowseFacet(
                "날짜 미분류",
                $"{year}년",
                yearNode.DateUnclassifiedCount,
                GalleryBrowseScope.ForDateUnclassified(year),
                Depth: 1)
            : null;
        return new GalleryYearBrowseCatalog(year, yearNode.Count, places, dateUnclassified);
    }

    public static IReadOnlyList<GalleryBrowseFacet> Search(
        FastGalleryHierarchyDto hierarchy,
        string? searchText,
        int maxResults = 50)
    {
        ArgumentNullException.ThrowIfNull(hierarchy);
        if (maxResults <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxResults));
        }

        var tokens = (searchText ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return [];
        }

        if (tokens.Length == 1
            && int.TryParse(tokens[0], out var requestedYear)
            && hierarchy.Roots.FirstOrDefault(node => node.Year == requestedYear) is { } yearNode)
        {
            return
            [
                new GalleryBrowseFacet(
                    $"{requestedYear}년 전체",
                    "연도",
                    yearNode.Count,
                    GalleryBrowseScope.ForYear(requestedYear),
                    Depth: 0),
            ];
        }

        var candidates = hierarchy.Roots
            .Where(node => node.Year.HasValue)
            .SelectMany(BuildHierarchyFacets)
            .Where(candidate => MatchesAll(candidate, tokens))
            .ToList();
        var exact = candidates
            .Where(candidate => tokens.Length == 1
                                && string.Equals(
                                    candidate.DisplayName,
                                    tokens[0],
                                    StringComparison.CurrentCultureIgnoreCase))
            .ToList();
        var selected = exact.Count > 0 ? exact : candidates;
        return selected
            .GroupBy(candidate => candidate.Scope)
            .Select(group => group.First() with { PhotoCount = group.Sum(item => item.PhotoCount) })
            .OrderBy(candidate => candidate.Depth)
            .ThenByDescending(candidate => candidate.PhotoCount)
            .ThenBy(candidate => candidate.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Take(maxResults)
            .ToArray();
    }

    public static int CountUnclassified(FastGalleryHierarchyDto hierarchy)
    {
        ArgumentNullException.ThrowIfNull(hierarchy);
        return hierarchy.Roots
            .Where(node => node.Year.HasValue)
            .SelectMany(node => node.ChildNodes)
            .Where(IsUnclassifiedCountry)
            .Sum(node => Math.Max(0, node.Count));
    }

    private static IEnumerable<GalleryBrowseFacet> BuildHierarchyFacets(
        FastGalleryHierarchyNodeDto yearNode)
    {
        if (yearNode.Year is not int year)
        {
            yield break;
        }

        yield return new GalleryBrowseFacet(
            $"{year}년 전체",
            "연도",
            yearNode.Count,
            GalleryBrowseScope.ForYear(year),
            Depth: 0);

        foreach (var countryNode in yearNode.ChildNodes)
        {
            if (IsUnclassifiedCountry(countryNode))
            {
                yield return new GalleryBrowseFacet(
                    LibraryConstants.UnclassifiedTitle,
                    $"{year}년 · 장소",
                    countryNode.Count,
                    new GalleryBrowseScope.HierarchyScope(year: year, unclassified: true),
                    Depth: 2);
                continue;
            }

            var country = countryNode.Country!.Trim();
            yield return new GalleryBrowseFacet(
                country,
                $"{year}년 · 국가",
                countryNode.Count,
                GalleryBrowseScope.ForHierarchy(year: year, country: country),
                Depth: countryNode.ChildNodes.Count == 0 ? 2 : 1);

            foreach (var regionNode in countryNode.ChildNodes)
            {
                var region = string.IsNullOrWhiteSpace(regionNode.Region)
                    ? null
                    : regionNode.Region.Trim();
                if (region is not null)
                {
                    yield return new GalleryBrowseFacet(
                        PlaceNormalizer.NormalizeRegion(region),
                        $"{year}년 · {country}",
                        regionNode.Count,
                        GalleryBrowseScope.ForHierarchy(year: year, country: country, region: region),
                        Depth: 2);
                }

                foreach (var placeNode in regionNode.ChildNodes)
                {
                    var placeId = placeNode.MemorykeeperPlaceId ?? placeNode.PlaceId;
                    var locationKey = string.IsNullOrWhiteSpace(placeNode.LocationKey)
                        ? null
                        : placeNode.LocationKey.Trim();
                    if (placeId is null && locationKey is null)
                    {
                        continue;
                    }

                    var placeName = string.IsNullOrWhiteSpace(placeNode.DisplayName)
                        ? region ?? LibraryConstants.UnclassifiedTitle
                        : placeNode.DisplayName.Trim();
                    yield return new GalleryBrowseFacet(
                        placeName,
                        BuildContext(year, country, region),
                        placeNode.Count,
                        GalleryBrowseScope.ForHierarchy(
                            year,
                            country,
                            region,
                            locationKey,
                            placeId),
                        Depth: 3);
                }
            }
        }

        if (yearNode.DateUnclassifiedCount > 0)
        {
            yield return new GalleryBrowseFacet(
                "날짜 미분류",
                $"{year}년",
                yearNode.DateUnclassifiedCount,
                GalleryBrowseScope.ForDateUnclassified(year),
                Depth: 1);
        }
    }

    private static bool MatchesAll(GalleryBrowseFacet candidate, IReadOnlyList<string> tokens)
    {
        var searchable = $"{candidate.DisplayName} {candidate.Context}";
        return tokens.All(token => searchable.Contains(token, StringComparison.CurrentCultureIgnoreCase));
    }

    private static bool IsUnclassifiedCountry(FastGalleryHierarchyNodeDto node)
    {
        var country = PlaceNormalizer.NormalizeCountry(node.Country);
        return string.IsNullOrWhiteSpace(country)
               || string.Equals(country, LibraryConstants.UnclassifiedTitle, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildContext(int year, string country, string? region) =>
        string.IsNullOrWhiteSpace(region)
            ? $"{year}년 · {country}"
            : $"{year}년 · {country} · {PlaceNormalizer.NormalizeRegion(region)}";
}
