using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemoryKeeper.App.Models;
using MemoryKeeper.App.Services;
using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Infrastructure.Services.Api;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;

namespace MemoryKeeper.App.ViewModels;

public partial class PendingMemoryViewModel : ObservableObject, IPlaceRegistrationDialogViewModel
{
    private readonly MemoryKeeperWriteService _pendingMemoryService;
    private readonly MemoryKeeperPlaceService _placeService;
    private readonly IGalleryApiRepository _galleryApiRepository;
    private readonly ILocationResolver _locationResolver;
    private readonly IThumbnailService _thumbnailService;
    private readonly IPhotoNavigationState _photoNavigationState;
    private readonly ILogger<PendingMemoryViewModel> _logger;
    private readonly DispatcherQueue _dispatcherQueue;
    private CancellationTokenSource? _thumbnailCts;
    private IReadOnlyList<PendingMemoryMediaItem> _mediaPropertySources = [];

    [ObservableProperty]
    private ObservableCollection<PendingMemoryGroupItem> groups = [];

    [ObservableProperty]
    private ObservableCollection<PendingMemoryMediaItem> reclassificationCandidates = [];

    [ObservableProperty]
    private PendingMemoryGroupItem? selectedGroup;

    [ObservableProperty]
    private ObservableCollection<PendingMemoryMediaItem> selectedGroupMedia = [];

    [ObservableProperty]
    private ObservableCollection<PlaceDto> places = [];

    [ObservableProperty]
    private PlaceDto? selectedPlace;

    [ObservableProperty]
    private string statusMessage = "미완성 추억을 불러오세요.";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private ObservableCollection<NearbyPlaceCandidateDto> nearbyCandidates = [];

    [ObservableProperty]
    private ObservableCollection<PlaceSuggestionDto> placeSearchResults = [];

    [ObservableProperty]
    private string placeSearchText = string.Empty;

    [ObservableProperty]
    private string registrationGpsText = string.Empty;

    [ObservableProperty]
    private BitmapImage? registrationPreviewImage;

    [ObservableProperty]
    private string registrationPreviewFileName = string.Empty;

    [ObservableProperty]
    private NearbyPlaceCandidateDto? selectedNearbyCandidate;

    [ObservableProperty]
    private PlaceSuggestionDto? selectedPlaceSuggestion;

    [ObservableProperty]
    private bool isPlaceDialogBusy;

    [ObservableProperty]
    private string placeDialogStatus = string.Empty;

    [ObservableProperty]
    private ObservableCollection<PlacePickerItemDto> recentPlaces = [];

    [ObservableProperty]
    private ObservableCollection<PlacePickerItemDto> favoritePlaces = [];

    [ObservableProperty]
    private ObservableCollection<PlacePickerCountryNode> placeHierarchy = [];

    [ObservableProperty]
    private ObservableCollection<PlacePickerItemDto> filteredExistingPlaces = [];

    [ObservableProperty]
    private string existingPlaceSearchText = string.Empty;

    [ObservableProperty]
    private PlacePickerItemDto? selectedExistingPlace;

    [ObservableProperty]
    private PlaceLocationPreview originalLocation = PlaceLocationPreview.Empty;

    [ObservableProperty]
    private PlaceLocationPreview selectedLocation = PlaceLocationPreview.Empty;

    public string? CurrentPlaceStatusText => null;

    public bool SupportsMapPick => true;

    public XamlRoot? HostXamlRoot { get; set; }

    public bool HasOriginalLocation => !OriginalLocation.IsEmpty;

    public bool HasSelectedLocation => !SelectedLocation.IsEmpty;

    public bool ShowLocationChangeComparison =>
        HasOriginalLocation && HasSelectedLocation && CanApplyPlaceChange;

    public bool CanApplyPlaceChange =>
        PlaceLocationPreview.CanApply(OriginalLocation, SelectedLocation);

    public event EventHandler? PlacePreviewChanged;

    [ObservableProperty]
    private bool hasMapPickSelection;

    [ObservableProperty]
    private double mapPickLatitude = 37.5665;

    [ObservableProperty]
    private double mapPickLongitude = 126.9780;

    [ObservableProperty]
    private double mapPickRadiusMeters = 100;

    [ObservableProperty]
    private bool isGpsSectionSelected;

    [ObservableProperty]
    private ObservableCollection<PendingMemoryMediaItem> activeMediaItems = [];

    public bool HasGpsReclassificationCandidates => ReclassificationCandidates.Count > 0;

    public string GpsSectionSummaryText =>
        ReclassificationCandidates.Count == 0
            ? "해당 사진 없음"
            : $"사진 {ReclassificationCandidates.Count}장 · 우선 확인";

    public string ActiveMediaSectionTitle =>
        IsGpsSectionSelected
            ? "GPS 있음 · 장소 미등록 (체크 해제 시 제외)"
            : "그룹 사진 (체크 해제 시 제외)";

    public int IncludedCount =>
        ActiveMediaItems.Count(item => item.IsIncluded);

    public bool HasSelectionForActions => IncludedCount > 0;

    /// <summary>Pending memories are always PlaceID-null — accent the register button.</summary>
    public bool EmphasizePlaceRegistration => HasSelectionForActions;

    public event EventHandler? OpenPlaceRegistrationRequested;

