using MemoryKeeper.Application.DTOs;



namespace MemoryKeeper.App.Services;



public enum VisitMapNavigationSource

{

    Unknown = 0,

    Home = 1,

    TravelRecord = 2,

    ShellNav = 3,

    PhotoDetail = 4,

}



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

    /// Optional place-name fallback when FocusPlaceId does not match (no search filter).

    /// </summary>

    string? FocusPlaceName { get; set; }



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



    VisitMapNavigationSource NavigationSource { get; }



    /// <summary>

    /// Monotonic generation for visit-map activations. Stale async work must ignore older gens.

    /// </summary>

    int NavigationGeneration { get; }



    bool HasPendingFocus { get; }



    bool HasPendingFilters { get; }



    /// <summary>

    /// Starts a visit-map navigation and increments <see cref="NavigationGeneration"/>.

    /// </summary>

    int BeginNavigation(VisitMapNavigationSource source);



    void ClearFocus();



    void ClearFilters();

}


