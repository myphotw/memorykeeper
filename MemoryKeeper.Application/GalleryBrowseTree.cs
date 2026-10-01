using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Application;

public enum GalleryBrowseTreeNodeKind
{
    Country,
    Region,
    Place,
    Unclassified,
    DateUnclassified,
}

public sealed record GalleryBrowseTreeNode(
    string Key,
    GalleryBrowseTreeNodeKind Kind,
    string DisplayName,
    string Context,
    int PhotoCount,
    GalleryBrowseScope Scope,
    int Depth,
    IReadOnlyList<GalleryBrowseTreeNode> Children);

public sealed record GalleryYearBrowseTree(
    int Year,
    int PhotoCount,
    IReadOnlyList<GalleryBrowseTreeNode> Roots);

public sealed record GalleryBrowseTreeFocus(
    IReadOnlySet<string> ExpandedNodeKeys,
    string? CurrentNodeKey);

/// <summary>
/// Preserves the authoritative Fast Gallery year/country/region/place relationship for
/// lightweight clients. It only projects hierarchy aggregates and never loads photo pages.
/// </summary>
public static class GalleryBrowseTree
{
    public static GalleryYearBrowseTree? ForYear(FastGalleryHierarchyDto hierarchy, int year)
    {
        ArgumentNullException.ThrowIfNull(hierarchy);
        var yearNode = hierarchy.Roots.FirstOrDefault(node => node.Year == year);
        if (yearNode is null)
        {
            return null;
        }

        var roots = new List<GalleryBrowseTreeNode>();
        var unclassifiedCount = 0;
        foreach (var countryNode in yearNode.ChildNodes)
        {
            var sourceCountry = countryNode.Country?.Trim();
            var displayCountry = PlaceNormalizer.NormalizeCountry(sourceCountry);
            if (IsUnclassifiedCountry(displayCountry))
            {
                unclassifiedCount += Math.Max(0, countryNode.Count);
                continue;
            }

            roots.Add(BuildCountry(year, countryNode, sourceCountry!, displayCountry));
        }

        roots.Sort((left, right) => StringComparer.CurrentCultureIgnoreCase.Compare(
            left.DisplayName,
            right.DisplayName));

        if (unclassifiedCount > 0)
        {
            roots.Add(new GalleryBrowseTreeNode(
                Key(year, "unclassified"),
                GalleryBrowseTreeNodeKind.Unclassified,
                LibraryConstants.UnclassifiedTitle,
                $"{year}년",
                unclassifiedCount,
                new GalleryBrowseScope.HierarchyScope(year: year, unclassified: true),
                Depth: 0,
                Children: []));
        }

        if (yearNode.DateUnclassifiedCount > 0)
        {
            roots.Add(new GalleryBrowseTreeNode(
                Key(year, "date-unclassified"),
                GalleryBrowseTreeNodeKind.DateUnclassified,
                "날짜 미분류",
                $"{year}년",
                yearNode.DateUnclassifiedCount,
                GalleryBrowseScope.ForDateUnclassified(year),
                Depth: 0,
                Children: []));
        }

        return new GalleryYearBrowseTree(year, yearNode.Count, roots);
    }

    public static IReadOnlyList<GalleryBrowseTreeNode> FindPath(
        GalleryYearBrowseTree tree,
        GalleryBrowseScope scope)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(scope);

        foreach (var root in tree.Roots)
        {
            var path = FindPath(root, scope);
            if (path.Count > 0)
            {
                return path;
            }
        }

