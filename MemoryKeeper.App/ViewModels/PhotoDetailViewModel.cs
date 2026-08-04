using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemoryKeeper.App.Maps;
using MemoryKeeper.App.Models;
using MemoryKeeper.App.Services;
using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Domain.Entities;
using MemoryKeeper.Infrastructure.Services.Api;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;

namespace MemoryKeeper.App.ViewModels;

public partial class PhotoDetailViewModel : ObservableObject, IPlaceRegistrationDialogViewModel
{
    private const double DefaultPanelWidth = 360;
    private const double DefaultMapPickRadius = 100;

    private readonly PhotoDetailService _photoDetailService;
    private readonly IGalleryApiRepository _galleryApiRepository;
    private readonly BaseApiClient _apiClient;
    private readonly PlaceService _placeService;
    private readonly PlacePickerService _placePickerService;
    private readonly MediaPlaceAssignmentService _mediaPlaceAssignmentService;
    private readonly TagService _tagService;
    private readonly IThumbnailService _thumbnailService;
    private readonly IShellFileService _shellFileService;
    private readonly IPlaceFocusState _placeFocusState;
    private readonly IPhotoNavigationState _photoNavigationState;
    private readonly ILocationResolver _locationResolver;
    private readonly IMetadataExtractor _metadataExtractor;
    private readonly ISettingRepository _settingRepository;
    private readonly ILogger<PhotoDetailViewModel> _logger;
    private IMapController? _mapController;

    [ObservableProperty] private Guid mediaId;
    [ObservableProperty] private string fileName = string.Empty;
    [ObservableProperty] private string capturedAtText = "-";
    [ObservableProperty] private string placeName = string.Empty;
    [ObservableProperty] private string country = string.Empty;
    [ObservableProperty] private string province = string.Empty;
    [ObservableProperty] private string city = string.Empty;
    [ObservableProperty] private string address = string.Empty;
    [ObservableProperty] private string canonicalName = string.Empty;
    [ObservableProperty] private string googlePlaceIdText = string.Empty;
    [ObservableProperty] private string gpsText = "-";
    [ObservableProperty] private string gpsStatusText = "❌ GPS 없음";
    [ObservableProperty] private string placeStatusText = "미등록";
    [ObservableProperty] private bool hasGps;
    [ObservableProperty] private bool hasPlace;
    [ObservableProperty] private bool emphasizePlaceRegistration = true;
    [ObservableProperty] private bool isFavorite;
    [ObservableProperty] private string favoriteButtonText = "⭐ 즐겨찾기";
    [ObservableProperty] private BitmapImage? photoImage;
    [ObservableProperty] private ObservableCollection<RelatedPhotoItem> relatedPhotos = [];
    [ObservableProperty] private ObservableCollection<PlaceDto> places = [];
    [ObservableProperty] private PlaceDto? selectedPlace;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool hasMapLocation;
    [ObservableProperty] private double? latitude;
    [ObservableProperty] private double? longitude;
    [ObservableProperty] private string originalPath = string.Empty;
    [ObservableProperty] private string absoluteLibraryPath = string.Empty;
    [ObservableProperty] private Guid? placeId;
    [ObservableProperty] private float zoomFactor = 1.0f;
    [ObservableProperty] private ObservableCollection<TagChipItem> tags = [];
    [ObservableProperty] private ObservableCollection<TagChipItem> tagPickerPinnedItems = [];
    [ObservableProperty] private ObservableCollection<TagChipItem> tagPickerRecentItems = [];
    [ObservableProperty] private ObservableCollection<TagChipItem> tagPickerCommonItems = [];
    [ObservableProperty] private ObservableCollection<TagChipItem> tagPickerCandidateItems = [];
    [ObservableProperty] private string tagSearchKeyword = string.Empty;
    [ObservableProperty] private string newTagName = string.Empty;
    [ObservableProperty] private string exifDebugText = string.Empty;
    [ObservableProperty] private string cameraText = "-";
    [ObservableProperty] private string exposureText = "-";
    [ObservableProperty] private string apertureText = "-";
    [ObservableProperty] private string isoText = "-";
    [ObservableProperty] private string lensText = "-";
    [ObservableProperty] private string focalLengthText = "-";
    [ObservableProperty] private string resolutionText = "-";
    [ObservableProperty] private string fileSizeText = "-";
    [ObservableProperty] private string memo = string.Empty;
    [ObservableProperty] private string memoDraft = string.Empty;
    [ObservableProperty] private double panelWidth = DefaultPanelWidth;
    [ObservableProperty] private bool canGoPrevious;
    [ObservableProperty] private bool canGoNext;
    [ObservableProperty] private string playlistPositionText = string.Empty;

