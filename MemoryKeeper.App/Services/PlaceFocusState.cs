using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.App.Services;

public sealed class PlaceFocusState : IPlaceFocusState
{
    public Guid? FocusPlaceId { get; set; }

    public Guid? FocusMediaId { get; set; }

    public string? PendingSearchText { get; set; }

    public TravelSeason? PendingSeason { get; set; }

    public string? PendingCountry { get; set; }
}
