namespace MemoryKeeper.Application.DTOs;

public sealed class PlacePickerCountryNode
{
    public string Title { get; init; } = string.Empty;

    public IReadOnlyList<PlacePickerRegionNode> Regions { get; init; } = [];
}

public sealed class PlacePickerRegionNode
{
    public string Title { get; init; } = string.Empty;

    public IReadOnlyList<PlacePickerItemDto> Places { get; init; } = [];
}

public sealed class PlacePickerItemDto
{
    public Guid Id { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public string Country { get; init; } = string.Empty;

    public string City { get; init; } = string.Empty;

    public string Region { get; init; } = string.Empty;

    public string? LocationKey { get; init; }

    public string LocationPath { get; init; } = string.Empty;

    public string FullPath { get; init; } = string.Empty;

    public int PhotoCount { get; init; }

    public bool LocationHierarchyComplete { get; init; }

    public string? LocationCleanupReason { get; init; }

    public int HierarchyPathCount { get; init; } = 1;

    public string? CanonicalName { get; init; }

    public bool IsFavorite { get; init; }

    public bool IsActive { get; init; } = true;

    public bool HasMultipleHierarchyPaths => HierarchyPathCount > 1;

    public string RegionSummary => !string.IsNullOrWhiteSpace(FullPath)
        ? FullPath
        : string.Join(" · ", new[] { Country, Region, City }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase));

    public string TreeDisplayText => string.Join(" · ", new[]
    {
        DisplayName,
        $"사진 {PhotoCount:N0}장",
        HasMultipleHierarchyPaths ? "같은 등록 장소" : null,
        !LocationHierarchyComplete || !string.IsNullOrWhiteSpace(LocationCleanupReason) ? "정보 확인 필요" : null,
        !IsActive ? "비활성" : null,
    }.Where(value => !string.IsNullOrWhiteSpace(value)));
}

public sealed class PlacePickerData
{
    public IReadOnlyList<PlacePickerItemDto> RecentPlaces { get; init; } = [];

    public IReadOnlyList<PlacePickerItemDto> FavoritePlaces { get; init; } = [];

    public IReadOnlyList<PlacePickerCountryNode> Hierarchy { get; init; } = [];

    public IReadOnlyList<PlacePickerItemDto> AllPlaces { get; init; } = [];
}
