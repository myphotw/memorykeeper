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

    public string? CanonicalName { get; init; }

    public bool IsFavorite { get; init; }

    public string RegionSummary =>
        string.Join(" · ", new[] { Country, City }.Where(value => !string.IsNullOrWhiteSpace(value)));
}

public sealed class PlacePickerData
{
    public IReadOnlyList<PlacePickerItemDto> RecentPlaces { get; init; } = [];

    public IReadOnlyList<PlacePickerItemDto> FavoritePlaces { get; init; } = [];

    public IReadOnlyList<PlacePickerCountryNode> Hierarchy { get; init; } = [];

    public IReadOnlyList<PlacePickerItemDto> AllPlaces { get; init; } = [];
}