    // Place registration dialog state
    [ObservableProperty] private ObservableCollection<NearbyPlaceCandidateDto> nearbyCandidates = [];
    [ObservableProperty] private ObservableCollection<PlaceSuggestionDto> placeSearchResults = [];
    [ObservableProperty] private string placeSearchText = string.Empty;
    [ObservableProperty] private string registrationGpsText = string.Empty;
    [ObservableProperty] private BitmapImage? registrationPreviewImage;
    [ObservableProperty] private string registrationPreviewFileName = string.Empty;
    [ObservableProperty] private NearbyPlaceCandidateDto? selectedNearbyCandidate;
    [ObservableProperty] private PlaceSuggestionDto? selectedPlaceSuggestion;
    [ObservableProperty] private bool isPlaceDialogBusy;
    [ObservableProperty] private string placeDialogStatus = string.Empty;
    [ObservableProperty] private double mapPickLatitude;
    [ObservableProperty] private double mapPickLongitude;
    [ObservableProperty] private double mapPickRadiusMeters = DefaultMapPickRadius;
    [ObservableProperty] private bool hasMapPickSelection;
    [ObservableProperty] private PlaceLocationPreview originalLocation = PlaceLocationPreview.Empty;
    [ObservableProperty] private PlaceLocationPreview selectedLocation = PlaceLocationPreview.Empty;

    // Existing place picker (MK-042T)
    [ObservableProperty] private ObservableCollection<PlacePickerItemDto> recentPlaces = [];
    [ObservableProperty] private ObservableCollection<PlacePickerItemDto> favoritePlaces = [];
    [ObservableProperty] private ObservableCollection<PlacePickerCountryNode> placeHierarchy = [];
    [ObservableProperty] private ObservableCollection<PlacePickerItemDto> filteredExistingPlaces = [];
    [ObservableProperty] private string existingPlaceSearchText = string.Empty;
    [ObservableProperty] private PlacePickerItemDto? selectedExistingPlace;

    public string? CurrentPlaceStatusText => HasPlace
        ? $"현재 장소: {PlaceName} ({PlaceStatusText})"
        : "현재 장소: 위치정보 없음";

    public bool SupportsMapPick => true;

    public XamlRoot? HostXamlRoot { get; set; }

    public bool HasOriginalLocation => !OriginalLocation.IsEmpty;

    public bool HasSelectedLocation => !SelectedLocation.IsEmpty;

    public bool ShowLocationChangeComparison =>
        HasOriginalLocation && HasSelectedLocation && CanApplyPlaceChange;

    public bool CanApplyPlaceChange =>
        PlaceLocationPreview.CanApply(OriginalLocation, SelectedLocation);

    public event EventHandler? PlacePreviewChanged;

    public bool ShowExifDebugButton
#if DEBUG
        => true;
#else
        => false;
#endif

    public event EventHandler? Closed;
    public event EventHandler? OpenMapRequested;
    public event EventHandler? OpenPlaceRegistrationRequested;
    public event EventHandler? OpenTagManagerRequested;
    public event EventHandler? OpenMemoEditorRequested;
    public event EventHandler? OpenMapPickRequested;
    public event EventHandler<string>? ToastRequested;
    public event EventHandler? PlaceRegistered;

    public PhotoDetailViewModel(
        PhotoDetailService photoDetailService,
        IGalleryApiRepository galleryApiRepository,
        BaseApiClient apiClient,
        PlaceService placeService,
        PlacePickerService placePickerService,
        MediaPlaceAssignmentService mediaPlaceAssignmentService,
        TagService tagService,
        IThumbnailService thumbnailService,
        IShellFileService shellFileService,
        IPlaceFocusState placeFocusState,
        IPhotoNavigationState photoNavigationState,
        ILocationResolver locationResolver,
        IMetadataExtractor metadataExtractor,
        ISettingRepository settingRepository,
        ILogger<PhotoDetailViewModel> logger)
    {
        _photoDetailService = photoDetailService;
        _galleryApiRepository = galleryApiRepository;
        _apiClient = apiClient;
        _placeService = placeService;
        _placePickerService = placePickerService;
        _mediaPlaceAssignmentService = mediaPlaceAssignmentService;
        _tagService = tagService;
        _thumbnailService = thumbnailService;
        _shellFileService = shellFileService;
        _placeFocusState = placeFocusState;
        _photoNavigationState = photoNavigationState;
        _locationResolver = locationResolver;
        _metadataExtractor = metadataExtractor;
        _settingRepository = settingRepository;
        _logger = logger;
    }

    public void AttachMap(IMapController mapController)
    {
        DetachMap();
        _mapController = mapController;
        _mapController.Ready += OnMapReady;
        if (_mapController.IsReady)
        {
            _ = SyncMapAsync();
        }
    }

    public void DetachMap()
    {
        if (_mapController is not null)
        {
            _mapController.Ready -= OnMapReady;
        }

        _mapController = null;
    }

