using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Application;

public sealed record GalleryPlaceProjectionItem(
    Guid PlaceId,
    string? LocationKey,
    string DisplayName,
    int PhotoCount,
    bool IsRegisteredPlace);

public sealed record GalleryPlaceRegionProjectionItem(
    string DisplayName,
    string CanonicalPath,
    int PhotoCount,
    IReadOnlyList<string> RegionFilters,
    IReadOnlyList<GalleryPlaceRegionProjectionItem> Regions,
    IReadOnlyList<GalleryPlaceProjectionItem> Places);

public sealed record GalleryCountryProjectionItem(
    string DisplayName,
    string? CountryFilter,
    int PhotoCount,
    bool IsDomestic,
    bool IsUnclassified,
    IReadOnlyList<GalleryPlaceRegionProjectionItem> Regions,
    IReadOnlyList<GalleryPlaceProjectionItem> Places);

/// <summary>
/// Projects the all-years Fast Gallery aggregates onto the canonical registered-place paths.
/// The projection is entirely in-memory: it does not scan photos or perform per-place requests.
/// </summary>
public static class GalleryPlaceHierarchyProjection
{
    private const string DomesticCountry = "대한민국";

    public static IReadOnlyList<GalleryCountryProjectionItem> Build(FastGalleryHierarchyDto hierarchy) =>
        Build(hierarchy, []);

