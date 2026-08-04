namespace MemoryKeeper.Application.DTOs;

public sealed class PlaceSuggestionDto
{
    public string PlaceId { get; init; } = string.Empty;

    public string PrimaryText { get; init; } = string.Empty;

    public string SecondaryText { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;
}
