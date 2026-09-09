using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MemoryKeeper.App.Models;
using MemoryKeeper.App.Services;
using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Infrastructure.Services.Api;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;

namespace MemoryKeeper.App.ViewModels;

/// <summary>Small adapter that lets Gallery reuse the common place-registration dialog.</summary>
public partial class GalleryPlaceEditSessionViewModel : ObservableObject, IPlaceRegistrationDialogViewModel
{
    private const double DefaultRadiusMeters = 100d;

    private readonly MemoryKeeperPlaceService _placeService;
    private readonly GalleryPlaceAssignmentWorkflow _workflow;
    private readonly ILocationResolver _locationResolver;
    private readonly ILogger<GalleryPlaceEditSessionViewModel> _logger;
    private readonly IReadOnlyList<GalleryItem> _selectedItems;
    private IReadOnlyList<MemoryKeeperFilePlaceStateDto> _placeStates = [];
    private IReadOnlyList<PlaceDto> _places = [];

    public GalleryPlaceEditSessionViewModel(
        MemoryKeeperPlaceService placeService,
        GalleryPlaceAssignmentWorkflow workflow,
        ILocationResolver locationResolver,
        ILogger<GalleryPlaceEditSessionViewModel> logger,
        IReadOnlyList<GalleryItem> selectedItems)
    {
        _placeService = placeService;
        _workflow = workflow;
        _locationResolver = locationResolver;
        _logger = logger;
        _selectedItems = selectedItems;
        RegistrationPreviewFileName = selectedItems.Count == 1
            ? selectedItems[0].FileName
            : $"선택한 사진 {selectedItems.Count}장";
        RegistrationPreviewImage = selectedItems.FirstOrDefault()?.ThumbnailImage;
    }

    public XamlRoot? HostXamlRoot { get; set; }
    public Func<string, PlaceRadiusExpansionPlan, Task<bool>>? RadiusExpansionPreviewHandler { get; set; }
    public Action<string, string>? MutationStarted { get; set; }
    public Action<string, string>? MutationProgress { get; set; }

    public bool MutationAttempted { get; private set; }
    public bool Succeeded { get; private set; }
    public bool RadiusWasChanged { get; private set; }
    public Guid? TargetPlaceId { get; private set; }
    public bool ConflictFailure { get; private set; }

    public string RegistrationPreviewFileName { get; }
    public BitmapImage? RegistrationPreviewImage { get; }
    [ObservableProperty] private string registrationGpsText = string.Empty;
    public string? CurrentPlaceStatusText { get; private set; }
    [ObservableProperty] private string placeDialogStatus = string.Empty;
    [ObservableProperty] private bool isPlaceDialogBusy;
    [ObservableProperty] private PlaceLocationPreview originalLocation = PlaceLocationPreview.Empty;
    [ObservableProperty] private PlaceLocationPreview selectedLocation = PlaceLocationPreview.Empty;
    [ObservableProperty] private ObservableCollection<PlacePickerItemDto> recentPlaces = [];
    [ObservableProperty] private ObservableCollection<PlacePickerItemDto> favoritePlaces = [];
    [ObservableProperty] private ObservableCollection<PlacePickerCountryNode> placeHierarchy = [];
    [ObservableProperty] private ObservableCollection<PlacePickerItemDto> filteredExistingPlaces = [];
    [ObservableProperty] private string existingPlaceSearchText = string.Empty;
    [ObservableProperty] private PlacePickerItemDto? selectedExistingPlace;
    [ObservableProperty] private ObservableCollection<NearbyPlaceCandidateDto> nearbyCandidates = [];
    [ObservableProperty] private ObservableCollection<PlaceSuggestionDto> placeSearchResults = [];
    [ObservableProperty] private string placeSearchText = string.Empty;
    [ObservableProperty] private NearbyPlaceCandidateDto? selectedNearbyCandidate;
    [ObservableProperty] private PlaceSuggestionDto? selectedPlaceSuggestion;
    [ObservableProperty] private bool hasMapPickSelection;
    [ObservableProperty] private double mapPickLatitude = 37.5665;
    [ObservableProperty] private double mapPickLongitude = 126.9780;
    [ObservableProperty] private double mapPickRadiusMeters = DefaultRadiusMeters;

