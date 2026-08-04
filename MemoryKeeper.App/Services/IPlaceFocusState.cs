using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.App.Services;

/// <summary>
/// Shares Place focus and pending Visit Record filters between Home / Travel Records and Visit Record.
/// </summary>
public interface IPlaceFocusState
{
    Guid? FocusPlaceId { get; set; }

    /// <summary>
    /// Optional media to highlight after focusing a place (year + preview).
    /// </summary>
    Guid? FocusMediaId { get; set; }

    /// <summary>
    /// When set, Visit Record applies this search text on next load and clears it.
    /// </summary>
    string? PendingSearchText { get; set; }

    /// <summary>
    /// When set, Visit Record filters Timeline by season months on next load and clears it.
    /// </summary>
    TravelSeason? PendingSeason { get; set; }

    /// <summary>
    /// When set, Visit Record filters Timeline by country on next load and clears it.
    /// </summary>
    string? PendingCountry { get; set; }
}
