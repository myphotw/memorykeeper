using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Application;

/// <summary>
/// UI-independent Gallery browsing scope. Additional hierarchy levels can be
/// introduced without coupling the query contract to WinUI or MAUI controls.
/// </summary>
public abstract record GalleryBrowseScope
{
    private GalleryBrowseScope()
    {
    }

    public sealed record DefaultFeedScope : GalleryBrowseScope;

    public sealed record YearScope : GalleryBrowseScope
    {
        public YearScope(int year)
        {
            if (year is < 1 or > 9999)
            {
                throw new ArgumentOutOfRangeException(nameof(year));
            }

            Year = year;
        }

        public int Year { get; }
    }

    public sealed record HierarchyScope : GalleryBrowseScope
    {
        public HierarchyScope(
            int? year = null,
            string? country = null,
            string? region = null,
            string? locationKey = null,
            Guid? placeId = null,
            bool? unclassified = null,
            bool? dateUnclassified = null)
        {
            if (year is < 1 or > 9999)
            {
                throw new ArgumentOutOfRangeException(nameof(year));
            }

            if (dateUnclassified == true && year is null)
            {
                throw new ArgumentException("Date-unclassified browsing requires a year.", nameof(year));
            }

            if (string.IsNullOrWhiteSpace(country)
                && string.IsNullOrWhiteSpace(region)
                && string.IsNullOrWhiteSpace(locationKey)
                && placeId is null
                && unclassified != true
                && dateUnclassified != true)
            {
                throw new ArgumentException("At least one hierarchy filter is required.");
            }

            Year = year;
            Country = Normalize(country);
            Region = Normalize(region);
            LocationKey = Normalize(locationKey);
            PlaceId = placeId;
            Unclassified = unclassified;
            DateUnclassified = dateUnclassified;
        }

        public int? Year { get; }

        public string? Country { get; }

        public string? Region { get; }

        public string? LocationKey { get; }

        public Guid? PlaceId { get; }

        public bool? Unclassified { get; }

        public bool? DateUnclassified { get; }

        private static string? Normalize(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public sealed record CanonicalRegionScope : GalleryBrowseScope
    {
        private const char Separator = '\u001f';

        public CanonicalRegionScope(
            int year,
            string country,
            string canonicalRegion,
            IEnumerable<string> sourceRegions)
        {
            if (year is < 1 or > 9999)
            {
                throw new ArgumentOutOfRangeException(nameof(year));
            }

            ArgumentException.ThrowIfNullOrWhiteSpace(country);
            ArgumentException.ThrowIfNullOrWhiteSpace(canonicalRegion);
            ArgumentNullException.ThrowIfNull(sourceRegions);

            var normalizedRegions = sourceRegions
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            if (normalizedRegions.Length == 0)
            {
                throw new ArgumentException("At least one exact source region is required.", nameof(sourceRegions));
            }

            Year = year;
            Country = country.Trim();
            CanonicalRegion = canonicalRegion.Trim();
            SourceRegionsKey = string.Join(Separator, normalizedRegions.Select(Uri.EscapeDataString));
        }

        public int Year { get; }

        public string Country { get; }

        public string CanonicalRegion { get; }

        private string SourceRegionsKey { get; }

        public IReadOnlyList<string> SourceRegions => SourceRegionsKey
            .Split(Separator, StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .ToArray();
    }

    public static GalleryBrowseScope DefaultFeed { get; } = new DefaultFeedScope();

    public static GalleryBrowseScope ForYear(int year) => new YearScope(year);

    public static GalleryBrowseScope ForUnclassified() => new HierarchyScope(unclassified: true);

    public static GalleryBrowseScope ForHierarchy(
        int? year = null,
        string? country = null,
        string? region = null,
        string? locationKey = null,
        Guid? placeId = null) =>
        new HierarchyScope(year, country, region, locationKey, placeId);

    public static GalleryBrowseScope ForCanonicalRegion(
        int year,
        string country,
        string canonicalRegion,
        IEnumerable<string> sourceRegions) =>
        new CanonicalRegionScope(year, country, canonicalRegion, sourceRegions);

    public static GalleryBrowseScope ForDateUnclassified(int year) =>
        new HierarchyScope(year: year, dateUnclassified: true);
}

public static class GalleryBrowseScopeQueryMapper
{
    public static IReadOnlyList<FastGalleryPhotoQuery> ToQueries(
        GalleryBrowseScope scope,
        int limit = 50,
        string? cursor = null)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (scope is GalleryBrowseScope.CanonicalRegionScope region)
        {
            return region.SourceRegions
                .Select(sourceRegion => new FastGalleryPhotoQuery
                {
                    Limit = limit,
                    Cursor = cursor,
                    Year = region.Year,
                    Country = region.Country,
                    Region = sourceRegion,
                })
                .ToArray();
        }

        return [ToQuery(scope, limit, cursor)];
    }

    public static FastGalleryPhotoQuery ToQuery(
        GalleryBrowseScope scope,
        int limit = 50,
        string? cursor = null)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return scope switch
        {
            GalleryBrowseScope.DefaultFeedScope => new FastGalleryPhotoQuery
            {
                Limit = limit,
                Cursor = cursor,
            },
            GalleryBrowseScope.YearScope year => new FastGalleryPhotoQuery
            {
                Limit = limit,
                Cursor = cursor,
                Year = year.Year,
            },
            GalleryBrowseScope.HierarchyScope hierarchy => new FastGalleryPhotoQuery
            {
                Limit = limit,
                Cursor = cursor,
                Year = hierarchy.Year,
                Country = hierarchy.Country,
                Region = hierarchy.Region,
                LocationKey = hierarchy.LocationKey,
                PlaceId = string.IsNullOrWhiteSpace(hierarchy.LocationKey) ? hierarchy.PlaceId : null,
                Unclassified = hierarchy.Unclassified,
                DateUnclassified = hierarchy.DateUnclassified,
            },
            GalleryBrowseScope.CanonicalRegionScope => throw new InvalidOperationException(
                "Canonical region scopes contain multiple exact Backend queries. Use ToQueries()."),
            _ => throw new ArgumentOutOfRangeException(nameof(scope)),
        };
    }
}

public static class FastGalleryYearCatalog
{
    public static IReadOnlyList<int> FromHierarchy(FastGalleryHierarchyDto hierarchy)
    {
        ArgumentNullException.ThrowIfNull(hierarchy);

        return hierarchy.Roots
            .Where(node => node.Year.HasValue && node.Count > 0)
            .Select(node => node.Year!.Value)
            .Distinct()
            .OrderByDescending(year => year)
            .ToArray();
    }
}