    private void OnMapReady(object? sender, EventArgs e) => _ = SyncMapAsync();

    partial void OnHasMapLocationChanged(bool value) => _ = SyncMapAsync();

    partial void OnLatitudeChanged(double? value) => _ = SyncMapAsync();

    partial void OnLongitudeChanged(double? value) => _ = SyncMapAsync();

    [RelayCommand]
    private async Task LoadAsync()
    {
        await LoadPanelWidthAsync();
        var mediaId = _photoNavigationState.FocusMediaId;
        if (mediaId is null)
        {
            StatusMessage = "표시할 사진이 없습니다.";
            return;
        }

        await LoadMediaAsync(mediaId.Value);
    }

    [RelayCommand]
    private async Task LoadMediaAsync(Guid mediaId)
    {
        ZoomFactor = 1.0f;
        await RunBusyAsync(async () =>
        {
            PhotoDetailDto detail;
            try
            {
                var apiDetail = await _galleryApiRepository.GetPhotoAsync(mediaId);
                detail = GalleryBackendMapper.ToPhotoDetail(apiDetail, _apiClient.ApiBaseUrl);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gallery API detail failed. MediaId={MediaId}", mediaId);
                StatusMessage = $"사진 상세를 불러오지 못했습니다. {ex.Message}";
                throw;
            }

            _photoNavigationState.FocusMediaId = mediaId;
            await ApplyDetailAsync(detail);
            RefreshPlaylistUi();
        });
    }

    [RelayCommand]
    private async Task GoPreviousAsync()
    {
        if (!_photoNavigationState.TryGetPrevious(out var previousId))
        {
            return;
        }

        await LoadMediaAsync(previousId);
    }

    [RelayCommand]
    private async Task GoNextAsync()
    {
        if (!_photoNavigationState.TryGetNext(out var nextId))
        {
            return;
        }

        await LoadMediaAsync(nextId);
    }

