using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Application;

public sealed record GalleryPlaceIdentity(string? LocationKey, Guid? PlaceId)
{
    public bool Matches(GalleryPlaceIdentity other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (!string.IsNullOrWhiteSpace(LocationKey)
            && !string.IsNullOrWhiteSpace(other.LocationKey))
        {
            return string.Equals(LocationKey, other.LocationKey, StringComparison.Ordinal);
        }

        return PlaceId is Guid placeId
               && other.PlaceId is Guid otherPlaceId
               && placeId == otherPlaceId;
    }

    public static GalleryPlaceIdentity? FromScope(GalleryBrowseScope scope) => scope switch
    {
        GalleryBrowseScope.HierarchyScope hierarchy
            when !string.IsNullOrWhiteSpace(hierarchy.LocationKey) || hierarchy.PlaceId.HasValue =>
            new GalleryPlaceIdentity(hierarchy.LocationKey, hierarchy.PlaceId),
        _ => null,
    };
}

public sealed record GalleryPlaceYearFacet(
    int Year,
    string DisplayName,
    string Context,
    int PhotoCount,
    GalleryBrowseScope Scope,
    bool IsCurrent);

/// <summary>
/// Finds the same authoritative Place leaf across hierarchy years without loading photo pages.
/// location_key is preferred when both sides provide one; place_id is the compatibility fallback.
/// </summary>
public static class GalleryPlaceYearCatalog
{
    public static IReadOnlyList<GalleryPlaceYearFacet> FindSiblings(
        FastGalleryHierarchyDto hierarchy,
        GalleryBrowseScope currentScope)
    {
        ArgumentNullException.ThrowIfNull(hierarchy);
        ArgumentNullException.ThrowIfNull(currentScope);

        var currentIdentity = GalleryPlaceIdentity.FromScope(currentScope);
        if (currentIdentity is null)
        {
            return [];
        }

        var currentYear = (currentScope as GalleryBrowseScope.HierarchyScope)?.Year;

        return hierarchy.Roots
            .Where(yearNode => yearNode.Year.HasValue)
            .SelectMany(yearNode => EnumeratePlaces(yearNode, currentYear))
            .Where(candidate => currentIdentity.Matches(candidate.Identity))
            .GroupBy(candidate => candidate.Facet.Year)
            .Select(group => group
                .OrderByDescending(candidate => candidate.Facet.PhotoCount)
                .ThenBy(candidate => candidate.Facet.Context, StringComparer.CurrentCultureIgnoreCase)
                .First()
                .Facet)
            .OrderByDescending(candidate => candidate.Year)
            .ToArray();
    }

    private static IEnumerable<PlaceCandidate> EnumeratePlaces(
        FastGalleryHierarchyNodeDto yearNode,
        int? currentYear)
    {
        var year = yearNode.Year!.Value;
        foreach (var countryNode in yearNode.ChildNodes)
        {
            if (string.IsNullOrWhiteSpace(countryNode.Country))
            {
                continue;
            }

            var sourceCountry = countryNode.Country.Trim();
            var displayCountry = PlaceNormalizer.NormalizeCountry(sourceCountry);
            foreach (var regionNode in countryNode.ChildNodes)
            {
                var sourceRegion = string.IsNullOrWhiteSpace(regionNode.Region)
                    ? LibraryConstants.UnclassifiedTitle
                    : regionNode.Region.Trim();
                var displayRegion = PlaceNormalizer.NormalizeRegion(sourceRegion);
                if (string.IsNullOrWhiteSpace(displayRegion))
                {
                    displayRegion = LibraryConstants.UnclassifiedTitle;
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

                    var displayName = string.IsNullOrWhiteSpace(placeNode.DisplayName)
                        ? displayRegion
                        : placeNode.DisplayName.Trim();
                    var scope = GalleryBrowseScope.ForHierarchy(
                        year,
                        sourceCountry,
                        sourceRegion,
                        locationKey,
                        placeId);
                    yield return new PlaceCandidate(
                        new GalleryPlaceIdentity(locationKey, placeId),
                        new GalleryPlaceYearFacet(
                            year,
                            displayName,
                            $"{year}년 · {displayCountry} · {displayRegion}",
                            Math.Max(0, placeNode.Count),
                            scope,
                            IsCurrent: year == currentYear));
                }
            }
        }
    }

    private sealed record PlaceCandidate(
        GalleryPlaceIdentity Identity,
        GalleryPlaceYearFacet Facet);
}
