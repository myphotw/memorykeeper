using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Application;

public sealed class PlacePickerHierarchyProjectionResult
{
    public IReadOnlyList<PlacePickerCountryNode> Hierarchy { get; init; } = [];
    public IReadOnlyList<PlacePickerItemDto> AllPathItems { get; init; } = [];
    public IReadOnlyDictionary<Guid, PlacePickerItemDto> PrimaryItemByPlaceId { get; init; }
        = new Dictionary<Guid, PlacePickerItemDto>();
}

/// <summary>
/// Joins the canonical Backend hierarchy with registered-place state. Paths remain distinct,
/// while every selectable row retains the registered Place UUID used by mutations.
/// </summary>
public static class PlacePickerHierarchyProjection
{
    private const string Other = "기타";

    public static PlacePickerHierarchyProjectionResult Build(
        IReadOnlyList<PlaceDto> registeredPlaces,
        IReadOnlyList<MemoryKeeperPlaceHierarchyPathApiDto> hierarchyPaths,
        bool includeInactive = false)
    {
        ArgumentNullException.ThrowIfNull(registeredPlaces);
        ArgumentNullException.ThrowIfNull(hierarchyPaths);

        var activeById = registeredPlaces
            .Where(place => (includeInactive || place.IsActive) && place.Id != Guid.Empty)
            .GroupBy(place => place.Id)
            .ToDictionary(group => group.Key, group => group.First());

        var rows = hierarchyPaths
            .Where(path => path.MemorykeeperPlaceId != Guid.Empty
                           && activeById.ContainsKey(path.MemorykeeperPlaceId))
            .DistinctBy(PathIdentity.Create)
            .ToList();

        // The deployed contract includes zero-photo active places. Keep a defensive selectable
        // row if an older response omits one, without inventing geography from raw address fields.
        var returnedIds = rows.Select(path => path.MemorykeeperPlaceId).ToHashSet();
        foreach (var place in activeById.Values.Where(place => !returnedIds.Contains(place.Id)))
        {
            rows.Add(new MemoryKeeperPlaceHierarchyPathApiDto
            {
                MemorykeeperPlaceId = place.Id,
                DisplayName = place.DisplayName,
                Country = Other,
                Region = Other,
                LocationPath = [Other, Other],
                FullPath = $"{Other} > {Other} > {place.DisplayName}",
                PhotoCount = 0,
                LocationHierarchyComplete = false,
                LocationCleanupReason = "REGION_MISSING",
            });
        }

        var pathCounts = rows
            .GroupBy(path => path.MemorykeeperPlaceId)
            .ToDictionary(group => group.Key, group => group.Count());
        var items = rows
            .Select(path => ToPickerItem(path, activeById[path.MemorykeeperPlaceId], pathCounts[path.MemorykeeperPlaceId]))
            .OrderBy(item => item.Country, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Region, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.FullPath, StringComparer.Ordinal)
            .ToList();

        var hierarchy = items
            .GroupBy(item => BlankToOther(item.Country), StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(country => new PlacePickerCountryNode
            {
                Title = country.Key,
                Regions = country
                    .GroupBy(item => BlankToOther(item.Region), StringComparer.OrdinalIgnoreCase)
                    .OrderBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
                    .Select(region => new PlacePickerRegionNode
                    {
                        Title = region.Key,
                        Places = region.ToList(),
                    })
                    .ToList(),
            })
            .ToList();

        var primary = items
            .GroupBy(item => item.Id)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(item => item.LocationHierarchyComplete)
                    .ThenByDescending(item => item.PhotoCount)
                    .ThenBy(item => item.FullPath, StringComparer.Ordinal)
                    .First());

        return new PlacePickerHierarchyProjectionResult
        {
            Hierarchy = hierarchy,
            AllPathItems = items,
            PrimaryItemByPlaceId = primary,
        };
    }

    public static bool Matches(PlacePickerItemDto item, PlaceDto place, string query)
    {
        var term = query.Trim();
        return term.Length == 0
               || item.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
               || item.FullPath.Contains(term, StringComparison.OrdinalIgnoreCase)
               || item.Country.Contains(term, StringComparison.OrdinalIgnoreCase)
               || item.Region.Contains(term, StringComparison.OrdinalIgnoreCase)
               || place.CanonicalName?.Contains(term, StringComparison.OrdinalIgnoreCase) == true
               || place.Address.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    private static PlacePickerItemDto ToPickerItem(
        MemoryKeeperPlaceHierarchyPathApiDto path,
        PlaceDto place,
        int pathCount)
    {
        var country = BlankToOther(path.Country);
        var region = BlankToOther(path.Region);
        var displayName = string.IsNullOrWhiteSpace(path.DisplayName)
            ? place.DisplayName
            : path.DisplayName.Trim();
        var locationSegments = path.LocationPath?
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .Select(segment => segment.Trim())
            .ToArray() ?? [];
        var locationPath = locationSegments.Length == 0
            ? $"{country} > {region}"
            : string.Join(" > ", locationSegments);
        var fullPath = string.IsNullOrWhiteSpace(path.FullPath)
            ? $"{locationPath} > {displayName}"
            : path.FullPath.Trim();
        return new PlacePickerItemDto
        {
            Id = place.Id,
            DisplayName = displayName,
            Country = country,
            City = region,
            Region = region,
            LocationKey = path.LocationKey,
            LocationPath = locationPath,
            FullPath = fullPath,
            PhotoCount = Math.Max(0, path.PhotoCount),
            LocationHierarchyComplete = path.LocationHierarchyComplete,
            LocationCleanupReason = path.LocationCleanupReason,
            HierarchyPathCount = pathCount,
            CanonicalName = place.CanonicalName,
            IsFavorite = place.IsFavorite,
            IsActive = place.IsActive,
        };
    }

    private static string BlankToOther(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Other : value.Trim();

    private sealed record PathIdentity(Guid PlaceId, string CanonicalPathIdentity)
    {
        public static PathIdentity Create(MemoryKeeperPlaceHierarchyPathApiDto path) => new(
            path.MemorykeeperPlaceId,
            !string.IsNullOrWhiteSpace(path.LocationKey)
                ? path.LocationKey.Trim()
                : !string.IsNullOrWhiteSpace(path.FullPath)
                    ? path.FullPath.Trim()
                    : string.Join("|", path.Country?.Trim(), path.Region?.Trim(), path.DisplayName?.Trim()));
    }
}