    public static IReadOnlyList<GalleryCountryProjectionItem> Build(
        FastGalleryHierarchyDto hierarchy,
        IReadOnlyList<MemoryKeeperPlaceHierarchyPathApiDto> registeredPaths)
    {
        ArgumentNullException.ThrowIfNull(hierarchy);
        ArgumentNullException.ThrowIfNull(registeredPaths);

        var fastPlaces = CollectFastPlaces(hierarchy);
        var registeredIds = registeredPaths
            .Where(path => path.MemorykeeperPlaceId != Guid.Empty)
            .Select(path => path.MemorykeeperPlaceId)
            .ToHashSet();
        var countries = new Dictionary<string, CountryAccumulator>(StringComparer.OrdinalIgnoreCase);

        if (registeredPaths.Count > 0)
        {
            var linkedCounts = PlaceLinkedPhotoCountPolicy.Project(registeredPaths);
            foreach (var group in registeredPaths
                         .Where(path => path.MemorykeeperPlaceId != Guid.Empty)
                         .GroupBy(path => path.MemorykeeperPlaceId))
            {
                if (!linkedCounts.TryGetValue(group.Key, out var photoCount) || photoCount <= 0)
                {
                    continue;
                }

                var distinctPaths = group
                    .DistinctBy(CanonicalPathIdentity, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var primary = distinctPaths
                    .OrderByDescending(path => path.LocationHierarchyComplete)
                    .ThenByDescending(path => path.PhotoCount)
                    .ThenBy(path => path.FullPath, StringComparer.Ordinal)
                    .First();
                var country = GetOrAddCountry(countries, primary.Country);
                var sourceRegions = distinctPaths
                    .Select(path => path.Region)
                    .Concat(fastPlaces
                        .Where(place => place.PlaceId == group.Key)
                        .SelectMany(place => place.SourceRegions))
                    .Where(region => !string.IsNullOrWhiteSpace(region))
                    .Select(region => region!.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                var regionPath = sourceRegions.Count == 0
                    ? Array.Empty<string>()
                    : GetRegionPath(primary, country.DisplayName);
                var place = new GalleryPlaceProjectionItem(
                    PlaceId: group.Key,
                    // Registered leaves must use the authoritative Place UUID query.
                    LocationKey: null,
                    DisplayName: string.IsNullOrWhiteSpace(primary.DisplayName)
                        ? LibraryConstants.UnclassifiedTitle
                        : primary.DisplayName.Trim(),
                    PhotoCount: photoCount,
                    IsRegisteredPlace: true);
                country.AddPlace(regionPath, sourceRegions, place);
            }
        }

        // Keep raw Fast Gallery leaves and registered leaves not represented by an available
        // canonical response. This is the graceful fallback when the registered hierarchy fails.
        foreach (var fastPlace in fastPlaces.Where(place =>
                     place.PhotoCount > 0 && !registeredIds.Contains(place.PlaceId)))
        {
            var country = GetOrAddCountry(countries, fastPlace.Country);
            var regionPath = string.IsNullOrWhiteSpace(fastPlace.PrimaryRegion)
                ? Array.Empty<string>()
                : new[] { fastPlace.PrimaryRegion! };
            country.AddPlace(
                regionPath,
                fastPlace.SourceRegions,
                new GalleryPlaceProjectionItem(
                    fastPlace.PlaceId,
                    fastPlace.LocationKey,
                    fastPlace.DisplayName,
                    fastPlace.PhotoCount,
                    fastPlace.IsRegisteredPlace));
        }

        return countries.Values
            .Where(country => country.HasPlaces)
            .Select(country => country.ToProjection())
            .OrderByDescending(country => country.IsDomestic)
            .ThenBy(country => country.IsUnclassified)
            .ThenBy(country => country.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<FastPlaceAccumulator> CollectFastPlaces(FastGalleryHierarchyDto hierarchy)
    {
        var places = new Dictionary<Guid, FastPlaceAccumulator>();
        foreach (var root in hierarchy.Roots)
        {
            if (root.Year.HasValue)
            {
                foreach (var country in root.ChildNodes)
                {
                    CollectFastPlaces(country, country.Country, currentRegion: null, places);
                }
            }
            else
            {
                CollectFastPlaces(root, root.Country, currentRegion: null, places);
            }
        }

        return places.Values.ToList();
    }

    private static void CollectFastPlaces(
        FastGalleryHierarchyNodeDto node,
        string? country,
        string? currentRegion,
        IDictionary<Guid, FastPlaceAccumulator> places)
    {
        var nodeCountry = string.IsNullOrWhiteSpace(node.Country) ? country : node.Country;
        var nodeRegion = string.IsNullOrWhiteSpace(node.Region) ? currentRegion : node.Region;
        var placeId = node.MemorykeeperPlaceId ?? node.PlaceId;
        if (placeId is Guid id)
        {
            if (!places.TryGetValue(id, out var place))
            {
                place = new FastPlaceAccumulator(
                    id,
                    nodeCountry,
                    node.LocationKey,
                    string.IsNullOrWhiteSpace(node.DisplayName)
                        ? LibraryConstants.UnclassifiedTitle
                        : node.DisplayName.Trim(),
                    node.MemorykeeperPlaceId.HasValue);
                places.Add(id, place);
            }

            place.PhotoCount += Math.Max(0, node.Count);
            place.IsRegisteredPlace |= node.MemorykeeperPlaceId.HasValue;
            if (string.IsNullOrWhiteSpace(place.LocationKey) && !string.IsNullOrWhiteSpace(node.LocationKey))
            {
                place.LocationKey = node.LocationKey;
            }

            if (!string.IsNullOrWhiteSpace(nodeRegion))
            {
                var normalizedRegion = nodeRegion.Trim();
                place.RegionCounts[normalizedRegion] =
                    place.RegionCounts.GetValueOrDefault(normalizedRegion) + Math.Max(0, node.Count);
            }
        }

        foreach (var child in node.ChildNodes)
        {
            CollectFastPlaces(child, nodeCountry, nodeRegion, places);
        }
    }

    private static CountryAccumulator GetOrAddCountry(
        IDictionary<string, CountryAccumulator> countries,
        string? countryValue)
    {
        var normalized = PlaceNormalizer.NormalizeCountry(countryValue);
        var isUnclassified = string.IsNullOrWhiteSpace(normalized)
                             || string.Equals(normalized, LibraryConstants.UnclassifiedTitle, StringComparison.OrdinalIgnoreCase)
                             || string.Equals(normalized, "기타", StringComparison.OrdinalIgnoreCase);
        var displayName = isUnclassified ? LibraryConstants.UnclassifiedTitle : normalized;
        if (!countries.TryGetValue(displayName, out var country))
        {
            country = new CountryAccumulator(displayName, isUnclassified);
            countries.Add(displayName, country);
        }

        return country;
    }

    private static IReadOnlyList<string> GetRegionPath(
        MemoryKeeperPlaceHierarchyPathApiDto path,
        string displayCountry)
    {
        var segments = path.LocationPath?
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .Select(segment => segment.Trim())
            .ToList() ?? [];
        if (segments.Count > 0 && CountriesEqual(segments[0], displayCountry))
        {
            segments.RemoveAt(0);
        }

        if (segments.Count > 0
            && !string.IsNullOrWhiteSpace(path.DisplayName)
            && string.Equals(segments[^1], path.DisplayName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            segments.RemoveAt(segments.Count - 1);
        }

        if (segments.Count == 0 && !string.IsNullOrWhiteSpace(path.Region))
        {
            segments.Add(path.Region.Trim());
        }

        return DistinctConsecutive(
                segments.Where(segment => !CountriesEqual(segment, displayCountry)),
                StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool CountriesEqual(string left, string right) =>
        string.Equals(
            PlaceNormalizer.NormalizeCountry(left),
            PlaceNormalizer.NormalizeCountry(right),
            StringComparison.OrdinalIgnoreCase);

    private static string CanonicalPathIdentity(MemoryKeeperPlaceHierarchyPathApiDto path) =>
        !string.IsNullOrWhiteSpace(path.LocationKey)
            ? path.LocationKey.Trim()
            : !string.IsNullOrWhiteSpace(path.FullPath)
                ? path.FullPath.Trim()
                : string.Join("|", (path.LocationPath ?? []).Append(path.DisplayName?.Trim()));

    private sealed class CountryAccumulator(string displayName, bool isUnclassified)
    {
        private readonly RegionAccumulator _root = new(string.Empty, string.Empty);

        public string DisplayName { get; } = displayName;

        public bool IsUnclassified { get; } = isUnclassified;

        public bool HasPlaces => _root.HasPlaces;

        public void AddPlace(
            IReadOnlyList<string> regionPath,
            IReadOnlyList<string> sourceRegions,
            GalleryPlaceProjectionItem place) =>
            _root.AddPlace(regionPath, 0, sourceRegions, place);

        public GalleryCountryProjectionItem ToProjection()
        {
            var regions = _root.Children.Values
                .Select(region => region.ToProjection())
                .OrderBy(region => region.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            var places = _root.Places.Values
                .OrderBy(place => place.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            return new GalleryCountryProjectionItem(
                DisplayName,
                IsUnclassified ? null : DisplayName,
                regions.Sum(region => region.PhotoCount) + places.Sum(place => place.PhotoCount),
                string.Equals(DisplayName, DomesticCountry, StringComparison.OrdinalIgnoreCase),
                IsUnclassified,
                regions,
                places);
        }
    }

    private sealed class RegionAccumulator(string displayName, string canonicalPath)
    {
        public string DisplayName { get; } = displayName;

        public string CanonicalPath { get; } = canonicalPath;

        public Dictionary<string, RegionAccumulator> Children { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<Guid, GalleryPlaceProjectionItem> Places { get; } = [];

        public HashSet<string> RegionFilters { get; } = new(StringComparer.Ordinal);

        public bool HasPlaces => Places.Count > 0 || Children.Values.Any(child => child.HasPlaces);

        public void AddPlace(
            IReadOnlyList<string> regionPath,
            int index,
            IReadOnlyList<string> sourceRegions,
            GalleryPlaceProjectionItem place)
        {
            foreach (var sourceRegion in sourceRegions)
            {
                if (!string.IsNullOrWhiteSpace(sourceRegion))
                {
                    RegionFilters.Add(sourceRegion.Trim());
                }
            }

            if (index >= regionPath.Count)
            {
                Places[place.PlaceId] = place;
                return;
            }

            var title = regionPath[index];
            var path = string.IsNullOrWhiteSpace(CanonicalPath) ? title : $"{CanonicalPath} > {title}";
            if (!Children.TryGetValue(title, out var child))
            {
                child = new RegionAccumulator(title, path);
                Children.Add(title, child);
            }

            child.AddPlace(regionPath, index + 1, sourceRegions, place);
        }

        public GalleryPlaceRegionProjectionItem ToProjection()
        {
            var regions = Children.Values
                .Select(region => region.ToProjection())
                .OrderBy(region => region.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            var places = Places.Values
                .OrderBy(place => place.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            return new GalleryPlaceRegionProjectionItem(
                DisplayName,
                CanonicalPath,
                regions.Sum(region => region.PhotoCount) + places.Sum(place => place.PhotoCount),
                RegionFilters.OrderBy(region => region, StringComparer.CurrentCultureIgnoreCase).ToList(),
                regions,
                places);
        }
    }

    private sealed class FastPlaceAccumulator(
        Guid placeId,
        string? country,
        string? locationKey,
        string displayName,
        bool isRegisteredPlace)
    {
        public Guid PlaceId { get; } = placeId;

        public string? Country { get; } = country;

        public string? LocationKey { get; set; } = locationKey;

        public string DisplayName { get; } = displayName;

        public int PhotoCount { get; set; }

        public bool IsRegisteredPlace { get; set; } = isRegisteredPlace;

        public Dictionary<string, int> RegionCounts { get; } = new(StringComparer.Ordinal);

        public string? PrimaryRegion => RegionCounts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(pair => pair.Key)
            .FirstOrDefault();

        public IReadOnlyList<string> SourceRegions => RegionCounts.Keys.ToList();
    }

    private static IEnumerable<T> DistinctConsecutive<T>(
        IEnumerable<T> source,
        IEqualityComparer<T> comparer)
    {
        using var enumerator = source.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            yield break;
        }

        var previous = enumerator.Current;
        yield return previous;
        while (enumerator.MoveNext())
        {
            if (comparer.Equals(previous, enumerator.Current))
            {
                continue;
            }

            previous = enumerator.Current;
            yield return previous;
        }
    }
}
