using MemoryKeeper.Application.DTOs;



namespace MemoryKeeper.App.Services;



public sealed class PlaceFocusState : IPlaceFocusState

{

    public Guid? FocusPlaceId { get; set; }



    public Guid? FocusMediaId { get; set; }



    public string? FocusPlaceName { get; set; }



    public string? PendingSearchText { get; set; }



    public TravelSeason? PendingSeason { get; set; }



    public string? PendingCountry { get; set; }



    public VisitMapNavigationSource NavigationSource { get; private set; }



    public int NavigationGeneration { get; private set; }



    public bool HasPendingFocus =>

        FocusPlaceId is not null

        || FocusMediaId is not null

        || !string.IsNullOrWhiteSpace(FocusPlaceName);



    public bool HasPendingFilters =>

        !string.IsNullOrWhiteSpace(PendingSearchText)

        || PendingSeason is not null

        || !string.IsNullOrWhiteSpace(PendingCountry);



    public int BeginNavigation(VisitMapNavigationSource source)

    {

        NavigationSource = source;

        NavigationGeneration++;

        return NavigationGeneration;

    }



    public void ClearFocus()

    {

        FocusPlaceId = null;

        FocusMediaId = null;

        FocusPlaceName = null;

    }



    public void ClearFilters()

    {

        PendingSearchText = null;

        PendingSeason = null;

        PendingCountry = null;

    }

}