        return [];
    }

    public static GalleryBrowseTreeFocus CreateFocus(
        GalleryYearBrowseTree tree,
        GalleryBrowseScope scope)
    {
        var path = FindPath(tree, scope);
        return new GalleryBrowseTreeFocus(
            path.Where(node => node.Children.Count > 0)
                .Select(node => node.Key)
                .ToHashSet(StringComparer.Ordinal),
            path.LastOrDefault()?.Key);
    }

    private static GalleryBrowseTreeNode BuildCountry(
        int year,
        FastGalleryHierarchyNodeDto countryNode,
        string sourceCountry,
        string displayCountry)
    {
        var sourceRegionNodes = countryNode.ChildNodes.ToArray();
        var children = GalleryRegionHierarchyProjection.Build(sourceRegionNodes)
            .Select(region => BuildRegion(
                year,
                sourceCountry,
                displayCountry,
                region,
                sourceRegionNodes))
            .OrderBy(node => node.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(node => node.Key, StringComparer.Ordinal)
            .ToArray();

        return new GalleryBrowseTreeNode(
            Key(year, "country", sourceCountry),
            GalleryBrowseTreeNodeKind.Country,
            displayCountry,
            $"{year}년",
            Math.Max(0, countryNode.Count),
            GalleryBrowseScope.ForHierarchy(year: year, country: sourceCountry),
            Depth: 0,
            Children: children);
    }

    private static GalleryBrowseTreeNode BuildRegion(
        int year,
        string sourceCountry,
        string displayCountry,
        GalleryRegionProjectionItem region,
        IReadOnlyList<FastGalleryHierarchyNodeDto> sourceRegionNodes)
    {
        var matchingRegionNodes = sourceRegionNodes
            .Where(node => region.SourceRegions.Count > 0
                ? region.SourceRegions.Contains(node.Region ?? string.Empty, StringComparer.Ordinal)
                : string.Equals(
                    PlaceNormalizer.NormalizeRegion(node.Region),
                    region.CanonicalIdentity,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var regionScope = region.SourceRegions.Count > 0
            ? GalleryBrowseScope.ForCanonicalRegion(
                year,
                sourceCountry,
                region.CanonicalIdentity,
                region.SourceRegions)
            : GalleryBrowseScope.ForHierarchy(
                year: year,
                country: sourceCountry,
                region: region.DisplayName);

        var children = matchingRegionNodes
            .SelectMany(regionNode => regionNode.ChildNodes.Select(placeNode => BuildPlace(
                year,
                sourceCountry,
                displayCountry,
                regionNode.Region ?? string.Empty,
                region.DisplayName,
                placeNode)))
            .Where(node => node is not null)
            .Cast<GalleryBrowseTreeNode>()
            .OrderBy(node => node.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(node => node.Key, StringComparer.Ordinal)
            .ToArray();

        return new GalleryBrowseTreeNode(
            Key(year, "country", sourceCountry, "region", region.CanonicalIdentity),
            GalleryBrowseTreeNodeKind.Region,
            region.DisplayName,
            $"{year}년 · {displayCountry}",
            region.PhotoCount,
            regionScope,
            Depth: 1,
            Children: children);
    }

    private static GalleryBrowseTreeNode? BuildPlace(
        int year,
        string sourceCountry,
        string displayCountry,
        string sourceRegion,
        string displayRegion,
        FastGalleryHierarchyNodeDto placeNode)
    {
        var placeId = placeNode.MemorykeeperPlaceId ?? placeNode.PlaceId;
        var locationKey = string.IsNullOrWhiteSpace(placeNode.LocationKey)
            ? null
            : placeNode.LocationKey.Trim();
        if (placeId is null && locationKey is null)
        {
            return null;
        }

        var displayName = string.IsNullOrWhiteSpace(placeNode.DisplayName)
            ? displayRegion
            : placeNode.DisplayName.Trim();
        var identity = locationKey ?? placeId!.Value.ToString("D");
        return new GalleryBrowseTreeNode(
            Key(year, "country", sourceCountry, "region", sourceRegion, "place", identity),
            GalleryBrowseTreeNodeKind.Place,
            displayName,
            $"{year}년 · {displayCountry} · {displayRegion}",
            Math.Max(0, placeNode.Count),
            GalleryBrowseScope.ForHierarchy(
                year,
                sourceCountry,
                sourceRegion,
                locationKey,
                placeId),
            Depth: 2,
            Children: []);
    }

    private static bool IsUnclassifiedCountry(string? country) =>
        string.IsNullOrWhiteSpace(country)
        || string.Equals(country, LibraryConstants.UnclassifiedTitle, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<GalleryBrowseTreeNode> FindPath(
        GalleryBrowseTreeNode node,
        GalleryBrowseScope scope)
    {
        if (ScopeMatches(node.Scope, scope))
        {
            return [node];
        }

        foreach (var child in node.Children)
        {
            var childPath = FindPath(child, scope);
            if (childPath.Count > 0)
            {
                return [node, .. childPath];
            }
        }

        return [];
    }

    private static bool ScopeMatches(
        GalleryBrowseScope nodeScope,
        GalleryBrowseScope currentScope)
    {
        if (Equals(nodeScope, currentScope))
        {
            return true;
        }

        if (nodeScope is not GalleryBrowseScope.CanonicalRegionScope canonicalRegion
            || currentScope is not GalleryBrowseScope.HierarchyScope
            {
                Year: int year,
                Country: not null,
                Region: not null,
                LocationKey: null,
                PlaceId: null,
            } hierarchy
            || hierarchy.Unclassified == true
            || hierarchy.DateUnclassified == true)
        {
            return false;
        }

        var countryMatches = string.Equals(
                                 canonicalRegion.Country,
                                 hierarchy.Country,
                                 StringComparison.OrdinalIgnoreCase)
                             || string.Equals(
                                 PlaceNormalizer.NormalizeCountry(canonicalRegion.Country),
                                 PlaceNormalizer.NormalizeCountry(hierarchy.Country),
                                 StringComparison.OrdinalIgnoreCase);
        var regionMatches = canonicalRegion.SourceRegions.Contains(
                                hierarchy.Region,
                                StringComparer.Ordinal)
                            || string.Equals(
                                canonicalRegion.CanonicalRegion,
                                PlaceNormalizer.NormalizeRegion(hierarchy.Region),
                                StringComparison.OrdinalIgnoreCase);
        return canonicalRegion.Year == year && countryMatches && regionMatches;
    }

    private static string Key(int year, params string[] parts) =>
        string.Join(":", new[] { year.ToString() }.Concat(parts.Select(Uri.EscapeDataString)));
}