    public event EventHandler? OpenMemoRequested;

    public event EventHandler? BackRequested;

    public PendingMemoryViewModel(
        MemoryKeeperWriteService pendingMemoryService,
        MemoryKeeperPlaceService placeService,
        IGalleryApiRepository galleryApiRepository,
        ILocationResolver locationResolver,
        IThumbnailService thumbnailService,
        IPhotoNavigationState photoNavigationState,
        ILogger<PendingMemoryViewModel> logger)
    {
        _pendingMemoryService = pendingMemoryService;
        _placeService = placeService;
        _galleryApiRepository = galleryApiRepository;
        _locationResolver = locationResolver;
        _thumbnailService = thumbnailService;
        _photoNavigationState = photoNavigationState;
        _logger = logger;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    public bool TryGetSelectedPhotoCoordinates(out double latitude, out double longitude)
    {
        var candidate = GetPrimarySelectedMedia();
        if (candidate?.Latitude is double lat && candidate.Longitude is double lng)
        {
            latitude = lat;
            longitude = lng;
            return true;
        }

        latitude = 0;
        longitude = 0;
        return false;
    }

    public IReadOnlyList<Guid> GetSelectedMediaIdsForPlaceRegistration()
    {
        var included = ActiveMediaItems
            .Where(item => item.IsIncluded)
            .Select(item => item.MediaId)
            .Distinct()
            .ToList();

        if (included.Count > 0)
        {
            return included;
        }

        if (ActiveMediaItems.Count > 0)
        {
            return ActiveMediaItems.Select(item => item.MediaId).Distinct().ToList();
        }

        return [];
    }

    partial void OnSelectedGroupChanged(PendingMemoryGroupItem? value)
    {
        if (value is not null)
        {
            IsGpsSectionSelected = false;
            SelectedGroupMedia = new ObservableCollection<PendingMemoryMediaItem>(value.MediaItems);
            ActiveMediaItems = SelectedGroupMedia;
            ResubscribeMediaPropertyChanged();
            NotifySelectionChanged();
            _ = LoadThumbnailsAsync(SelectedGroupMedia);
            return;
        }

        if (!IsGpsSectionSelected)
        {
            SelectedGroupMedia = [];
            ActiveMediaItems = [];
            ResubscribeMediaPropertyChanged();
            NotifySelectionChanged();
        }
    }

    [RelayCommand]
    private void SelectGpsSection()
    {
        if (ReclassificationCandidates.Count == 0)
        {
            return;
        }

        IsGpsSectionSelected = true;
        if (SelectedGroup is not null)
        {
            SelectedGroup = null;
        }

        ActiveMediaItems = ReclassificationCandidates;
        ResubscribeMediaPropertyChanged();
        NotifySelectionChanged();
        OnPropertyChanged(nameof(ActiveMediaSectionTitle));
        _ = LoadThumbnailsAsync(ReclassificationCandidates);
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        await RunBusyAsync(LoadCoreAsync);
    }

    [RelayCommand]
    private void OpenPhotoDetail(PendingMemoryMediaItem? item)
    {
        if (item is null)
        {
            return;
        }

        var playlist = SelectedGroupMedia
            .Select(media => media.MediaId)
            .Concat(ReclassificationCandidates.Select(media => media.MediaId))
            .Distinct()
            .ToList();
        if (playlist.Count == 0)
        {
            playlist = [item.MediaId];
        }

        _photoNavigationState.RequestOpenViewer(item.MediaId, playlist, "pending", autoAdvanceAfterPlaceRegister: true);
    }

    [RelayCommand]
    private void OpenSelectedPhotoDetail()
    {
        var item = SelectedGroupMedia.FirstOrDefault(media => media.IsIncluded)
            ?? SelectedGroupMedia.FirstOrDefault()
            ?? ReclassificationCandidates.FirstOrDefault(media => media.IsIncluded)
            ?? ReclassificationCandidates.FirstOrDefault();
        OpenPhotoDetail(item);
    }

    [RelayCommand]
    private void IncludeAll()
    {
        foreach (var item in ActiveMediaItems)
        {
            item.IsIncluded = true;
        }

        NotifySelectionChanged();
    }

    [RelayCommand]
    private void ExcludeAll()
    {
        foreach (var item in ActiveMediaItems)
        {
            item.IsIncluded = false;
        }

        NotifySelectionChanged();
    }

    [RelayCommand]
    private void ToggleInclude(PendingMemoryMediaItem? item)
    {
        if (item is null)
        {
            return;
        }

        item.IsIncluded = !item.IsIncluded;
        NotifySelectionChanged();
    }

    [RelayCommand]
    private void GoBack() => BackRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenPlaceRegistration() =>
        OpenPlaceRegistrationRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenMemo() =>
        OpenMemoRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private async Task ToggleFavoriteAsync()
    {
        var mediaIds = GetSelectedMediaIdsForPlaceRegistration();
        if (mediaIds.Count == 0)
        {
            StatusMessage = "즐겨찾기를 적용할 사진을 선택하세요.";
            return;
        }

        await RunBusyAsync(async () =>
        {
            var favorited = 0;
            foreach (var mediaId in mediaIds)
            {
                var detail = await _galleryApiRepository.GetPhotoAsync(mediaId);
                var updated = await _pendingMemoryService.SetFavoriteAsync(
                    mediaId, !detail.Favorite, detail.MetadataRevision);
                if (updated.Favorite)
                {
                    favorited++;
                }
            }

            StatusMessage = favorited > 0
                ? $"즐겨찾기 상태를 변경했습니다. ({mediaIds.Count}장)"
                : "즐겨찾기를 해제했습니다.";
        });
    }

    public async Task PreparePlaceRegistrationAsync()
    {
        NearbyCandidates = [];
        PlaceSearchResults = [];
        PlaceSearchText = string.Empty;
        ExistingPlaceSearchText = string.Empty;
        FilteredExistingPlaces = [];
        SelectedNearbyCandidate = null;
        SelectedPlaceSuggestion = null;
        SelectedExistingPlace = null;
        HasMapPickSelection = false;
        RegistrationPreviewImage = null;
        RegistrationPreviewFileName = string.Empty;
        RegistrationGpsText = string.Empty;
        OriginalLocation = PlaceLocationPreview.Empty;
        SelectedLocation = PlaceLocationPreview.Empty;
        PlaceDialogStatus = "장소 목록을 불러오는 중...";
        IsPlaceDialogBusy = true;

        try
        {
            await LoadPlacePickerDataAsync();

            var previewSource = SelectedGroupMedia.FirstOrDefault(item => item.IsIncluded)
                ?? SelectedGroupMedia.FirstOrDefault()
                ?? ReclassificationCandidates.FirstOrDefault(item => item.IsIncluded)
                ?? ReclassificationCandidates.FirstOrDefault();

            if (previewSource is not null)
            {
                RegistrationPreviewFileName = previewSource.FileName;
                RegistrationPreviewImage = previewSource.ThumbnailImage;
                if (RegistrationPreviewImage is null)
                {
                    try
                    {
                        var path = await _thumbnailService.GetOrCreateThumbnailAsync(
                            previewSource.MediaId,
                            previewSource.AbsoluteLibraryPath);
                        if (!string.IsNullOrWhiteSpace(path))
                        {
                            RegistrationPreviewImage = new BitmapImage(new Uri(path));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Place registration preview failed.");
                    }
                }
            }

            NotifyPlacePreviewChanged();

            if (!TryGetSelectedPhotoCoordinates(out var latitude, out var longitude))
            {
                MapPickLatitude = 37.5665;
                MapPickLongitude = 126.9780;
                RegistrationGpsText = string.Empty;
                NearbyCandidates = [];
                PlaceDialogStatus = FavoritePlaces.Count > 0 || RecentPlaces.Count > 0
                    ? "GPS가 없습니다. 지도에서 위치를 선택하거나 기존 장소를 고르세요."
                    : "GPS가 없습니다. 지도에서 위치를 선택해 등록하세요.";
                return;
            }

            RegistrationGpsText = $"{latitude:F6}, {longitude:F6}";
            MapPickLatitude = latitude;
            MapPickLongitude = longitude;

            var nearby = await _locationResolver.SearchNearbyAsync(latitude, longitude, 5);
            NearbyCandidates = new ObservableCollection<NearbyPlaceCandidateDto>(nearby);
            PlaceDialogStatus = NearbyCandidates.Count == 0
                ? "주변 추천 장소가 없습니다. 기존 장소를 선택하거나 지도에서 선택하세요."
                : $"가까운 장소 {NearbyCandidates.Count}개 · 기존 장소 {FavoritePlaces.Count}개";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Prepare place registration failed.");
            PlaceDialogStatus = ex.Message;
        }
        finally
        {
            IsPlaceDialogBusy = false;
            NotifyPlacePreviewChanged();
        }
    }

    public async Task SearchExistingPlacesAsync()
    {
        var query = ExistingPlaceSearchText.Trim();
        var results = (await _placeService.GetPlaceListAsync())
            .Where(place => place.IsActive)
            .Where(place => string.IsNullOrWhiteSpace(query)
                            || PlaceMatches(place, query))
            .OrderBy(place => place.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(ToPickerItem)
            .ToList();
        FilteredExistingPlaces = new ObservableCollection<PlacePickerItemDto>(results);
        PlaceDialogStatus = string.IsNullOrWhiteSpace(ExistingPlaceSearchText)
            ? "기존 장소 목록"
            : results.Count == 0
                ? "검색 결과가 없습니다."
                : $"기존 장소 검색 결과 {results.Count}건";
    }

    public async Task SelectExistingPlaceAsync(PlacePickerItemDto place)
    {
        ArgumentNullException.ThrowIfNull(place);
        SelectedExistingPlace = place;
        SelectedNearbyCandidate = null;
        SelectedPlaceSuggestion = null;
        HasMapPickSelection = false;

        try
        {
            var dto = await _placeService.GetPlaceAsync(place.Id);
            SelectedLocation = PlaceLocationPreview.FromPlaceDto(dto, PlaceLocationSource.Existing);
            RegistrationGpsText = SelectedLocation.HasCoordinates
                ? $"{SelectedLocation.LatitudeText}, {SelectedLocation.LongitudeText}"
                : string.Empty;
            PlaceDialogStatus = $"기존 장소 선택: {dto.DisplayName}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Load existing place for preview failed.");
            PlaceDialogStatus = ex.Message;
        }

        NotifyPlacePreviewChanged();
    }

    public void ClearExternalPlaceSelections()
    {
        SelectedNearbyCandidate = null;
        SelectedPlaceSuggestion = null;
        HasMapPickSelection = false;
    }

    public async Task ApplyMapPickAsync(double latitude, double longitude, double radiusMeters)
    {
        MapPickLatitude = latitude;
        MapPickLongitude = longitude;
        MapPickRadiusMeters = Math.Clamp(radiusMeters, 20, 2000);
        HasMapPickSelection = true;
        SelectedExistingPlace = null;
        SelectedNearbyCandidate = null;
        SelectedPlaceSuggestion = null;
        RegistrationGpsText = $"{latitude:F6}, {longitude:F6}";
        SelectedLocation = PlaceLocationPreview.FromMapPick(latitude, longitude, MapPickRadiusMeters);
        PlaceDialogStatus = $"지도 선택: {latitude:F6}, {longitude:F6} · 반경 {MapPickRadiusMeters:0}m";
        NotifyPlacePreviewChanged();

        try
        {
            var resolved = await _locationResolver.ResolveAsync(latitude, longitude);
            if (!HasMapPickSelection
                || Math.Abs(MapPickLatitude - latitude) > 0.00001
                || Math.Abs(MapPickLongitude - longitude) > 0.00001)
            {
                return;
            }

            if (resolved is not null)
            {
                var normalized = PlaceNormalizer.Normalize(resolved);
                SelectedLocation = PlaceLocationPreview.FromMapPick(
                    latitude,
                    longitude,
                    MapPickRadiusMeters,
                    resolved with
                    {
                        DisplayName = normalized.DisplayName,
                        Country = normalized.Country,
                        Province = normalized.Province,
                        City = normalized.City
                    });
                PlaceDialogStatus =
                    $"지도 선택: {SelectedLocation.DisplayName} · {latitude:F6}, {longitude:F6}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reverse geocode failed for map pick preview.");
        }

        NotifyPlacePreviewChanged();
    }

    private async Task<PlaceDto> CreatePlaceFromMapPickAsync(PlaceGeographyFallback geographyFallback)
    {
        LocationResult? resolved = null;
        try
        {
            resolved = await _locationResolver.ResolveAsync(MapPickLatitude, MapPickLongitude);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reverse geocode failed for map pick.");
        }

        var normalized = resolved is null
            ? null
            : PlaceNormalizer.Normalize(resolved);

        return await _placeService.CreatePlaceAsync(new CreatePlaceRequest
        {
            DisplayName = normalized?.DisplayName
                ?? $"지도 선택 {MapPickLatitude:F4},{MapPickLongitude:F4}",
            CanonicalName = normalized?.CanonicalName,
            Country = PlaceNormalizer.NormalizeCountry(resolved?.Country),
            Province = PlaceNormalizer.NormalizeRegion(resolved?.Province),
            City = PlaceNormalizer.NormalizePlace(resolved?.City),
            District = resolved?.District ?? string.Empty,
            Address = resolved?.Address ?? string.Empty,
            PostalCode = resolved?.PostalCode ?? string.Empty,
            GooglePlaceId = resolved?.PlaceId,
            Category = resolved?.PlaceType,
            Latitude = MapPickLatitude,
            Longitude = MapPickLongitude,
            Radius = MapPickRadiusMeters,
            IsActive = true
        }, geographyFallback);
    }

    public async Task TogglePlaceFavoriteAsync(PlacePickerItemDto place)
    {
        var current = await _placeService.GetPlaceAsync(place.Id);
        var updated = await _placeService.SetPlaceFavoriteAsync(current, !place.IsFavorite);
        await LoadPlacePickerDataAsync();
        PlaceDialogStatus = updated.IsFavorite
            ? $"'{updated.DisplayName}'을(를) 즐겨찾기에 추가했습니다."
            : $"'{updated.DisplayName}' 즐겨찾기를 해제했습니다.";
    }

    private async Task LoadPlacePickerDataAsync()
    {
        var places = (await _placeService.GetPlaceListAsync())
            .Where(place => place.IsActive)
            .OrderBy(place => place.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        RecentPlaces = new ObservableCollection<PlacePickerItemDto>(
            places.Where(place => place.LastUsedAt.HasValue || place.UsageCount > 0)
                .OrderByDescending(place => place.LastUsedAt)
                .ThenByDescending(place => place.UpdatedAt)
                .Take(5)
                .Select(ToPickerItem));
        FavoritePlaces = new ObservableCollection<PlacePickerItemDto>(
            places.Where(place => place.IsFavorite).Select(ToPickerItem));
        PlaceHierarchy = new ObservableCollection<PlacePickerCountryNode>(BuildPlaceHierarchy(places));
        FilteredExistingPlaces = [];
    }

    private async Task<PlaceDto> CreateNasPlaceFromProviderAsync(
        string providerPlaceId,
        string? fallbackName,
        string? fallbackType,
        double? seedLatitude,
        double? seedLongitude,
        PlaceGeographyFallback geographyFallback)
    {
        LocationResult? resolved = null;
        try
        {
            resolved = await _locationResolver.ResolvePlaceIdAsync(providerPlaceId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Provider place detail lookup failed. ProviderPlaceId={ProviderPlaceId}", providerPlaceId);
        }

        var latitude = resolved?.Latitude ?? seedLatitude;
        var longitude = resolved?.Longitude ?? seedLongitude;
        var normalized = resolved is null ? null : PlaceNormalizer.Normalize(resolved);
        if (latitude is double lat && longitude is double lon)
        {
            var matched = await _placeService.MatchPlaceAsync(
                lat, lon, providerPlaceId, normalized?.CanonicalName);
            if (matched is not null)
            {
                return matched;
            }
        }

        if (latitude is null || longitude is null)
        {
            throw new InvalidOperationException("선택한 장소의 좌표를 확인할 수 없습니다.");
        }

        return await _placeService.CreatePlaceAsync(new CreatePlaceRequest
        {
            DisplayName = normalized?.DisplayName ?? fallbackName ?? "새 장소",
            CanonicalName = normalized?.CanonicalName ?? fallbackName,
            Address = resolved?.Address ?? string.Empty,
            PostalCode = resolved?.PostalCode ?? string.Empty,
            Country = PlaceNormalizer.NormalizeCountry(resolved?.Country),
            Province = PlaceNormalizer.NormalizeRegion(resolved?.Province),
            City = PlaceNormalizer.NormalizePlace(resolved?.City),
            District = resolved?.District ?? string.Empty,
            Latitude = latitude.Value,
            Longitude = longitude.Value,
            Radius = MapPickRadiusMeters,
            GooglePlaceId = providerPlaceId,
            Category = resolved?.PlaceType ?? fallbackType,
            IsActive = true,
        }, geographyFallback);
    }

    private PlaceGeographyFallback BuildRawGeographyFallback(IReadOnlyCollection<Guid> mediaIds)
    {
        var selectedIds = mediaIds.ToHashSet();
        var photo = ActiveMediaItems
            .Where(item => selectedIds.Contains(item.MediaId))
            .Select(item => item.Media)
            .OrderByDescending(item => new[]
            {
                item.Country,
                item.Province,
                item.City,
                item.District,
                item.RawPlaceName,
            }.Count(value => !string.IsNullOrWhiteSpace(value)))
            .FirstOrDefault();
        return new PlaceGeographyFallback
        {
            Country = photo?.Country?.Trim() ?? string.Empty,
            Province = photo?.Province?.Trim() ?? string.Empty,
            City = photo?.City?.Trim() ?? string.Empty,
            District = photo?.District?.Trim() ?? string.Empty,
            Address = photo?.RawPlaceName?.Trim() ?? string.Empty,
        };
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

    private static IReadOnlyList<PlacePickerCountryNode> BuildPlaceHierarchy(IReadOnlyList<PlaceDto> places) =>
        places
            .GroupBy(place => string.IsNullOrWhiteSpace(place.Country) ? "기타" : place.Country.Trim())
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(country => new PlacePickerCountryNode
            {
                Title = country.Key,
                Regions = country
                    .GroupBy(place => string.IsNullOrWhiteSpace(place.City) ? "기타" : place.City.Trim())
                    .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(region => new PlacePickerRegionNode
                    {
                        Title = region.Key,
                        Places = region.Select(ToPickerItem)
                            .OrderBy(place => place.DisplayName, StringComparer.OrdinalIgnoreCase)
                            .ToList(),
                    })
                    .ToList(),
            })
            .ToList();

    private static bool PlaceMatches(PlaceDto place, string query) =>
        new[]
        {
            place.DisplayName,
            place.CanonicalName,
            place.Country,
            place.Province,
            place.City,
            place.District,
            place.Address,
        }.Any(value => !string.IsNullOrWhiteSpace(value)
                       && value.Contains(query, StringComparison.OrdinalIgnoreCase));

    public async Task SearchPlaceSuggestionsAsync()
    {
        var query = PlaceSearchText?.Trim() ?? string.Empty;
        if (query.Length < 2)
        {
            PlaceSearchResults = [];
            return;
        }

        IsPlaceDialogBusy = true;
        try
        {
            var results = await _locationResolver.SuggestPlacesAsync(query);
            PlaceSearchResults = new ObservableCollection<PlaceSuggestionDto>(results);
            PlaceDialogStatus = results.Count == 0
                ? "검색 결과가 없습니다."
                : $"검색 결과 {results.Count}건";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Place suggestion search failed.");
            PlaceDialogStatus = ex.Message;
            PlaceSearchResults = [];
        }
        finally
        {
            IsPlaceDialogBusy = false;
        }
    }

    public async Task<(double Latitude, double Longitude)?> ResolveSuggestionCoordinatesAsync(PlaceSuggestionDto suggestion)
    {
        ArgumentNullException.ThrowIfNull(suggestion);
        if (string.IsNullOrWhiteSpace(suggestion.PlaceId))
        {
            return null;
        }

        try
        {
            var location = await _locationResolver.ResolvePlaceIdAsync(suggestion.PlaceId);
            if (location is null)
            {
                return null;
            }

            return (location.Latitude, location.Longitude);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Resolve suggestion coordinates failed.");
            return null;
        }
    }

    public async Task SelectGoogleSuggestionAsync(PlaceSuggestionDto suggestion)
    {
        ArgumentNullException.ThrowIfNull(suggestion);
        SelectedPlaceSuggestion = suggestion;
        SelectedNearbyCandidate = null;
        SelectedExistingPlace = null;
        HasMapPickSelection = false;
        PlaceDialogStatus = $"Google 장소 선택: {suggestion.PrimaryText}";

        if (string.IsNullOrWhiteSpace(suggestion.PlaceId))
        {
            SelectedLocation = new PlaceLocationPreview
            {
                DisplayName = suggestion.PrimaryText,
                Source = PlaceLocationSource.Google
            };
            NotifyPlacePreviewChanged();
            return;
        }

        IsPlaceDialogBusy = true;
        try
        {
            var location = await _locationResolver.ResolvePlaceIdAsync(suggestion.PlaceId);
            if (location is null)
            {
                SelectedLocation = new PlaceLocationPreview
                {
                    GooglePlaceId = suggestion.PlaceId,
                    DisplayName = suggestion.PrimaryText,
                    Source = PlaceLocationSource.Google
                };
                PlaceDialogStatus = $"'{suggestion.PrimaryText}' 좌표를 가져오지 못했습니다.";
                NotifyPlacePreviewChanged();
                return;
            }

            var normalized = PlaceNormalizer.Normalize(location);
            SelectedLocation = PlaceLocationPreview.FromLocationResult(
                location with
                {
                    DisplayName = normalized.DisplayName,
                    Country = normalized.Country,
                    Province = normalized.Province,
                    City = normalized.City
                },
                MapPickRadiusMeters,
                PlaceLocationSource.Google);
            RegistrationGpsText = $"{location.Latitude:F6}, {location.Longitude:F6}";
            MapPickLatitude = location.Latitude;
            MapPickLongitude = location.Longitude;
            PlaceDialogStatus =
                $"Google 장소: {SelectedLocation.DisplayName} · {location.Latitude:F6}, {location.Longitude:F6}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Resolve Google suggestion failed.");
            PlaceDialogStatus = $"좌표 조회 실패: {ex.Message}";
        }
        finally
        {
            IsPlaceDialogBusy = false;
            NotifyPlacePreviewChanged();
        }
    }

    public async Task SelectNearbyCandidateAsync(NearbyPlaceCandidateDto candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        SelectedNearbyCandidate = candidate;
        SelectedPlaceSuggestion = null;
        SelectedExistingPlace = null;
        HasMapPickSelection = false;
        SelectedLocation = PlaceLocationPreview.FromNearby(candidate, MapPickRadiusMeters);
        RegistrationGpsText = $"{candidate.Latitude:F6}, {candidate.Longitude:F6}";
        PlaceDialogStatus = $"주변 장소 선택: {candidate.Name}";
        NotifyPlacePreviewChanged();
        await Task.CompletedTask;
    }

    public void CancelPlaceRegistration()
    {
        SelectedNearbyCandidate = null;
        SelectedPlaceSuggestion = null;
        SelectedExistingPlace = null;
        HasMapPickSelection = false;
        SelectedLocation = PlaceLocationPreview.Empty;
        RegistrationGpsText = string.Empty;
        PlaceDialogStatus = string.Empty;
        NotifyPlacePreviewChanged();
    }

    public void DiscardMapPickSelection()
    {
        if (!HasMapPickSelection && SelectedLocation.Source != PlaceLocationSource.MapPick)
        {
            return;
        }

        CancelPlaceRegistration();
    }

    private void NotifyPlacePreviewChanged()
    {
        OnPropertyChanged(nameof(HasOriginalLocation));
        OnPropertyChanged(nameof(HasSelectedLocation));
        OnPropertyChanged(nameof(ShowLocationChangeComparison));
        OnPropertyChanged(nameof(CanApplyPlaceChange));
        PlacePreviewChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<bool> ConfirmPlaceRegistrationAsync()
    {
        var mediaIds = GetSelectedMediaIdsForPlaceRegistration();
        if (mediaIds.Count == 0)
        {
            PlaceDialogStatus = "등록할 사진을 선택하세요.";
            return false;
        }

        if (!CanApplyPlaceChange)
        {
            PlaceDialogStatus = "변경할 장소를 선택하세요.";
            return false;
        }

        var previewLat = SelectedLocation.Latitude ?? MapPickLatitude;
        var previewLng = SelectedLocation.Longitude ?? MapPickLongitude;
        var previewRadius = SelectedLocation.RadiusMeters > 0
            ? SelectedLocation.RadiusMeters
            : MapPickRadiusMeters;
        var excludeId = SelectedExistingPlace?.Id ?? SelectedLocation.PlaceId;

        var overlapOk = await PlaceOverlapPrompt.ConfirmIfNeededAsync(
            HostXamlRoot,
            _placeService,
            SelectedLocation.DisplayName,
            previewLat,
            previewLng,
            previewRadius,
            excludeId);
        if (!overlapOk)
        {
            PlaceDialogStatus = "장소 등록이 취소되었습니다.";
            return false;
        }

        string? googlePlaceId = null;
        string? fallbackName = null;
        string? fallbackType = null;
        Guid? existingPlaceId = null;

        if (SelectedExistingPlace is not null)
        {
            existingPlaceId = SelectedExistingPlace.Id;
        }
        else if (SelectedPlaceSuggestion is not null)
        {
            googlePlaceId = SelectedPlaceSuggestion.PlaceId;
            fallbackName = SelectedPlaceSuggestion.PrimaryText;
        }
        else if (SelectedNearbyCandidate is not null)
        {
            googlePlaceId = SelectedNearbyCandidate.GooglePlaceId;
            fallbackName = SelectedNearbyCandidate.Name;
            fallbackType = SelectedNearbyCandidate.PlaceType;
        }

        if (existingPlaceId is null
            && string.IsNullOrWhiteSpace(googlePlaceId)
            && !HasMapPickSelection)
        {
            PlaceDialogStatus = "연결할 장소를 선택하세요.";
            return false;
        }

        IsPlaceDialogBusy = true;
        PlaceDialogStatus = "장소를 등록하는 중...";
        var geographyFallback = BuildRawGeographyFallback(mediaIds);

        try
        {
            PlaceDto place;
            if (existingPlaceId is Guid placeId)
            {
                place = await _placeService.GetPlaceAsync(placeId);
            }
            else if (!string.IsNullOrWhiteSpace(googlePlaceId))
            {
                double? seedLatitude = null;
                double? seedLongitude = null;
                if (SelectedNearbyCandidate is not null)
                {
                    seedLatitude = SelectedNearbyCandidate.Latitude;
                    seedLongitude = SelectedNearbyCandidate.Longitude;
                }

                place = await CreateNasPlaceFromProviderAsync(
                    googlePlaceId,
                    fallbackName,
                    fallbackType,
                    seedLatitude,
                    seedLongitude,
                    geographyFallback);
            }
            else if (HasMapPickSelection)
            {
                place = await CreatePlaceFromMapPickAsync(geographyFallback);
            }
            else
            {
                PlaceDialogStatus = "연결할 장소를 선택하세요.";
                return false;
            }

            var result = await _pendingMemoryService.AssignPlaceAsync(new AssignMediaPlaceRequest
            {
                PlaceId = place.Id,
                MediaIds = mediaIds
            });

            var reclass = await _placeService.ReclassifyMediaAsync(place.Id, reassignFromOtherPlaces: true);

            StatusMessage = "장소가 등록되었습니다.";
            PlaceDialogStatus = reclass.AssignedCount > 0
                ? $"장소 '{place.DisplayName}'에 선택 {result.UpdatedCount}장 · 반경 내 {reclass.AssignedCount}장 연결"
                : $"장소 '{place.DisplayName}'에 {result.UpdatedCount}장을 연결했습니다.";
            await LoadCoreAsync();
            return true;
        }
        catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            _logger.LogWarning(ex, "Pending place assignment revision conflict.");
            await LoadCoreAsync();
            PlaceDialogStatus = "다른 곳에서 사진 정보가 변경되었습니다. 최신 정보를 다시 불러왔습니다.";
            StatusMessage = PlaceDialogStatus;
            return false;
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Confirm pending place registration API request failed.");
            PlaceDialogStatus = ApiErrorClassifier.ToUserMessage(ex, "사진 또는 장소를 찾을 수 없습니다.");
            StatusMessage = PlaceDialogStatus;
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Confirm place registration failed.");
            PlaceDialogStatus = ex.Message;
            StatusMessage = ex.Message;
            return false;
        }
        finally
        {
            IsPlaceDialogBusy = false;
        }
    }

    [RelayCommand]
    private async Task AssignPlaceAsync()
    {
        if (SelectedPlace is null)
        {
            StatusMessage = "등록할 장소를 선택하세요.";
            return;
        }

        var mediaIds = GetSelectedMediaIdsForPlaceRegistration().ToList();

        if (mediaIds.Count == 0)
        {
            StatusMessage = "등록할 사진을 선택하세요.";
            return;
        }

        var place = SelectedPlace;
        await RunBusyAsync(async () =>
        {
            var result = await _pendingMemoryService.AssignPlaceAsync(new AssignMediaPlaceRequest
            {
                PlaceId = place.Id,
                MediaIds = mediaIds
            });

            StatusMessage = "장소가 등록되었습니다.";
            _ = result;
            await LoadCoreAsync();
        });
    }

    private async Task LoadCoreAsync()
    {
        CancelThumbnailLoading();

        var overview = await _pendingMemoryService.GetPendingMemoriesAsync();
        var placeList = await _placeService.GetPlaceListAsync();

        Places = new ObservableCollection<PlaceDto>(
            placeList
                .Where(place => place.IsActive)
                .OrderByDescending(place => place.IsFavorite)
                .ThenByDescending(place => place.LastUsedAt ?? DateTime.MinValue)
                .ThenBy(place => place.DisplayName));

        var groupItems = overview.Groups
            .Select(group => new PendingMemoryGroupItem(group))
            .ToList();

        Groups = new ObservableCollection<PendingMemoryGroupItem>(groupItems);
        ReclassificationCandidates = new ObservableCollection<PendingMemoryMediaItem>(
            overview.ReclassificationCandidates.Select(item => new PendingMemoryMediaItem(item)));

        if (ReclassificationCandidates.Count > 0)
        {
            IsGpsSectionSelected = true;
            SelectedGroup = null;
            ActiveMediaItems = ReclassificationCandidates;
            _ = LoadThumbnailsAsync(ReclassificationCandidates);
        }
        else
        {
            IsGpsSectionSelected = false;
            SelectedGroup = groupItems.FirstOrDefault();
            if (SelectedGroup is null)
            {
                ActiveMediaItems = [];
            }
        }

        ResubscribeMediaPropertyChanged();
        NotifySelectionChanged();
        OnPropertyChanged(nameof(HasGpsReclassificationCandidates));
        OnPropertyChanged(nameof(GpsSectionSummaryText));
        OnPropertyChanged(nameof(ActiveMediaSectionTitle));

        if (SelectedPlace is not null)
        {
            SelectedPlace = Places.FirstOrDefault(place => place.Id == SelectedPlace.Id);
        }

        if (string.IsNullOrWhiteSpace(StatusMessage) || !StatusMessage.Contains("등록되었습니다"))
        {
            StatusMessage =
                $"GPS·장소미등록 {ReclassificationCandidates.Count}장 · 미완성 그룹 {groupItems.Count}개";
        }
    }

    private PendingMemoryItemDto? GetPrimarySelectedMedia()
    {
        return ActiveMediaItems
                   .Where(item => item.IsIncluded)
                   .Select(item => item.Media)
                   .FirstOrDefault(media => media.Latitude is not null && media.Longitude is not null)
               ?? ActiveMediaItems
                   .Select(item => item.Media)
                   .FirstOrDefault(media => media.Latitude is not null && media.Longitude is not null)
               ?? ReclassificationCandidates
                   .Where(item => item.IsIncluded)
                   .Select(item => item.Media)
                   .FirstOrDefault(media => media.Latitude is not null && media.Longitude is not null)
               ?? SelectedGroupMedia
                   .Select(item => item.Media)
                   .FirstOrDefault(media => media.Latitude is not null && media.Longitude is not null);
    }

    private void ResubscribeMediaPropertyChanged()
    {
        UnsubscribeMediaPropertyChanged();
        SubscribeMediaPropertyChanged(
            SelectedGroupMedia.Concat(ReclassificationCandidates));
    }

    private void SubscribeMediaPropertyChanged(IEnumerable<PendingMemoryMediaItem> items)
    {
        _mediaPropertySources = items.ToList();
        foreach (var item in _mediaPropertySources)
        {
            item.PropertyChanged += OnMediaItemPropertyChanged;
        }
    }

    private void UnsubscribeMediaPropertyChanged()
    {
        foreach (var item in _mediaPropertySources)
        {
            item.PropertyChanged -= OnMediaItemPropertyChanged;
        }

        _mediaPropertySources = [];
    }

    private void OnMediaItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PendingMemoryMediaItem.IsIncluded))
        {
            NotifySelectionChanged();
        }
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(IncludedCount));
        OnPropertyChanged(nameof(HasSelectionForActions));
        OnPropertyChanged(nameof(EmphasizePlaceRegistration));
    }