    public bool HasOriginalLocation => !OriginalLocation.IsEmpty;
    public bool HasSelectedLocation => !SelectedLocation.IsEmpty;
    public bool ShowLocationChangeComparison => HasOriginalLocation && HasSelectedLocation && CanApplyPlaceChange;
    public bool CanApplyPlaceChange => PlaceLocationPreview.CanApply(OriginalLocation, SelectedLocation);
    public bool SupportsMapPick => true;

    public event EventHandler? PlacePreviewChanged;

    public async Task PreparePlaceRegistrationAsync()
    {
        ResetPickerState();
        IsPlaceDialogBusy = true;
        try
        {
            var response = await _workflow.QueryStatesAsync(GetFileIds());
            _placeStates = response.Items;
            var firstGps = _placeStates.FirstOrDefault(item => item.GpsLat.HasValue && item.GpsLon.HasValue);
            if (firstGps is not null)
            {
                MapPickLatitude = firstGps.GpsLat!.Value;
                MapPickLongitude = firstGps.GpsLon!.Value;
                RegistrationGpsText = _placeStates.Count(item => item.GpsLat.HasValue && item.GpsLon.HasValue) == 1
                    ? $"{MapPickLatitude:F6}, {MapPickLongitude:F6}"
                    : $"GPS 사진 {_placeStates.Count(item => item.GpsLat.HasValue && item.GpsLon.HasValue)}장";
            }

            await LoadPlacesAsync();
            var placeIds = _placeStates.Select(item => item.MemorykeeperPlaceId).Distinct().ToList();
            if (placeIds.Count == 1 && placeIds[0] is Guid currentId)
            {
                var current = _places.FirstOrDefault(place => place.Id == currentId)
                    ?? await _placeService.GetPlaceAsync(currentId);
                OriginalLocation = PlaceLocationPreview.FromPlaceDto(current, PlaceLocationSource.Original);
                SelectedLocation = Clone(OriginalLocation);
                CurrentPlaceStatusText = $"현재 장소: {current.DisplayName}";
            }
            else
            {
                OriginalLocation = PlaceLocationPreview.Empty;
                SelectedLocation = PlaceLocationPreview.Empty;
                CurrentPlaceStatusText = placeIds.Count > 1 ? "현재 장소: 여러 장소" : "현재 장소: 미등록";
            }

            if (firstGps is not null)
            {
                try
                {
                    NearbyCandidates = new ObservableCollection<NearbyPlaceCandidateDto>(
                        await _locationResolver.SearchNearbyAsync(MapPickLatitude, MapPickLongitude, 5));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Gallery batch nearby-place search failed.");
                }
            }

            PlaceDialogStatus = "변경할 장소를 선택하세요.";
        }
        finally
        {
            IsPlaceDialogBusy = false;
            NotifyPreviewChanged();
        }
    }