    [RelayCommand]
    private async Task SelectRelatedAsync(RelatedPhotoItem? item)
    {
        if (item is null)
        {
            return;
        }

        await LoadMediaAsync(item.MediaId);
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync()
    {
        await RunBusyAsync(async () =>
        {
            IsFavorite = await _photoDetailService.ToggleFavoriteAsync(MediaId);
            FavoriteButtonText = IsFavorite ? "⭐ 즐겨찾기 해제" : "⭐ 즐겨찾기";
            StatusMessage = IsFavorite ? "즐겨찾기에 추가했습니다." : "즐겨찾기를 해제했습니다.";
            ToastRequested?.Invoke(this, StatusMessage);
            await RefreshRelatedAsync();
        });
    }

    [RelayCommand]
    private void OpenPlaceRegistration() => OpenPlaceRegistrationRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenTagManager() => OpenTagManagerRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenMemoEditor() => OpenMemoEditorRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenOnMap()
    {
        if (MediaId != Guid.Empty)
        {
            _placeFocusState.FocusMediaId = MediaId;
        }

        if (PlaceId is Guid id)
        {
            _placeFocusState.FocusPlaceId = id;
            _placeFocusState.PendingSearchText = null;
        }
        else
        {
            _placeFocusState.FocusPlaceId = LibraryConstants.UnclassifiedPlaceId;
            _placeFocusState.PendingSearchText = null;
        }

        OpenMapRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenOriginal()
    {
        try
        {
            var path = File.Exists(OriginalPath) ? OriginalPath : AbsoluteLibraryPath;
            _shellFileService.OpenFile(path);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void OpenFileLocation()
    {
        try
        {
            var path = File.Exists(OriginalPath) ? OriginalPath : AbsoluteLibraryPath;
            _shellFileService.OpenFileLocation(path);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task UpdatePlaceAsync()
    {
        if (SelectedPlace is null)
        {
            StatusMessage = "변경할 장소를 선택하세요.";
            return;
        }

        await RunBusyAsync(async () =>
        {
            var detail = await _photoDetailService.UpdatePlaceAsync(MediaId, SelectedPlace.Id);
            await ApplyDetailAsync(detail);
            StatusMessage = $"장소를 '{SelectedPlace.DisplayName}'(으)로 변경했습니다.";
            ToastRequested?.Invoke(this, "위치정보가 등록되었습니다.");
            PlaceRegistered?.Invoke(this, EventArgs.Empty);
            await TryAutoAdvanceAfterPlaceRegisterAsync();
        });
    }

    [RelayCommand]
    private async Task SaveMemoAsync()
    {
        await RunBusyAsync(async () =>
        {
            var detail = await _photoDetailService.UpdateMemoAsync(MediaId, MemoDraft);
            await ApplyDetailAsync(detail);
            StatusMessage = "메모를 저장했습니다.";
            ToastRequested?.Invoke(this, StatusMessage);
        });
    }

    [RelayCommand]
    private async Task DeleteFromLibraryAsync()
    {
        await RunBusyAsync(async () =>
        {
            await _photoDetailService.DeleteFromLibraryAsync(MediaId);
            _thumbnailService.DeleteThumbnail(MediaId);
            _photoNavigationState.RemoveFromPlaylist(MediaId);
            StatusMessage = "MemoryKeeper 라이브러리에서 제거했습니다. 원본/Library 파일은 유지됩니다.";
            Closed?.Invoke(this, EventArgs.Empty);
        });
    }

    [RelayCommand]
    private void ZoomFit() => ZoomFactor = 1.0f;

    [RelayCommand]
    private void ZoomActual() => ZoomFactor = 1.0f;

    [RelayCommand]
    private void ZoomIn() => ZoomFactor = Math.Min(4.0f, ZoomFactor + 0.25f);

    [RelayCommand]
    private void ZoomOut() => ZoomFactor = Math.Max(0.25f, ZoomFactor - 0.25f);

    [RelayCommand]
    private void Close() => Closed?.Invoke(this, EventArgs.Empty);

    public async Task SavePanelWidthAsync()
    {
        var width = Math.Clamp(PanelWidth, 240, 720).ToString(CultureInfo.InvariantCulture);
        var existing = await _settingRepository.GetByKeyAsync(SettingKeys.PhotoDetailPanelWidth);
        if (existing is null)
        {
            await _settingRepository.AddAsync(new Domain.Entities.Setting
            {
                Id = Guid.NewGuid(),
                Key = SettingKeys.PhotoDetailPanelWidth,
                Value = width,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.Value = width;
            existing.UpdatedAt = DateTime.UtcNow;
            await _settingRepository.UpdateAsync(existing);
        }
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
        RegistrationPreviewFileName = FileName;
        RegistrationPreviewImage = PhotoImage;
        RegistrationGpsText = HasGps && Latitude is double lat && Longitude is double lng
            ? $"{lat:F6}, {lng:F6}"
            : string.Empty;
        MapPickLatitude = Latitude ?? 37.5665;
        MapPickLongitude = Longitude ?? 126.9780;
        MapPickRadiusMeters = await LoadMapPickRadiusAsync();

        await LoadPlacePickerDataAsync();
        await InitializeOriginalLocationAsync();
        SelectedLocation = ClonePreview(OriginalLocation);
        NotifyPlacePreviewChanged();

        PlaceDialogStatus = HasGps
            ? "주변 장소를 불러오는 중..."
            : "기존 장소를 선택하거나 검색·지도에서 찾으세요.";

        if (!HasGps || Latitude is null || Longitude is null)
        {
            if (RecentPlaces.Count > 0 || FavoritePlaces.Count > 0 || PlaceHierarchy.Count > 0)
            {
                PlaceDialogStatus = $"등록된 장소 {RecentPlaces.Count + FavoritePlaces.Count}건을 바로 선택할 수 있습니다.";
            }

            NotifyPlacePreviewChanged();
            return;
        }

        IsPlaceDialogBusy = true;
        try
        {
            var nearby = await _locationResolver.SearchNearbyAsync(Latitude.Value, Longitude.Value, 5);
            NearbyCandidates = new ObservableCollection<NearbyPlaceCandidateDto>(nearby);
            // Do not auto-select nearby — Apply stays disabled until the user chooses (MK-052).
            PlaceDialogStatus = NearbyCandidates.Count > 0
                ? $"주변 추천 {NearbyCandidates.Count}건 · 기존 장소 {FavoritePlaces.Count}건"
                : "주변 추천이 없습니다. 기존 장소를 선택하거나 검색하세요.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Nearby search failed for photo detail.");
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
        var results = await _placePickerService.SearchAsync(ExistingPlaceSearchText);
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

    public async Task TogglePlaceFavoriteAsync(PlacePickerItemDto place)
    {
        var updated = await _placeService.SetPlaceFavoriteAsync(place.Id, !place.IsFavorite);
        await LoadPlacePickerDataAsync();
        PlaceDialogStatus = updated.IsFavorite
            ? $"'{updated.DisplayName}'을(를) 즐겨찾기에 추가했습니다."
            : $"'{updated.DisplayName}' 즐겨찾기를 해제했습니다.";
    }

    private async Task LoadPlacePickerDataAsync()
    {
        var pickerData = await _placePickerService.LoadAsync();
        RecentPlaces = new ObservableCollection<PlacePickerItemDto>(pickerData.RecentPlaces);
        FavoritePlaces = new ObservableCollection<PlacePickerItemDto>(pickerData.FavoritePlaces);
        PlaceHierarchy = new ObservableCollection<PlacePickerCountryNode>(pickerData.Hierarchy);
        FilteredExistingPlaces = [];
    }

    public async Task SearchPlaceSuggestionsAsync()
    {
        if (string.IsNullOrWhiteSpace(PlaceSearchText) || PlaceSearchText.Trim().Length < 2)
        {
            PlaceSearchResults = [];
            return;
        }

        IsPlaceDialogBusy = true;
        try
        {
            var results = await _locationResolver.SuggestPlacesAsync(PlaceSearchText.Trim());
            PlaceSearchResults = new ObservableCollection<PlaceSuggestionDto>(results);
            PlaceDialogStatus = results.Count == 0 ? "검색 결과가 없습니다." : $"검색 결과 {results.Count}건";
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

    public void OpenMapPick() => OpenMapPickRequested?.Invoke(this, EventArgs.Empty);

    public async Task ApplyMapPickAsync(double latitude, double longitude, double radiusMeters)
    {
        MapPickLatitude = latitude;
        MapPickLongitude = longitude;
        MapPickRadiusMeters = Math.Clamp(radiusMeters, 20, 2000);
        HasMapPickSelection = true;
        SelectedNearbyCandidate = null;
        SelectedPlaceSuggestion = null;
        SelectedExistingPlace = null;
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
            _logger.LogWarning(ex, "Reverse geocode for map pick preview failed.");
        }

        NotifyPlacePreviewChanged();
    }

    public void CancelPlaceRegistration()
    {
        SelectedNearbyCandidate = null;
        SelectedPlaceSuggestion = null;
        SelectedExistingPlace = null;
        HasMapPickSelection = false;
        SelectedLocation = ClonePreview(OriginalLocation);
        RegistrationGpsText = OriginalLocation.HasCoordinates
            ? $"{OriginalLocation.LatitudeText}, {OriginalLocation.LongitudeText}"
            : HasGps && Latitude is double lat && Longitude is double lng
                ? $"{lat:F6}, {lng:F6}"
                : string.Empty;
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

    public async Task<bool> ConfirmPlaceRegistrationAsync()
    {
        if (MediaId == Guid.Empty)
        {
            PlaceDialogStatus = "등록할 사진이 없습니다.";
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
            previewLat,
            previewLng,
            previewRadius,
            excludeId);
        if (!overlapOk)
        {
            PlaceDialogStatus = "장소 등록이 취소되었습니다.";
            return false;
        }

        IsPlaceDialogBusy = true;
        PlaceDialogStatus = "장소를 등록하는 중...";

        try
        {
            PlaceDto place;
            if (SelectedExistingPlace is not null)
            {
                place = await _placeService.GetPlaceAsync(SelectedExistingPlace.Id);
            }
            else if (SelectedPlaceSuggestion is not null)
            {
                place = await _placeService.CreateOrGetFromGooglePlaceAsync(
                    SelectedPlaceSuggestion.PlaceId,
                    SelectedPlaceSuggestion.PrimaryText,
                    seedLatitude: null,
                    seedLongitude: null);
            }
            else if (SelectedNearbyCandidate is not null)
            {
                place = await _placeService.CreateOrGetFromGooglePlaceAsync(
                    SelectedNearbyCandidate.GooglePlaceId,
                    SelectedNearbyCandidate.Name,
                    SelectedNearbyCandidate.PlaceType,
                    SelectedNearbyCandidate.Latitude,
                    SelectedNearbyCandidate.Longitude);
            }
            else if (HasMapPickSelection)
            {
                place = await CreatePlaceFromMapPickAsync();
            }
            else
            {
                PlaceDialogStatus = "연결할 장소를 선택하세요.";
                return false;
            }

            await _mediaPlaceAssignmentService.AssignAsync(new AssignMediaPlaceRequest
            {
                PlaceId = place.Id,
                MediaIds = [MediaId]
            });
            await _placeService.TouchUsageAsync(place.Id);
            var reclass = await _placeService.ReclassifyMediaAsync(place.Id, reassignFromOtherPlaces: true);

            var detail = await _photoDetailService.GetPhotoDetailAsync(MediaId);
            await ApplyDetailAsync(detail);
            StatusMessage = reclass.AssignedCount > 0
                ? $"위치정보가 등록되었습니다. · 반경 내 {reclass.AssignedCount}장 연결"
                : "위치정보가 등록되었습니다.";
            PlaceDialogStatus = StatusMessage;
            ToastRequested?.Invoke(this, StatusMessage);
            PlaceRegistered?.Invoke(this, EventArgs.Empty);
            await TryAutoAdvanceAfterPlaceRegisterAsync();
            return true;
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

    private async Task InitializeOriginalLocationAsync()
    {
        if (PlaceId is Guid id)
        {
            try
            {
                var place = await _placeService.GetPlaceAsync(id);
                OriginalLocation = PlaceLocationPreview.FromPlaceDto(place, PlaceLocationSource.Original);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load original place for preview.");
            }
        }

        OriginalLocation = PlaceLocationPreview.Empty;
    }

    private void NotifyPlacePreviewChanged()
    {
        OnPropertyChanged(nameof(HasOriginalLocation));
        OnPropertyChanged(nameof(HasSelectedLocation));
        OnPropertyChanged(nameof(ShowLocationChangeComparison));
        OnPropertyChanged(nameof(CanApplyPlaceChange));
        PlacePreviewChanged?.Invoke(this, EventArgs.Empty);
    }

    private static PlaceLocationPreview ClonePreview(PlaceLocationPreview source) =>
        new()
        {
            PlaceId = source.PlaceId,
            GooglePlaceId = source.GooglePlaceId,
            DisplayName = source.DisplayName,
            Country = source.Country,
            Province = source.Province,
            City = source.City,
            Latitude = source.Latitude,
            Longitude = source.Longitude,
            RadiusMeters = source.RadiusMeters,
            Source = source.Source
        };

    public async Task<string> BuildExifDebugTextAsync()
    {
        var path = File.Exists(AbsoluteLibraryPath) ? AbsoluteLibraryPath : OriginalPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            ExifDebugText = "EXIF를 읽을 파일이 없습니다.";
            return ExifDebugText;
        }

        try
        {
            var dump = await _metadataExtractor.DumpTagsAsync(path);
            var builder = new StringBuilder();
            builder.AppendLine($"File: {path}");
            builder.AppendLine($"Tags: {dump.Count}");
            builder.AppendLine();
            foreach (var pair in dump.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                builder.AppendLine($"{pair.Key} = {pair.Value}");
            }

            ExifDebugText = builder.ToString();
            return ExifDebugText;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EXIF dump failed. Path={Path}", path);
            ExifDebugText = $"EXIF 읽기 실패: {ex.Message}";
            return ExifDebugText;
        }
    }

    [RelayCommand]
    private async Task RemoveTagAsync(TagChipItem? tag)
    {
        if (tag is null || MediaId == Guid.Empty)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            await _tagService.RemoveTagsAsync(new RemoveTagRequest
            {
                MediaIds = [MediaId],
                TagIds = [tag.Id]
            });
            await RefreshTagsAsync();
            StatusMessage = $"태그 '{tag.Name}'을(를) 제거했습니다.";
        });
    }

    public async Task LoadTagPickerAsync()
    {
        if (MediaId == Guid.Empty)
        {
            TagPickerPinnedItems = [];
            TagPickerRecentItems = [];
            TagPickerCommonItems = [];
            TagPickerCandidateItems = [];
            return;
        }

        var state = await _tagService.GetTagPickerStateAsync([MediaId], TagSearchKeyword, forRemove: false);
        TagPickerPinnedItems = new ObservableCollection<TagChipItem>(state.PinnedTags.Select(tag => new TagChipItem(tag)));
        TagPickerRecentItems = new ObservableCollection<TagChipItem>(state.RecentTags.Select(tag => new TagChipItem(tag)));
        TagPickerCommonItems = new ObservableCollection<TagChipItem>(state.CommonTags.Select(tag => new TagChipItem(tag)));
        TagPickerCandidateItems = new ObservableCollection<TagChipItem>(state.CandidateTags.Select(tag => new TagChipItem(tag)));
    }

    [RelayCommand]
    private async Task SearchTagPickerAsync() => await LoadTagPickerAsync();

    public async Task AssignTagsFromPickerAsync()
    {
        if (MediaId == Guid.Empty)
        {
            return;
        }

        var tagIds = TagPickerPinnedItems
            .Concat(TagPickerRecentItems)
            .Concat(TagPickerCommonItems)
            .Concat(TagPickerCandidateItems)
            .Where(item => item.IsSelected && !item.IsAssigned)
            .Select(item => item.Id)
            .Distinct()
            .ToList();
        var newName = NewTagName.Trim();
        if (tagIds.Count == 0 && string.IsNullOrWhiteSpace(newName))
        {
            StatusMessage = "추가할 태그를 선택하거나 새 태그를 입력하세요.";
            return;
        }

        await RunBusyAsync(async () =>
        {
            await _tagService.AssignTagsAsync(new AssignTagRequest
            {
                MediaIds = [MediaId],
                TagIds = tagIds,
                NewTagName = string.IsNullOrWhiteSpace(newName) ? null : newName
            });
            NewTagName = string.Empty;
            TagSearchKeyword = string.Empty;
            await RefreshTagsAsync();
            StatusMessage = "태그를 추가했습니다.";
            ToastRequested?.Invoke(this, StatusMessage);
        });
    }

    private async Task<PlaceDto> CreatePlaceFromMapPickAsync()
    {
        LocationResult? resolved = null;
        try
        {
            resolved = await _locationResolver.ResolveAsync(MapPickLatitude, MapPickLongitude);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reverse geocode for map pick failed.");
        }

        var normalized = resolved is null
            ? null
            : PlaceNormalizer.Normalize(resolved);

        return await _placeService.CreatePlaceAsync(new CreatePlaceRequest
        {
            DisplayName = normalized?.DisplayName
                ?? $"지도 선택 {MapPickLatitude:F4},{MapPickLongitude:F4}",
            CanonicalName = normalized?.CanonicalName,
            Country = normalized?.Country ?? string.Empty,
            Province = normalized?.Province ?? string.Empty,
            City = normalized?.City ?? string.Empty,
            Address = resolved?.Address ?? string.Empty,
            PostalCode = resolved?.PostalCode ?? string.Empty,
            GooglePlaceId = resolved?.PlaceId,
            Category = resolved?.PlaceType,
            Latitude = MapPickLatitude,
            Longitude = MapPickLongitude,
            Radius = MapPickRadiusMeters,
            IsActive = true
        });
    }

    private async Task TryAutoAdvanceAfterPlaceRegisterAsync()
    {
        if (!_photoNavigationState.AutoAdvanceAfterPlaceRegister)
        {
            return;
        }

        _photoNavigationState.RemoveFromPlaylist(MediaId);
        RefreshPlaylistUi();
        if (_photoNavigationState.Playlist.Count == 0)
        {
            Closed?.Invoke(this, EventArgs.Empty);
            return;
        }

        await LoadMediaAsync(_photoNavigationState.Playlist[0]);
    }

    private async Task RefreshTagsAsync()
    {
        var mediaTags = await _tagService.GetMediaTagsAsync(MediaId);
        Tags = new ObservableCollection<TagChipItem>(mediaTags.Select(tag => new TagChipItem(tag)));
    }

    private async Task ApplyDetailAsync(PhotoDetailDto detail)
    {
        MediaId = detail.MediaId;
        FileName = detail.FileName;
        CapturedAtText = detail.CapturedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "촬영일 정보 없음";
        PlaceName = string.IsNullOrWhiteSpace(detail.PlaceName) ? "장소 미지정" : detail.PlaceName;
        Country = string.IsNullOrWhiteSpace(detail.Country) ? "-" : detail.Country;
        Province = string.IsNullOrWhiteSpace(detail.Province) ? "-" : detail.Province;
        City = string.IsNullOrWhiteSpace(detail.City) ? "-" : detail.City;
        Address = detail.Address;
        CanonicalName = string.IsNullOrWhiteSpace(detail.CanonicalName) ? "-" : detail.CanonicalName!;
        GooglePlaceIdText = string.IsNullOrWhiteSpace(detail.GooglePlaceId) ? "-" : detail.GooglePlaceId!;
        Latitude = detail.Latitude;
        Longitude = detail.Longitude;
        HasGps = detail.HasGps;
        HasMapLocation = detail.Latitude is not null && detail.Longitude is not null;
        GpsText = HasMapLocation ? $"{detail.Latitude:F5}, {detail.Longitude:F5}" : "-";
        GpsStatusText = HasGps ? "📍 GPS 있음" : "❌ GPS 없음";
        HasPlace = detail.PlaceId is not null;
        PlaceStatusText = HasPlace ? "등록됨" : "미등록";
        EmphasizePlaceRegistration = !HasPlace;
        IsFavorite = detail.IsFavorite;
        FavoriteButtonText = detail.IsFavorite ? "⭐ 즐겨찾기 해제" : "⭐ 즐겨찾기";
        OriginalPath = detail.OriginalPath;
        AbsoluteLibraryPath = detail.AbsoluteLibraryPath;
        PlaceId = detail.PlaceId;
        Memo = detail.Memo;
        MemoDraft = detail.Memo;
        CameraText = FormatPair(detail.CameraMaker, detail.CameraModel);
        ExposureText = string.IsNullOrWhiteSpace(detail.Exposure) ? "-" : detail.Exposure!;
        ApertureText = string.IsNullOrWhiteSpace(detail.FNumber) ? "-" : detail.FNumber!;
        IsoText = string.IsNullOrWhiteSpace(detail.Iso) ? "-" : detail.Iso!;
        LensText = string.IsNullOrWhiteSpace(detail.Lens) ? "-" : detail.Lens!;
        FocalLengthText = string.IsNullOrWhiteSpace(detail.FocalLength) ? "-" : detail.FocalLength!;
        ResolutionText = detail.Width is int w && detail.Height is int h ? $"{w}×{h}" : "-";
        FileSizeText = detail.FileSizeBytes is long bytes ? FormatFileSize(bytes) : "-";
        Tags = new ObservableCollection<TagChipItem>(detail.Tags.Select(tag => new TagChipItem(tag)));

        Places = new ObservableCollection<PlaceDto>(
            (await _placeService.GetPlaceListAsync()).Where(place => place.IsActive));
        SelectedPlace = PlaceId is Guid id
            ? Places.FirstOrDefault(place => place.Id == id)
            : null;

        var imagePath = detail.AbsoluteLibraryPath;
        if (!string.IsNullOrWhiteSpace(imagePath)
            && (imagePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || imagePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
        {
            PhotoImage = new BitmapImage(new Uri(imagePath));
        }
        else
        {
            var localPath = File.Exists(detail.AbsoluteLibraryPath)
                ? detail.AbsoluteLibraryPath
                : detail.OriginalPath;
            PhotoImage = File.Exists(localPath)
                ? new BitmapImage(new Uri(localPath))
                : (!string.IsNullOrWhiteSpace(detail.OriginalPath)
                   && (detail.OriginalPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                       || detail.OriginalPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    ? new BitmapImage(new Uri(detail.OriginalPath))
                    : null);
        }

        RelatedPhotos = new ObservableCollection<RelatedPhotoItem>(
            detail.RelatedPhotos.Select(photo => new RelatedPhotoItem(photo)));
        _ = LoadRelatedThumbnailsAsync(RelatedPhotos);

        await SyncMapAsync();
        StatusMessage = FileName;
    }

    private void RefreshPlaylistUi()
    {
        CanGoPrevious = _photoNavigationState.TryGetPrevious(out _);
        CanGoNext = _photoNavigationState.TryGetNext(out _);
        var playlist = _photoNavigationState.Playlist;
        if (FocusIndex(out var index, out var total))
        {
            PlaylistPositionText = $"{index + 1} / {total}";
        }
        else
        {
            PlaylistPositionText = playlist.Count > 0 ? $"1 / {playlist.Count}" : string.Empty;
        }
    }

    private bool FocusIndex(out int index, out int total)
    {
        index = -1;
        total = _photoNavigationState.Playlist.Count;
        if (FocusMediaIdOrCurrent() is not Guid current || total == 0)
        {
            return false;
        }

        index = _photoNavigationState.Playlist.ToList().IndexOf(current);
        return index >= 0;
    }

    private Guid? FocusMediaIdOrCurrent() => _photoNavigationState.FocusMediaId ?? (MediaId == Guid.Empty ? null : MediaId);

    private async Task RefreshRelatedAsync()
    {
        if (PlaceId is not Guid id)
        {
            RelatedPhotos = [];
            return;
        }

        var related = await _photoDetailService.GetRelatedPhotosAsync(id, MediaId);
        RelatedPhotos = new ObservableCollection<RelatedPhotoItem>(
            related.Select(photo => new RelatedPhotoItem(photo)));
        _ = LoadRelatedThumbnailsAsync(RelatedPhotos);
    }

    private async Task LoadRelatedThumbnailsAsync(IEnumerable<RelatedPhotoItem> items)
    {
        foreach (var item in items)
        {
            try
            {
                var path = await _thumbnailService.GetOrCreateThumbnailAsync(
                    item.MediaId,
                    item.AbsoluteLibraryPath);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    item.ThumbnailImage = new BitmapImage(new Uri(path));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Related thumbnail failed. MediaId={MediaId}", item.MediaId);
            }
        }
    }

    private async Task SyncMapAsync()
    {
        if (_mapController is null)
        {
            return;
        }

        try
        {
            if (!_mapController.IsReady)
            {
                return;
            }

            if (!HasMapLocation || Latitude is null || Longitude is null)
            {
                await _mapController.SetMarkersAsync([]);
                return;
            }

            await _mapController.SetMarkersAsync(
            [
                new MapMarker(MediaId, PlaceName, Latitude.Value, Longitude.Value, PlaceName)
            ]);
            await _mapController.CenterOnAsync(Latitude.Value, Longitude.Value, 14);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to sync photo detail map.");
        }
    }

    private async Task LoadPanelWidthAsync()
    {
        var setting = await _settingRepository.GetByKeyAsync(SettingKeys.PhotoDetailPanelWidth);
        if (setting is not null
            && double.TryParse(setting.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var width))
        {
            PanelWidth = Math.Clamp(width, 240, 720);
        }
    }

    private async Task<double> LoadMapPickRadiusAsync()
    {
        var setting = await _settingRepository.GetByKeyAsync(SettingKeys.MapPickDefaultRadiusMeters);
        if (setting is not null
            && double.TryParse(setting.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var radius))
        {
            return Math.Clamp(radius, 20, 2000);
        }

        return DefaultMapPickRadius;
    }

    private static string FormatPair(string? left, string? right)
    {
        var text = $"{left} {right}".Trim();
        return string.IsNullOrWhiteSpace(text) ? "-" : text;
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024.0:0.#} KB";
        }

        return $"{bytes / (1024.0 * 1024.0):0.##} MB";
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Photo detail operation failed.");
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