    private async Task LoadThumbnailsAsync(IEnumerable<PendingMemoryMediaItem> items)
    {
        CancelThumbnailLoading();
        _thumbnailCts = new CancellationTokenSource();
        var token = _thumbnailCts.Token;

        try
        {
            foreach (var item in items)
            {
                token.ThrowIfCancellationRequested();
                item.IsThumbnailLoading = true;

                try
                {
                    if (Uri.TryCreate(item.AbsoluteLibraryPath, UriKind.Absolute, out var remote)
                        && remote.Scheme is "http" or "https")
                    {
                        await EnqueueAsync(() =>
                        {
                            item.ThumbnailImage = HttpImageLoader.TryCreate(
                                item.AbsoluteLibraryPath,
                                _logger,
                                context: $"Pending:{item.MediaId:N}");
                        });
                        continue;
                    }

                    var path = await _thumbnailService.GetOrCreateThumbnailAsync(
                        item.MediaId,
                        item.AbsoluteLibraryPath,
                        token);

                    if (string.IsNullOrWhiteSpace(path))
                    {
                        continue;
                    }

                    await EnqueueAsync(() =>
                    {
                        item.ThumbnailImage = new BitmapImage(new Uri(path));
                    });
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Pending thumbnail load failed. MediaId={MediaId}", item.MediaId);
                }
                finally
                {
                    item.IsThumbnailLoading = false;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when reloading.
        }
    }

    private void CancelThumbnailLoading()
    {
        if (_thumbnailCts is null)
        {
            return;
        }

        _thumbnailCts.Cancel();
        _thumbnailCts.Dispose();
        _thumbnailCts = null;
    }

    private Task EnqueueAsync(Action action)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    action();
                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }))
        {
            tcs.SetException(new InvalidOperationException("Failed to enqueue pending memory UI update."));
        }

        return tcs.Task;
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await action();
        }
        catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            _logger.LogWarning(ex, "Pending operation revision conflict.");
            await LoadCoreAsync();
            StatusMessage = "다른 곳에서 사진 정보가 변경되었습니다. 최신 정보를 다시 불러왔습니다.";
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Pending memory API operation failed.");
            StatusMessage = ApiErrorClassifier.ToUserMessage(ex, "요청한 사진 또는 장소를 찾을 수 없습니다.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pending memory operation failed.");
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