    public Task SearchExistingPlacesAsync()
    {
        var query = ExistingPlaceSearchText.Trim();
        FilteredExistingPlaces = new ObservableCollection<PlacePickerItemDto>(_places
            .Where(place => query.Length == 0
                || place.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || place.Country.Contains(query, StringComparison.OrdinalIgnoreCase)
                || place.City.Contains(query, StringComparison.OrdinalIgnoreCase)
                || place.Address.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(place => place.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(ToPickerItem));
        PlaceDialogStatus = FilteredExistingPlaces.Count == 0 ? "검색 결과가 없습니다." : $"기존 장소 검색 결과 {FilteredExistingPlaces.Count}건";
        return Task.CompletedTask;
    }

    public async Task SearchPlaceSuggestionsAsync()
    {
        var query = PlaceSearchText.Trim();
        if (query.Length < 2)
        {
            PlaceSearchResults = [];
            return;
        }

        IsPlaceDialogBusy = true;
        try
        {
            PlaceSearchResults = new ObservableCollection<PlaceSuggestionDto>(
                await _locationResolver.SuggestPlacesAsync(query));
            PlaceDialogStatus = PlaceSearchResults.Count == 0 ? "검색 결과가 없습니다." : $"검색 결과 {PlaceSearchResults.Count}건";
        }
        finally
        {
            IsPlaceDialogBusy = false;
        }
    }

    public async Task<(double Latitude, double Longitude)?> ResolveSuggestionCoordinatesAsync(PlaceSuggestionDto suggestion)
    {
        if (string.IsNullOrWhiteSpace(suggestion.PlaceId))
        {
            return null;
        }
        var location = await _locationResolver.ResolvePlaceIdAsync(suggestion.PlaceId);
        return location is null ? null : (location.Latitude, location.Longitude);
    }

    public async Task SelectGoogleSuggestionAsync(PlaceSuggestionDto suggestion)
    {
        ClearExternalPlaceSelections();
        SelectedPlaceSuggestion = suggestion;
        if (string.IsNullOrWhiteSpace(suggestion.PlaceId))
        {
            SelectedLocation = new PlaceLocationPreview { DisplayName = suggestion.PrimaryText, Source = PlaceLocationSource.Google };
        }
        else
        {
            var location = await _locationResolver.ResolvePlaceIdAsync(suggestion.PlaceId)
                ?? throw new InvalidOperationException("선택한 장소의 위치를 확인하지 못했습니다.");
            var normalized = PlaceNormalizer.Normalize(location);
            location = location with
            {
                DisplayName = normalized.DisplayName,
                Country = normalized.Country,
                Province = normalized.Province,
                City = normalized.City,
            };
            SelectedLocation = PlaceLocationPreview.FromLocationResult(location, MapPickRadiusMeters);
            MapPickLatitude = location.Latitude;
            MapPickLongitude = location.Longitude;
            RegistrationGpsText = $"{location.Latitude:F6}, {location.Longitude:F6}";
        }
        PlaceDialogStatus = $"Google 장소 선택: {suggestion.PrimaryText}";
        NotifyPreviewChanged();
    }

    public Task SelectNearbyCandidateAsync(NearbyPlaceCandidateDto candidate)
    {
        ClearExternalPlaceSelections();
        SelectedNearbyCandidate = candidate;
        SelectedLocation = PlaceLocationPreview.FromNearby(candidate, MapPickRadiusMeters);
        RegistrationGpsText = $"{candidate.Latitude:F6}, {candidate.Longitude:F6}";
        PlaceDialogStatus = $"주변 장소 선택: {candidate.Name}";
        NotifyPreviewChanged();
        return Task.CompletedTask;
    }

    public async Task SelectExistingPlaceAsync(PlacePickerItemDto place)
    {
        ClearExternalPlaceSelections();
        SelectedExistingPlace = place;
        var dto = await _placeService.GetPlaceAsync(place.Id);
        SelectedLocation = PlaceLocationPreview.FromPlaceDto(dto, PlaceLocationSource.Existing);
        RegistrationGpsText = $"{dto.Latitude:F6}, {dto.Longitude:F6}";
        PlaceDialogStatus = $"기존 장소 선택: {dto.DisplayName}";
        NotifyPreviewChanged();
    }

    public async Task ApplyMapPickAsync(double latitude, double longitude, double radiusMeters)
    {
        ClearExternalPlaceSelections();
        HasMapPickSelection = true;
        MapPickLatitude = latitude;
        MapPickLongitude = longitude;
        MapPickRadiusMeters = Math.Clamp(radiusMeters, 20d, PlaceRadiusExpansionPlanner.MaximumRadiusMeters);
        LocationResult? resolved = null;
        try
        {
            resolved = await _locationResolver.ResolveAsync(latitude, longitude);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gallery batch map reverse-geocode failed.");
        }
        if (resolved is not null)
        {
            var normalized = PlaceNormalizer.Normalize(resolved);
            resolved = resolved with
            {
                DisplayName = normalized.DisplayName,
                Country = normalized.Country,
                Province = normalized.Province,
                City = normalized.City,
            };
        }
        SelectedLocation = PlaceLocationPreview.FromMapPick(latitude, longitude, MapPickRadiusMeters, resolved);
        RegistrationGpsText = $"{latitude:F6}, {longitude:F6}";
        PlaceDialogStatus = $"지도 선택: {SelectedLocation.DisplayName}";
        NotifyPreviewChanged();
    }

    public void CancelPlaceRegistration()
    {
        ResetExternalSelection();
        SelectedLocation = Clone(OriginalLocation);
        PlaceDialogStatus = string.Empty;
        NotifyPreviewChanged();
    }

    public void DiscardMapPickSelection() => CancelPlaceRegistration();

    public async Task<bool> ConfirmPlaceRegistrationAsync()
    {
        if (!CanApplyPlaceChange)
        {
            PlaceDialogStatus = "변경할 장소를 선택하세요.";
            return false;
        }

        IsPlaceDialogBusy = true;
        try
        {
            var target = await ResolveTargetPlaceAsync();
            if (target is null)
            {
                return false;
            }

            TargetPlaceId = target.Id;
            var plan = _workflow.PlanRadius(
                target.Latitude,
                target.Longitude,
                target.Radius,
                BuildRadiusSelections());
            if (plan.ExceedsMaximum)
            {
                PlaceDialogStatus = "선택한 사진을 포함하려면 장소 범위가 허용 한도를 초과합니다. 사진 위치를 확인해 주세요.";
                return false;
            }
            if (plan.NeedsExpansion)
            {
                if (RadiusExpansionPreviewHandler is null
                    || !await RadiusExpansionPreviewHandler(target.DisplayName, plan))
                {
                    PlaceDialogStatus = "장소 변경이 취소되었습니다.";
                    return false;
                }

                MutationStarted?.Invoke("장소 범위를 갱신하고 있습니다", "잠시 기다려 주세요");
                MutationAttempted = true;
                var updated = await _workflow.ExpandExistingRadiusAsync(
                    _placeService,
                    target,
                    plan.ProposedRadiusMeters,
                    (impact, token) => PlaceOverlapPrompt.ConfirmImpactIfNeededAsync(
                        HostXamlRoot,
                        target.DisplayName,
                        impact,
                        token));
                if (updated is null)
                {
                    PlaceDialogStatus = "장소 변경이 취소되었습니다.";
                    return false;
                }
                target = updated;
                RadiusWasChanged = true;
            }
            else
            {
                MutationStarted?.Invoke($"{_selectedItems.Count}장의 장소를 변경하고 있습니다", "잠시 기다려 주세요");
                MutationAttempted = true;
            }

            MutationProgress?.Invoke($"{_selectedItems.Count}장의 장소를 변경하고 있습니다", "잠시 기다려 주세요");
            await _workflow.AssignAsync(GetFileIds(), target.Id, stage =>
            {
                if (stage == GalleryPlaceAssignmentStage.Verifying)
                {
                    MutationProgress?.Invoke("사진첩을 갱신하고 있습니다", "잠시 기다려 주세요");
                }
            });
            TargetPlaceId = target.Id;
            Succeeded = true;
            PlaceDialogStatus = $"{_selectedItems.Count}장의 장소 변경이 완료되었습니다.";
            return true;
        }
        catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            ConflictFailure = true;
            PlaceDialogStatus = "사진 상태가 변경되어 장소를 저장하지 못했습니다. 다시 시도해 주세요.";
            _logger.LogWarning(ex, "Gallery batch place assignment revision conflict.");
            return false;
        }
        catch (Exception ex)
        {
            PlaceDialogStatus = RadiusWasChanged
                ? "장소 범위는 변경되었지만 사진의 장소를 저장하지 못했습니다. 최신 상태를 확인한 뒤 다시 시도해 주세요."
                : "사진의 장소를 저장하지 못했습니다. 다시 시도해 주세요.";
            _logger.LogError(ex, "Gallery batch place assignment failed.");
            return false;
        }
        finally
        {
            IsPlaceDialogBusy = false;
        }
    }

    public async Task TogglePlaceFavoriteAsync(PlacePickerItemDto place)
    {
        var current = await _placeService.GetPlaceAsync(place.Id);
        await _placeService.SetPlaceFavoriteAsync(current, !current.IsFavorite);
        await LoadPlacesAsync();
    }

    public void ClearExternalPlaceSelections()
    {
        SelectedExistingPlace = null;
        ResetExternalSelection();
    }

    private async Task<PlaceDto?> ResolveTargetPlaceAsync()
    {
        if (SelectedExistingPlace is not null)
        {
            return await _placeService.GetPlaceAsync(SelectedExistingPlace.Id);
        }

        LocationResult? location = null;
        if (SelectedPlaceSuggestion is { } suggestion
            && !string.IsNullOrWhiteSpace(suggestion.PlaceId))
        {
            location = await _locationResolver.ResolvePlaceIdAsync(suggestion.PlaceId);
        }
        else if (SelectedNearbyCandidate is { } nearby)
        {
            location = !string.IsNullOrWhiteSpace(nearby.GooglePlaceId)
                ? await _locationResolver.ResolvePlaceIdAsync(nearby.GooglePlaceId)
                : null;
            location ??= new LocationResult
            {
                DisplayName = nearby.Name,
                Address = nearby.Vicinity,
                Latitude = nearby.Latitude,
                Longitude = nearby.Longitude,
                PlaceId = nearby.GooglePlaceId,
            };
        }
        else if (HasMapPickSelection)
        {
            location = await _locationResolver.ResolveAsync(MapPickLatitude, MapPickLongitude)
                ?? new LocationResult
                {
                    DisplayName = SelectedLocation.DisplayName,
                    Latitude = MapPickLatitude,
                    Longitude = MapPickLongitude,
                };
        }

        if (location is null)
        {
            PlaceDialogStatus = "연결할 장소를 선택하세요.";
            return null;
        }
        var normalized = PlaceNormalizer.Normalize(location);
        var matched = await _placeService.MatchPlaceAsync(
            location.Latitude,
            location.Longitude,
            location.PlaceId,
            normalized.CanonicalName);
        if (matched is not null)
        {
            return matched;
        }

        var initialRadius = SelectedLocation.RadiusMeters > 0 ? SelectedLocation.RadiusMeters : DefaultRadiusMeters;
        var plan = _workflow.PlanRadius(
            location.Latitude,
            location.Longitude,
            initialRadius,
            BuildRadiusSelections());
        if (plan.ExceedsMaximum)
        {
            PlaceDialogStatus = "선택한 사진을 포함하려면 장소 범위가 허용 한도를 초과합니다. 사진 위치를 확인해 주세요.";
            return null;
        }
        if (plan.NeedsExpansion
            && (RadiusExpansionPreviewHandler is null
                || !await RadiusExpansionPreviewHandler(normalized.DisplayName, plan)))
        {
            PlaceDialogStatus = "장소 변경이 취소되었습니다.";
            return null;
        }
        var radius = plan.NeedsExpansion ? plan.ProposedRadiusMeters : initialRadius;
        if (!await PlaceOverlapPrompt.ConfirmIfNeededAsync(
                HostXamlRoot,
                _placeService,
                normalized.DisplayName,
                location.Latitude,
                location.Longitude,
                radius))
        {
            PlaceDialogStatus = "장소 변경이 취소되었습니다.";
            return null;
        }

        MutationStarted?.Invoke($"{_selectedItems.Count}장의 장소를 변경하고 있습니다", "잠시 기다려 주세요");
        MutationAttempted = true;
        return await _workflow.CreateTargetPlaceAsync(_placeService, new CreatePlaceRequest
        {
            DisplayName = normalized.DisplayName,
            CanonicalName = normalized.CanonicalName,
            Country = normalized.Country,
            Province = normalized.Province,
            City = normalized.City,
            District = location.District,
            Address = location.Address,
            PostalCode = location.PostalCode,
            GooglePlaceId = location.PlaceId,
            Category = location.PlaceType,
            Latitude = location.Latitude,
            Longitude = location.Longitude,
            Radius = radius,
            IsActive = true,
            ReclassifyMedia = false,
            ReassignFromOtherPlaces = false,
        });
    }

    private IReadOnlyList<PlaceRadiusPhotoSelection> BuildRadiusSelections()
    {
        var selectedByFileId = _selectedItems.ToDictionary(item => item.BackendFileId, StringComparer.Ordinal);
        return _placeStates
            .Where(state => selectedByFileId.ContainsKey(state.FileId))
            .Select(state => new PlaceRadiusPhotoSelection(
                selectedByFileId[state.FileId].MediaId,
                selectedByFileId[state.FileId].FileName,
                state.GpsLat,
                state.GpsLon))
            .ToList();
    }

    private async Task LoadPlacesAsync()
    {
        _places = (await _placeService.GetPlaceListAsync()).Where(place => place.IsActive).ToList();
        RecentPlaces = new ObservableCollection<PlacePickerItemDto>(_places
            .Where(place => place.LastUsedAt.HasValue || place.UsageCount > 0)
            .OrderByDescending(place => place.LastUsedAt)
            .Take(5)
            .Select(ToPickerItem));
        FavoritePlaces = new ObservableCollection<PlacePickerItemDto>(_places.Where(place => place.IsFavorite).Select(ToPickerItem));
        PlaceHierarchy = new ObservableCollection<PlacePickerCountryNode>(BuildHierarchy(_places));
    }

    private void ResetPickerState()
    {
        NearbyCandidates = [];
        PlaceSearchResults = [];
        FilteredExistingPlaces = [];
        ExistingPlaceSearchText = string.Empty;
        PlaceSearchText = string.Empty;
        ResetExternalSelection();
        SelectedExistingPlace = null;
    }

    private void ResetExternalSelection()
    {
        SelectedNearbyCandidate = null;
        SelectedPlaceSuggestion = null;
        HasMapPickSelection = false;
    }

    private IReadOnlyList<string> GetFileIds() => _selectedItems.Select(item => item.BackendFileId).ToList();

    private void NotifyPreviewChanged()
    {
        OnPropertyChanged(nameof(HasOriginalLocation));
        OnPropertyChanged(nameof(HasSelectedLocation));
        OnPropertyChanged(nameof(ShowLocationChangeComparison));
        OnPropertyChanged(nameof(CanApplyPlaceChange));
        PlacePreviewChanged?.Invoke(this, EventArgs.Empty);
    }

    private static PlacePickerItemDto ToPickerItem(PlaceDto place) => new()
    {
        Id = place.Id,
        DisplayName = place.DisplayName,
        Country = place.Country,
        City = place.City,
        CanonicalName = place.CanonicalName,
        IsFavorite = place.IsFavorite,
    };

    private static IReadOnlyList<PlacePickerCountryNode> BuildHierarchy(IReadOnlyList<PlaceDto> places) =>
        places.GroupBy(place => string.IsNullOrWhiteSpace(place.Country) ? "기타" : place.Country)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(country => new PlacePickerCountryNode
            {
                Title = country.Key,
                Regions = country.GroupBy(place => string.IsNullOrWhiteSpace(place.City) ? "기타" : place.City)
                    .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(region => new PlacePickerRegionNode
                    {
                        Title = region.Key,
                        Places = region.OrderBy(place => place.DisplayName, StringComparer.OrdinalIgnoreCase).Select(ToPickerItem).ToList(),
                    }).ToList(),
            }).ToList();

    private static PlaceLocationPreview Clone(PlaceLocationPreview source) => new()
    {
        PlaceId = source.PlaceId,
        GooglePlaceId = source.GooglePlaceId,
        DisplayName = source.DisplayName,
        Country = source.Country,
        Province = source.Province,
        City = source.City,
        District = source.District,
        Address = source.Address,
        Latitude = source.Latitude,
        Longitude = source.Longitude,
        RadiusMeters = source.RadiusMeters,
        Source = source.Source,
    };
}
