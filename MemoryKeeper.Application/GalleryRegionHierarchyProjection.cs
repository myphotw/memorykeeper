using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Application;

public sealed record GalleryRegionProjectionItem(
    string CanonicalIdentity,
    string DisplayName,
    int PhotoCount,
    IReadOnlyList<string> SourceRegions);

/// <summary>
/// Groups Fast Gallery region aggregates by the existing geographic canonical name while
/// retaining every exact Backend region filter needed to query the underlying photos.
/// Call this separately for each country branch so aliases never cross country boundaries.
/// </summary>
public static class GalleryRegionHierarchyProjection
{
    public static IReadOnlyList<GalleryRegionProjectionItem> Build(
        IEnumerable<FastGalleryHierarchyNodeDto> regionNodes)
    {
        ArgumentNullException.ThrowIfNull(regionNodes);

        var regions = new Dictionary<string, RegionAccumulator>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in regionNodes)
        {
            var rawRegion = node.Region ?? string.Empty;
            var canonicalRegion = PlaceNormalizer.NormalizeRegion(rawRegion);
            var canonicalIdentity = string.IsNullOrWhiteSpace(canonicalRegion)
                ? LibraryConstants.UnclassifiedTitle
                : canonicalRegion;

            if (!regions.TryGetValue(canonicalIdentity, out var region))
            {
                region = new RegionAccumulator(canonicalIdentity);
                regions.Add(canonicalIdentity, region);
            }

            region.PhotoCount += Math.Max(0, node.Count);
            if (!string.IsNullOrWhiteSpace(rawRegion)
                && !region.SourceRegions.Contains(rawRegion, StringComparer.Ordinal))
            {
                region.SourceRegions.Add(rawRegion);
            }
        }

        return regions.Values
            .Select(region => new GalleryRegionProjectionItem(
                region.CanonicalIdentity,
                SelectDisplayName(region),
                region.PhotoCount,
                region.SourceRegions.ToList()))
            .ToList();
    }

    private static string SelectDisplayName(RegionAccumulator region)
    {
        var koreanDisplay = region.SourceRegions
            .Where(ContainsHangul)
            .Select(PlaceNormalizer.NormalizeRegion)
            .Where(value => !string.IsNullOrWhiteSpace(value) && ContainsHangul(value))
            .OrderBy(value => value.Length)
            .ThenBy(value => value, StringComparer.Ordinal)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(koreanDisplay))
        {
            return koreanDisplay;
        }

        if (ContainsHangul(region.CanonicalIdentity))
        {
            return region.CanonicalIdentity;
        }

        return region.SourceRegions
                   .OrderBy(value => value.Length)
                   .ThenBy(value => value, StringComparer.OrdinalIgnoreCase)
                   .ThenBy(value => value, StringComparer.Ordinal)
                   .FirstOrDefault()
               ?? region.CanonicalIdentity;
    }

    private static bool ContainsHangul(string value) =>
        value.Any(character => character is >= '\uac00' and <= '\ud7a3');

    private sealed class RegionAccumulator(string canonicalIdentity)
    {
        public string CanonicalIdentity { get; } = canonicalIdentity;

        public int PhotoCount { get; set; }

        public List<string> SourceRegions { get; } = [];
    }
}
