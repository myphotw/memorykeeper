using System.Collections.ObjectModel;
using MemoryKeeper.Application.DTOs;
using Microsoft.UI.Xaml.Media.Imaging;

namespace MemoryKeeper.App.ViewModels;

public interface IPlaceRegistrationDialogViewModel
{
    string RegistrationPreviewFileName { get; }

    BitmapImage? RegistrationPreviewImage { get; }

    string RegistrationGpsText { get; }

    string? CurrentPlaceStatusText { get; }

    string PlaceDialogStatus { get; set; }

    bool IsPlaceDialogBusy { get; }

    /// <summary>Place registered on the photo when the dialog opened (MK-052).</summary>
    PlaceLocationPreview OriginalLocation { get; }

    /// <summary>Current pending selection shown in the Preview Card (MK-052).</summary>
    PlaceLocationPreview SelectedLocation { get; }

    bool HasOriginalLocation { get; }

    bool HasSelectedLocation { get; }

    bool ShowLocationChangeComparison { get; }

    bool CanApplyPlaceChange { get; }

    ObservableCollection<PlacePickerItemDto> RecentPlaces { get; }

    ObservableCollection<PlacePickerItemDto> FavoritePlaces { get; }

    ObservableCollection<PlacePickerCountryNode> PlaceHierarchy { get; }

    ObservableCollection<PlacePickerItemDto> FilteredExistingPlaces { get; }

    string ExistingPlaceSearchText { get; set; }

    PlacePickerItemDto? SelectedExistingPlace { get; set; }

    ObservableCollection<NearbyPlaceCandidateDto> NearbyCandidates { get; }

    ObservableCollection<PlaceSuggestionDto> PlaceSearchResults { get; }

    string PlaceSearchText { get; set; }

    NearbyPlaceCandidateDto? SelectedNearbyCandidate { get; set; }

    PlaceSuggestionDto? SelectedPlaceSuggestion { get; set; }

    bool HasMapPickSelection { get; set; }

    bool SupportsMapPick { get; }

    double MapPickLatitude { get; }

    double MapPickLongitude { get; }

    double MapPickRadiusMeters { get; set; }

    /// <summary>Raised when Preview Card / Apply enablement should refresh.</summary>
    event EventHandler? PlacePreviewChanged;

    Task PreparePlaceRegistrationAsync();

    Task SearchExistingPlacesAsync();

    Task SearchPlaceSuggestionsAsync();

    /// <summary>Resolves suggestion coordinates for map pin move (does not select as Google place).</summary>
    Task<(double Latitude, double Longitude)?> ResolveSuggestionCoordinatesAsync(PlaceSuggestionDto suggestion);

    /// <summary>
    /// Resolves Google Place Details for a suggestion and updates the Preview Card.
    /// </summary>
    Task SelectGoogleSuggestionAsync(PlaceSuggestionDto suggestion);

    Task SelectNearbyCandidateAsync(NearbyPlaceCandidateDto candidate);

    Task SelectExistingPlaceAsync(PlacePickerItemDto place);

    Task ApplyMapPickAsync(double latitude, double longitude, double radiusMeters);

    /// <summary>Restores SelectedLocation to OriginalLocation without applying.</summary>
    void CancelPlaceRegistration();

    /// <summary>Undoes an aborted map-pick nested dialog.</summary>
    void DiscardMapPickSelection();

    Task<bool> ConfirmPlaceRegistrationAsync();

    Task TogglePlaceFavoriteAsync(PlacePickerItemDto place);

    void ClearExternalPlaceSelections();
}
