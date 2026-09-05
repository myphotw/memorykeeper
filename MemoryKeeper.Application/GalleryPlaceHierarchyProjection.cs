using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Application;

public sealed record GalleryPlaceProjectionItem(
    Guid PlaceId,
    string DisplayName,
    int PhotoCount);

public sealed record GalleryCountryProjectionItem(
    string DisplayName,
    string? CountryFilter,
    int PhotoCount,
    bool IsDomestic,
    bool IsUnclassified,
    IReadOnlyList<GalleryPlaceProjectionItem> Places);

/// <summary>
/// Projects the lightweight Fast Gallery year hierarchy into an all-years country/place tree.
/// Counts are summed from hierarchy aggregates; no photo snapshot or follow-up request is needed.
/// </summary>
public static class GalleryPlaceHierarchyProjection
{
    private const string DomesticCountry = "대한민국";

    public static IReadOnlyList<GalleryCountryProjectionItem> Build(FastGalleryHierarchyDto hierarchy)
    {
        ArgumentNullException.ThrowIfNull(hierarchy);

        var countries = new Dictionary<string, CountryAccumulator>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in hierarchy.Roots)
        {
            if (root.Year.HasValue)
            {
                foreach (var countryNode in root.ChildNodes)
                {
                    AddCountryBranch(countries, countryNode);
                }
            }
            else
            {
                AddCountryBranch(countries, root);
            }
        }

        return countries.Values
            .Select(country => new GalleryCountryProjectionItem(
                country.DisplayName,
                country.IsUnclassified ? null : country.DisplayName,
                country.PhotoCount,
                string.Equals(country.DisplayName, DomesticCountry, StringComparison.OrdinalIgnoreCase),
                country.IsUnclassified,
                country.Places.Values
                    .OrderBy(place => place.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                    .Select(place => new GalleryPlaceProjectionItem(
                        place.PlaceId,
                        place.DisplayName,
                        place.PhotoCount))
                    .ToList()))
            .OrderByDescending(country => country.IsDomestic)
            .ThenBy(country => country.IsUnclassified)
            .ThenBy(country => country.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static void AddCountryBranch(
        IDictionary<string, CountryAccumulator> countries,
        FastGalleryHierarchyNodeDto countryNode)
    {
        var normalizedCountry = PlaceNormalizer.NormalizeCountry(countryNode.Country);
        var isUnclassified = string.IsNullOrWhiteSpace(normalizedCountry)
                             || string.Equals(normalizedCountry, LibraryConstants.UnclassifiedTitle, StringComparison.OrdinalIgnoreCase)
                             || string.Equals(normalizedCountry, "기타", StringComparison.OrdinalIgnoreCase);
        var displayName = isUnclassified ? LibraryConstants.UnclassifiedTitle : normalizedCountry;
        if (!countries.TryGetValue(displayName, out var country))
        {
            country = new CountryAccumulator(displayName, isUnclassified);
            countries.Add(displayName, country);
        }

        country.PhotoCount += Math.Max(0, countryNode.Count);
        AddPlaces(country, countryNode);
    }

    private static void AddPlaces(CountryAccumulator country, FastGalleryHierarchyNodeDto node)
    {
        var placeId = node.MemorykeeperPlaceId ?? node.PlaceId;
        if (placeId is Guid id)
        {
            var title = string.IsNullOrWhiteSpace(node.DisplayName)
                ? LibraryConstants.UnclassifiedTitle
                : node.DisplayName.Trim();
            if (!country.Places.TryGetValue(id, out var place))
            {
                place = new PlaceAccumulator(id, title);
                country.Places.Add(id, place);
            }

            place.PhotoCount += Math.Max(0, node.Count);
        }

        foreach (var child in node.ChildNodes)
        {
            AddPlaces(country, child);
        }
    }

    private sealed class CountryAccumulator(string displayName, bool isUnclassified)
    {
        public string DisplayName { get; } = displayName;

        public bool IsUnclassified { get; } = isUnclassified;

        public int PhotoCount { get; set; }

        public Dictionary<Guid, PlaceAccumulator> Places { get; } = [];
    }

    private sealed class PlaceAccumulator(Guid placeId, string displayName)
    {
        public Guid PlaceId { get; } = placeId;

        public string DisplayName { get; } = displayName;

        public int PhotoCount { get; set; }
    }
}
