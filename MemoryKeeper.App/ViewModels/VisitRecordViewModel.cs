using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemoryKeeper.App.Maps;
using MemoryKeeper.App.Models;
using MemoryKeeper.App.Services;
using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Infrastructure.Services.Api;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;

namespace MemoryKeeper.App.ViewModels;

public enum VisitRecordSortMode
{
    Recent = 0,
    Name = 1,
    VisitCount = 2
}

public partial class VisitRecordViewModel : ObservableObject
{
    private readonly IGalleryApiRepository _galleryApiRepository;
    private readonly BaseApiClient _apiClient;
    private readonly PhotoDetailService _photoDetailService;
    private readonly IThumbnailService _thumbnailService;
    private readonly ISettingRepository _settingRepository;
    private readonly IPlaceFocusState _placeFocusState;
    private readonly IPhotoNavigationState _photoNavigationState;
    private readonly IPlaceEditorSeedState _placeEditorSeedState;
    private readonly ILogger<VisitRecordViewModel> _logger;
    private readonly DispatcherQueue _dispatcherQueue;
    private IMapController? _mapController;
    private CancellationTokenSource? _suggestCts;
    private CancellationTokenSource? _thumbCts;
    private CancellationTokenSource? _activationCts;
    private bool _suppressMapCamera;
    private bool _activationOwnsFocus;
    private int _mapSyncVersion;
    private readonly SemaphoreSlim _mapSyncLock = new(1, 1);
    private IReadOnlyList<VisitRecordPlaceItem> _allMapItems = [];
    private IReadOnlyList<VisitRecordPlaceItem> _timelineItems = [];
    private VisitRecordPlaceItem? _pendingMapFocus;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<SearchChipItem> searchChips = [];

    [ObservableProperty]
    private ObservableCollection<SearchSuggestionItem> suggestions = [];

    [ObservableProperty]
    private ObservableCollection<string> recentQueries = [];

    [ObservableProperty]
    private bool isSuggestionOpen;

    [ObservableProperty]
    private bool isRecentOpen;

    [ObservableProperty]
    private bool hasNoResults;

    [ObservableProperty]
    private ObservableCollection<VisitRecordYearGroup> yearGroups = [];

    [ObservableProperty]
    private int? selectedYear;

    [ObservableProperty]
    private VisitRecordPlaceItem? selectedPlace;

    [ObservableProperty]
    private Guid? selectedPlaceId;

    [ObservableProperty]
    private Guid? hoveredPlaceId;

    [ObservableProperty]
    private ObservableCollection<VisitPreviewItem> previewPhotos = [];

    [ObservableProperty]
    private VisitRecordSortMode sortMode = VisitRecordSortMode.Recent;

    [ObservableProperty]
    private string statusMessage = "기억나는 단어를 입력해 검색하세요.";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool isMapReady;

    public event EventHandler? OpenGalleryRequested;

    public event EventHandler? OpenPlaceManagementRequested;

    public event EventHandler? BackRequested;

    public VisitRecordViewModel(
        IGalleryApiRepository galleryApiRepository,
        BaseApiClient apiClient,
        PhotoDetailService photoDetailService,
        IThumbnailService thumbnailService,
        ISettingRepository settingRepository,
        IPlaceFocusState placeFocusState,
        IPhotoNavigationState photoNavigationState,
        IPlaceEditorSeedState placeEditorSeedState,
        ILogger<VisitRecordViewModel> logger)
    {
        _galleryApiRepository = galleryApiRepository;
        _apiClient = apiClient;
        _photoDetailService = photoDetailService;
        _thumbnailService = thumbnailService;
        _settingRepository = settingRepository;
        _placeFocusState = placeFocusState;
        _photoNavigationState = photoNavigationState;
        _placeEditorSeedState = placeEditorSeedState;
        _logger = logger;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    [RelayCommand]
    private void GoBack() => BackRequested?.Invoke(this, EventArgs.Empty);

    public void AttachMap(IMapController mapController)
    {
        DetachMap();
        _mapController = mapController;
        _mapController.Ready += OnMapReady;
        _mapController.MarkerClicked += OnMarkerClicked;
        _mapController.MarkerHovered += OnMarkerHovered;
        IsMapReady = mapController.IsReady;
    }

    public void DetachMap()
    {
        if (_mapController is not null)
        {
            _mapController.Ready -= OnMapReady;
            _mapController.MarkerClicked -= OnMarkerClicked;
            _mapController.MarkerHovered -= OnMarkerHovered;
        }

        _mapController = null;
        IsMapReady = false;
    }

    /// <summary>
    /// Single activation path for HOME and TRAVEL_RECORD → Visit Map.
    /// Waits for mapReady, syncs markers, then applies pending focus camera.
    /// </summary>
    public async Task ActivateVisitSurfaceAsync(
        int navigationGeneration,
        VisitMapNavigationSource source,
        bool reloadData,
        CancellationToken cancellationToken = default)
    {
        _activationCts?.Cancel();
        _activationCts?.Dispose();
        _activationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _activationCts.Token;

        if (navigationGeneration != _placeFocusState.NavigationGeneration)
        {
            _logger.LogInformation(
                "stale async result ignored (activate start). RequestedGen={Requested} CurrentGen={Current}",
                navigationGeneration,
                _placeFocusState.NavigationGeneration);
            return;
        }

        if (_mapController is null)
        {
            _logger.LogWarning("ActivateVisitSurface aborted; map controller missing. Gen={Gen}", navigationGeneration);
            return;
        }

        var wantsFocus = _placeFocusState.HasPendingFocus;
        var wantsFilters = _placeFocusState.HasPendingFilters;
        var recoveryCount = 0;

        _activationOwnsFocus = true;
        _suppressMapCamera = true;
        try
        {
            _logger.LogInformation(
                "ActivateVisitSurface start. Source={Source} Gen={Gen} Reload={Reload} WantsFocus={Focus} WantsFilters={Filters} WebViewHintReady={Layout}",
                source,
                navigationGeneration,
                reloadData,
                wantsFocus,
                wantsFilters,
                _mapController.IsHostLayoutReady);

            var apiKeySetting = await _settingRepository.GetByKeyAsync(SettingKeys.GoogleMapsApiKey);
            var apiKey = GoogleMapsApiKeyValidator.NormalizeOrNull(apiKeySetting?.Value);
            if (apiKey is null)
            {
                StatusMessage = "Google API Key가 없거나 형식이 올바르지 않습니다. 설정 → Google API에서 AIza… Key를 저장하세요.";
                IsMapReady = false;
                return;
            }

            await _mapController.EnsureMapReadyAsync(apiKey, forceReload: false, ct);
            IsMapReady = _mapController.IsReady;
            _logger.LogInformation("mapReady confirmed. Gen={Gen} TilesLoaded={Tiles}", navigationGeneration, _mapController.HasTilesLoaded);

            if (ct.IsCancellationRequested || navigationGeneration != _placeFocusState.NavigationGeneration)
            {
                _logger.LogInformation("stale async result ignored after mapReady. Gen={Gen}", navigationGeneration);
                return;
            }

            if (reloadData || _allMapItems.Count == 0 || wantsFilters)
            {
                await LoadAsync();
            }
            else if (wantsFocus)
            {
                ApplyPendingFocusCore(clearWhenMissing: true, consumeFocusState: false);
            }

            if (ct.IsCancellationRequested || navigationGeneration != _placeFocusState.NavigationGeneration)
            {
                _logger.LogInformation("stale async result ignored after data. Gen={Gen}", navigationGeneration);
                return;
            }

            await SyncMapAsync(fitBounds: !wantsFocus);
            _logger.LogInformation("marker sync completed. Gen={Gen} FitBounds={Fit}", navigationGeneration, !wantsFocus);

            await _mapController.NotifyLayoutAsync(ct);
            var tilesOk = _mapController.HasTilesLoaded
                          || await _mapController.WaitUntilTilesLoadedAsync(TimeSpan.FromSeconds(4), ct);
            _logger.LogInformation("tilesloaded wait. Gen={Gen} Ok={Ok}", navigationGeneration, tilesOk);

            // Selection-path recovery must NOT HTML-reload. Only nudge resize/center once.
            if (!tilesOk && recoveryCount == 0)
            {
                recoveryCount = 1;
                _logger.LogInformation(
                    "tile recovery resize only (no HTML reload). Gen={Gen}",
                    navigationGeneration);
                await _mapController.NotifyLayoutAsync(ct);
                if (SelectedPlace is not null
                    && PlaceIdentity.HasValidCoordinates(SelectedPlace.Latitude, SelectedPlace.Longitude))
                {
                    await _mapController.CenterOnAsync(SelectedPlace.Latitude, SelectedPlace.Longitude, 15, ct);
                }

                tilesOk = _mapController.HasTilesLoaded
                          || await _mapController.WaitUntilTilesLoadedAsync(TimeSpan.FromSeconds(2), ct);
            }

            if (ct.IsCancellationRequested || navigationGeneration != _placeFocusState.NavigationGeneration)
            {
                _logger.LogInformation("stale async result ignored before focus. Gen={Gen}", navigationGeneration);
                return;
            }

            _suppressMapCamera = false;
            if (wantsFocus)
            {
                if (SelectedPlace is null)
                {
                    ApplyPendingFocusCore(clearWhenMissing: true, consumeFocusState: false);
                }

                if (SelectedPlace is not null)
                {
                    _logger.LogInformation(
                        "focus requested/applied. Gen={Gen} PlaceId={PlaceId} PlaceName={PlaceName} Lat={Lat} Lon={Lon}",
                        navigationGeneration,
                        SelectedPlace.PlaceId,
                        SelectedPlace.PlaceName,
                        SelectedPlace.Latitude,
                        SelectedPlace.Longitude);
                    await FocusSelectedOnMapAsync(SelectedPlace);
                    StatusMessage = $"'{SelectedPlace.PlaceName}' · 사진 위치로 이동했습니다.";
                }
                else
                {
                    StatusMessage = "방문지도에서 해당 사진/장소를 찾지 못했습니다.";
                    _logger.LogWarning("focus apply failed; place not found. Gen={Gen}", navigationGeneration);
                }

                _placeFocusState.ClearFocus();
            }

            _logger.LogInformation(
                "ActivateVisitSurface done. Source={Source} Gen={Gen} TilesOk={Tiles} Recovery={Recovery}",
                source,
                navigationGeneration,
                tilesOk,
                recoveryCount);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation("ActivateVisitSurface canceled. Gen={Gen}", navigationGeneration);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ActivateVisitSurface failed. Gen={Gen}", navigationGeneration);
            StatusMessage = $"지도를 준비하지 못했습니다. {ex.Message}";
        }
        finally
        {
            _suppressMapCamera = false;
            _activationOwnsFocus = false;
        }
    }

    [RelayCommand]
    private async Task InitializeMapAsync()
    {
        if (_mapController is null)
        {
            return;
        }

        try
        {
            var apiKeySetting = await _settingRepository.GetByKeyAsync(SettingKeys.GoogleMapsApiKey);
            var apiKey = GoogleMapsApiKeyValidator.NormalizeOrNull(apiKeySetting?.Value);
            if (apiKey is null)
            {
                StatusMessage = "Google API Key가 없거나 형식이 올바르지 않습니다. 설정 → Google API에서 AIza… Key를 저장하세요.";
                IsMapReady = false;
                return;
            }

            await _mapController.EnsureMapReadyAsync(apiKey, forceReload: false);
            IsMapReady = _mapController.IsReady;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Visit record map init failed.");
            StatusMessage = $"지도를 초기화하지 못했습니다. {ex.Message}";
        }
    }

    partial void OnSelectedPlaceChanged(VisitRecordPlaceItem? value)
    {
        SelectedPlaceId = value?.PlaceId;
        foreach (var item in _timelineItems)
        {
            item.IsSelected = value is not null && item.PlaceId == value.PlaceId;
        }

        if (value is not null)
        {
            // Do not write FocusPlaceId here — that is navigation state for shell entry,
            // and rewriting it on every click can confuse pending activation.
            _ = LoadPreviewAsync(value);
            if (!_suppressMapCamera)
            {
                _ = FocusSelectedOnMapAsync(value);
            }
        }
        else
        {
            PreviewPhotos = [];
        }
    }

    partial void OnHoveredPlaceIdChanged(Guid? value)
    {
        foreach (var item in _timelineItems)
        {
            item.IsHighlighted = value is Guid id && item.PlaceId == id;
        }

        if (_mapController?.IsReady == true)
        {
            _ = _mapController.HoverMarkerAsync(value);
        }
    }

    partial void OnSortModeChanged(VisitRecordSortMode value) => RebuildYearGroups();

    partial void OnSelectedYearChanged(int? value) => RefreshYearSelectionChrome();

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (!string.IsNullOrWhiteSpace(_placeFocusState.PendingSearchText))
        {
            SearchText = _placeFocusState.PendingSearchText;
            _placeFocusState.PendingSearchText = null;
        }

        await RefreshRecentQueriesAsync();
        await SearchAsync();
    }

    partial void OnSearchTextChanged(string value) => _ = RefreshSuggestionsAsync(value);

    [RelayCommand]
    private async Task SearchAsync()
    {
        await RunBusyAsync(async () =>
        {
            IsSuggestionOpen = false;
            IsRecentOpen = false;

            VisitRecordQueryResult query;
            if (_placeFocusState.PendingSeason is TravelSeason season)
            {
                _placeFocusState.PendingSeason = null;
                _placeFocusState.PendingCountry = null;
                SearchText = string.Empty;
                query = await GalleryBackendBridge.QueryVisitRecordsAsync(
                    _galleryApiRepository,
                    _apiClient.ApiBaseUrl,
                    keyword: season.ToString());
            }
            else if (!string.IsNullOrWhiteSpace(_placeFocusState.PendingCountry))
            {
                var country = _placeFocusState.PendingCountry;
                _placeFocusState.PendingCountry = null;
                SearchText = string.Empty;
                query = await GalleryBackendBridge.QueryVisitRecordsAsync(
                    _galleryApiRepository,
                    _apiClient.ApiBaseUrl,
                    country: country);
            }
            else
            {
                query = await GalleryBackendBridge.QueryVisitRecordsAsync(
                    _galleryApiRepository,
                    _apiClient.ApiBaseUrl,
                    keyword: string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim());
            }

            ApplyQuery(query);
            await RefreshRecentQueriesAsync();
        });
    }

    [RelayCommand]
    private async Task ClearSearchAsync()
    {
        SearchText = string.Empty;
        SearchChips = [];
        Suggestions = [];
        IsSuggestionOpen = false;
        await SearchAsync();
        StatusMessage = "검색 조건을 초기화했습니다.";
    }

    [RelayCommand]
    private async Task ApplySuggestionAsync(SearchSuggestionItem? suggestion)
    {
        if (suggestion is null)
        {
            return;
        }

        SearchText = ReplaceLastToken(SearchText, suggestion.Text);
        IsSuggestionOpen = false;
        await SearchAsync();
    }

    [RelayCommand]
    private async Task ApplyRecentQueryAsync(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        SearchText = query;
        IsRecentOpen = false;
        await SearchAsync();
    }

    [RelayCommand]
    private async Task ShowRecentAsync()
    {
        await RefreshRecentQueriesAsync();
        IsSuggestionOpen = false;
        IsRecentOpen = RecentQueries.Count > 0;
    }

    [RelayCommand]
    private void ExpandAll()
    {
        foreach (var group in YearGroups.Where(g => !g.IsAll))
        {
            group.IsExpanded = true;
        }
    }

    [RelayCommand]
    private void CollapseAll()
    {
        foreach (var group in YearGroups.Where(g => !g.IsAll))
        {
            group.IsExpanded = false;
        }
    }

    [RelayCommand]
    private void ToggleYearGroup(VisitRecordYearGroup? group)
    {
        if (group is null || group.IsAll)
        {
            return;
        }

        // Arrow only expands/collapses the year list — map filter is controlled by title selection.
        group.IsExpanded = !group.IsExpanded;
    }

    [RelayCommand]
    private async Task SelectYearGroupAsync(VisitRecordYearGroup? group)
    {
        if (group is null)
        {
            return;
        }

        if (group.IsAll)
        {
            SelectedYear = null;
            StatusMessage = "전체 장소를 지도에 표시합니다.";
        }
        else
        {
            SelectedYear = group.Year;
            group.IsExpanded = true;
            StatusMessage = $"{group.Year}년 장소 {group.PlaceCount}곳을 지도에 표시합니다.";
        }

        RefreshYearSelectionChrome();
        await SyncMapAsync();
    }

    [RelayCommand]
    private void SortByRecent() => SortMode = VisitRecordSortMode.Recent;

    [RelayCommand]
    private void SortByName() => SortMode = VisitRecordSortMode.Name;

    [RelayCommand]
    private void SortByVisitCount() => SortMode = VisitRecordSortMode.VisitCount;

    [RelayCommand]
    private void SelectPlace(VisitRecordPlaceItem? item)
    {
        if (item is null)
        {
            return;
        }

        SelectedPlace = item;
    }

    [RelayCommand]
    private void OpenPhotoDetail(VisitRecordPlaceItem? item)
    {
        var mediaId = item?.RepresentativeMediaId;
        if (mediaId is null)
        {
            StatusMessage = "이 장소에서 열 수 있는 사진이 없습니다.";
            return;
        }

        SelectedPlace = item;
        _photoNavigationState.RequestOpen(mediaId.Value);
    }

    [RelayCommand]
    private void OpenPreviewPhoto(VisitPreviewItem? item)
    {
        if (item is null)
        {
            return;
        }

        var playlist = (SelectedPlace?.AllPhotos.Count > 0
                ? SelectedPlace.AllPhotos.Select(photo => photo.MediaId)
                : PreviewPhotos.Select(photo => photo.MediaId))
            .Distinct()
            .ToList();
        if (playlist.Count == 0)
        {
            playlist = [item.MediaId];
        }

        _photoNavigationState.RequestOpenViewer(item.MediaId, playlist, "visits");
    }

    [RelayCommand]
    private void OpenGallery()
    {
        if (SelectedPlaceId is Guid placeId)
        {
            _placeFocusState.FocusPlaceId = placeId;
        }

        OpenGalleryRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task ZoomInAsync()
    {
        if (_mapController?.IsReady == true)
        {
            await _mapController.ZoomByAsync(1);
        }
    }

    [RelayCommand]
    private async Task ZoomOutAsync()
    {
        if (_mapController?.IsReady == true)
        {
            await _mapController.ZoomByAsync(-1);
        }
    }

    [RelayCommand]
    private async Task FitAllAsync()
    {
        if (_mapController?.IsReady == true)
        {
            await _mapController.FitMarkersAsync();
        }
    }

    [RelayCommand]
    private async Task FocusSelectedAsync()
    {
        if (SelectedPlace is not null)
        {
            await FocusSelectedOnMapAsync(SelectedPlace);
        }
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync(VisitRecordPlaceItem? item)
    {
        if (item?.RepresentativeMediaId is not Guid mediaId)
        {
            StatusMessage = "즐겨찾기할 대표 사진이 없습니다.";
            return;
        }

        await RunBusyAsync(async () =>
        {
            var next = await _photoDetailService.ToggleFavoriteAsync(mediaId);
            StatusMessage = next ? "즐겨찾기에 추가했습니다." : "즐겨찾기를 해제했습니다.";
            await SearchAsync();
        });
    }

    [RelayCommand]
    private void EditPlace(VisitRecordPlaceItem? item)
    {
        if (item is not null)
        {
            SelectedPlace = item;
            if (item.IsUnclassified)
            {
                _placeEditorSeedState.SeedMediaIds = item.AllPhotos.Select(photo => photo.MediaId).ToList();
                _placeEditorSeedState.SeedLatitude = null;
                _placeEditorSeedState.SeedLongitude = null;
                StatusMessage = $"미분류 사진 {item.PhotoCount}장을 새 장소에 연결합니다. 지도에서 위치를 지정한 뒤 저장하세요.";
            }
            else
            {
                _placeFocusState.FocusPlaceId = item.PlaceId;
            }
        }

        OpenPlaceManagementRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void EditTags(VisitRecordPlaceItem? item)
    {
        if (item is null)
        {
            return;
        }

        SelectedPlace = item;
        StatusMessage = "태그는 Gallery 다중 선택 또는 Photo Detail에서 수정할 수 있습니다.";
        OpenGalleryRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task DeleteFromLibraryAsync(VisitRecordPlaceItem? item)
    {
        if (item?.RepresentativeMediaId is not Guid mediaId)
        {
            StatusMessage = "삭제할 대표 사진이 없습니다.";
            return;
        }

        await RunBusyAsync(async () =>
        {
            await _photoDetailService.DeleteFromLibraryAsync(mediaId);
            _thumbnailService.DeleteThumbnail(mediaId);
            StatusMessage = "대표 사진을 라이브러리에서 제거했습니다.";
            await SearchAsync();
        });
    }

    [RelayCommand]
    private void ChangeRepresentative(VisitRecordPlaceItem? item)
    {
        if (item is null)
        {
            return;
        }

        SelectedPlace = item;
        StatusMessage = "대표사진은 즐겨찾기 우선 규칙으로 자동 선택됩니다. Preview에서 사진을 열어 즐겨찾기를 지정하세요.";
    }

    private void ApplyQuery(VisitRecordQueryResult query)
    {
        _allMapItems = query.AllMapPlaces
            .Select(place => new VisitRecordPlaceItem(place))
            .ToList();

        _timelineItems = query.TimelinePlaces
            .Select(place => new VisitRecordPlaceItem(place))
            .ToList();

        SearchChips = new ObservableCollection<SearchChipItem>(
            query.Chips.Select(chip => new SearchChipItem(chip)));

        RebuildYearGroups();
        HasNoResults = _timelineItems.Count == 0;

        ApplyPendingFocusCore(clearWhenMissing: true, consumeFocusState: !_activationOwnsFocus);

        StatusMessage = HasNoResults
            ? "검색 결과가 없습니다."
            : string.IsNullOrWhiteSpace(SearchText)
                ? $"방문지도 {_timelineItems.Count}곳 / 지도 {_allMapItems.Count}곳"
                : $"'{SearchText.Trim()}' Timeline {_timelineItems.Count}곳 (지도 전체 {_allMapItems.Count}곳 유지)";

        if (!_activationOwnsFocus)
        {
            _ = SyncMapAsync();
        }
        // Thumbnails load lazily on marker/place selection only (no map-entry preload).
    }

    /// <summary>
    /// Applies FocusPlaceId / FocusMediaId when navigating from Photo Viewer / Detail without a full reload.
    /// Prefer <see cref="ActivateVisitSurfaceAsync"/> for shell navigation.
    /// </summary>
    [RelayCommand]
    private async Task ApplyPendingFocusAsync()
    {
        await ActivateVisitSurfaceAsync(
            _placeFocusState.NavigationGeneration,
            _placeFocusState.NavigationSource == VisitMapNavigationSource.Unknown
                ? VisitMapNavigationSource.ShellNav
                : _placeFocusState.NavigationSource,
            reloadData: _allMapItems.Count == 0 || _placeFocusState.HasPendingFilters);
    }

    private bool ApplyPendingFocusCore(bool clearWhenMissing, bool consumeFocusState = true)
    {
        var focusId = _placeFocusState.FocusPlaceId;
        var focusMediaId = _placeFocusState.FocusMediaId;
        var focusName = _placeFocusState.FocusPlaceName?.Trim();
        if (focusId is null && focusMediaId is null && string.IsNullOrWhiteSpace(focusName))
        {
            SelectedPlace = _timelineItems.FirstOrDefault()
                            ?? _allMapItems.FirstOrDefault();
            return false;
        }

        if (consumeFocusState)
        {
            _placeFocusState.ClearFocus();
        }

        VisitRecordPlaceItem? place = null;
        if (focusId is Guid id)
        {
            // Prefer map items (validated GPS) over timeline when PlaceIds match.
            place = PreferMapCoords(
                _allMapItems.FirstOrDefault(item => item.PlaceId == id)
                ?? _timelineItems.FirstOrDefault(item => item.PlaceId == id));
        }

        if (place is null && !string.IsNullOrWhiteSpace(focusName))
        {
            place = PreferMapCoords(
                _allMapItems.FirstOrDefault(item =>
                    string.Equals(item.PlaceName, focusName, StringComparison.OrdinalIgnoreCase))
                ?? _timelineItems.FirstOrDefault(item =>
                    string.Equals(item.PlaceName, focusName, StringComparison.OrdinalIgnoreCase)));
        }

        if (place is null && focusMediaId is Guid mediaId)
        {
            place = PreferMapCoords(
                _allMapItems.FirstOrDefault(item =>
                    item.AllPhotos.Any(photo => photo.MediaId == mediaId)
                    || item.Place.RepresentativeMediaId == mediaId)
                ?? _timelineItems.FirstOrDefault(item =>
                    item.AllPhotos.Any(photo => photo.MediaId == mediaId)
                    || item.Place.RepresentativeMediaId == mediaId));
        }

        if (place is null)
        {
            _logger.LogWarning(
                "Visit map focus miss. FocusId={FocusId} FocusName={FocusName} FocusMediaId={MediaId}",
                focusId,
                focusName,
                focusMediaId);
            if (clearWhenMissing && SelectedPlace is null)
            {
                SelectedPlace = _timelineItems.FirstOrDefault();
            }

            return false;
        }

        _logger.LogInformation(
            "Visit map focus hit. PlaceId={PlaceId} PlaceName={PlaceName} Lat={Lat} Lon={Lon} HasLocation={HasLocation} MapInit=pending",
            place.PlaceId,
            place.PlaceName,
            place.Latitude,
            place.Longitude,
            PlaceIdentity.HasValidCoordinates(place.Latitude, place.Longitude));

        int? year = null;
        if (focusMediaId is Guid mid)
        {
            var photo = place.AllPhotos.FirstOrDefault(item => item.MediaId == mid)
                        ?? place.Place.AllPhotos.FirstOrDefault(item => item.MediaId == mid);
            if (photo is not null && photo.CaptureYear > 0)
            {
                year = photo.CaptureYear;
            }
        }

        if (year is null && place.CaptureYears.Count > 0)
        {
            year = place.CaptureYears[0];
        }

        if (year is int selectedYear)
        {
            SelectedYear = selectedYear;
            ExpandYearGroup(selectedYear);
            var scoped = YearGroups
                .FirstOrDefault(group => group.Year == selectedYear)
                ?.Places.FirstOrDefault(item => item.PlaceId == place.PlaceId);
            SelectedPlace = PreferMapCoords(scoped ?? place) ?? place;
        }
        else
        {
            SelectedPlace = place;
        }

        return true;
    }

    /// <summary>
    /// When a timeline place lacks GPS, reuse the matching map place (same PlaceId / name).
    /// </summary>
    private VisitRecordPlaceItem? PreferMapCoords(VisitRecordPlaceItem? place)
    {
        if (place is null)
        {
            return null;
        }

        if (PlaceIdentity.HasValidCoordinates(place.Latitude, place.Longitude))
        {
            return place;
        }

        var fromMap = _allMapItems.FirstOrDefault(item => item.PlaceId == place.PlaceId)
                      ?? _allMapItems.FirstOrDefault(item =>
                          string.Equals(item.PlaceName, place.PlaceName, StringComparison.OrdinalIgnoreCase));
        if (fromMap is not null
            && PlaceIdentity.HasValidCoordinates(fromMap.Latitude, fromMap.Longitude))
        {
            return fromMap;
        }

        return place;
    }

    private void ExpandYearGroup(int year)
    {
        foreach (var group in YearGroups.Where(g => !g.IsAll))
        {
            group.IsExpanded = group.Year == year;
        }

        RefreshYearSelectionChrome();
    }

    private void RebuildYearGroups()
    {
        var ordered = SortPlaces(_timelineItems).ToList();
        var expandedByYear = YearGroups
            .Where(group => !group.IsAll)
            .ToDictionary(group => group.Year, group => group.IsExpanded);

        // MK-046: a place appears under every capture year that has photos,
        // scoped so previews/counts match that year (not only the latest 8 overall).
        var yearPairs = ordered
            .SelectMany(item =>
            {
                var years = item.CaptureYears.Count > 0
                    ? item.CaptureYears
                    : item.AllPhotos.Select(photo => photo.CaptureYear).Distinct().DefaultIfEmpty(
                        (item.Place.LastCapturedDate ?? item.Place.FirstCapturedDate)?.ToLocalTime().Year ?? 0);
                return years.Select(year =>
                {
                    var scoped = item.ForYear(year);
                    // ForYear() creates a new instance — keep already-loaded thumbnails.
                    scoped.ThumbnailImage = item.ThumbnailImage;
                    return (Year: year, Item: scoped);
                });
            })
            .Where(pair => pair.Year > 0 && pair.Item.PhotoCount > 0)
            .GroupBy(pair => pair.Year)
            .OrderByDescending(group => group.Key)
            .Select(group =>
            {
                var yearGroup = new VisitRecordYearGroup(
                    group.Key,
                    group.Select(pair => pair.Item).DistinctBy(item => item.PlaceId));
                if (expandedByYear.TryGetValue(group.Key, out var wasExpanded))
                {
                    yearGroup.IsExpanded = wasExpanded;
                }

                return yearGroup;
            })
            .ToList();

        var allPlaces = ordered
            .Where(item => !item.IsUnclassified)
            .DistinctBy(item => item.PlaceId)
            .ToList();
        var allGroup = new VisitRecordYearGroup(VisitRecordYearGroup.AllYearsSentinel, allPlaces)
        {
            IsExpanded = false
        };

        var groups = new List<VisitRecordYearGroup> { allGroup };
        groups.AddRange(yearPairs);
        YearGroups = new ObservableCollection<VisitRecordYearGroup>(groups);

        if (SelectedYear is int selected
            && YearGroups.All(group => group.IsAll || group.Year != selected))
        {
            SelectedYear = null;
        }

        RefreshYearSelectionChrome();
    }

    private void RefreshYearSelectionChrome()
    {
        foreach (var group in YearGroups)
        {
            group.IsSelected = group.IsAll
                ? SelectedYear is null
                : SelectedYear == group.Year;
        }
    }

    private IEnumerable<VisitRecordPlaceItem> SortPlaces(IEnumerable<VisitRecordPlaceItem> items)
    {
        return SortMode switch
        {
            VisitRecordSortMode.Name => items.OrderBy(item => item.PlaceName),
            VisitRecordSortMode.VisitCount => items
                .OrderByDescending(item => item.VisitRecordCount)
                .ThenByDescending(item => item.PhotoCount)
                .ThenBy(item => item.PlaceName),
            _ => items
                .OrderByDescending(item => item.Place.LastCapturedDate)
                .ThenBy(item => item.PlaceName)
        };
    }

    private async Task SyncMapAsync(bool fitBounds = true)
    {
        if (_mapController?.IsReady != true)
        {
            return;
        }

        var version = Interlocked.Increment(ref _mapSyncVersion);
        await _mapSyncLock.WaitAsync();
        try
        {
            // A newer sync was requested while we waited — drop this stale pass.
            if (version != _mapSyncVersion)
            {
                return;
            }

            // Re-read filter under the lock so concurrent loads cannot overwrite a year selection.
            IReadOnlyList<VisitRecordPlaceItem> markerItems;
            if (SelectedYear is int year)
            {
                var yearGroup = YearGroups.FirstOrDefault(g => !g.IsAll && g.Year == year);
                markerItems = yearGroup is not null
                    ? yearGroup.Places.Where(item => !item.IsUnclassified).ToList()
                    : Array.Empty<VisitRecordPlaceItem>();
            }
            else
            {
                markerItems = _allMapItems.Where(item => !item.IsUnclassified).ToList();
            }

            var markers = markerItems
                .Where(item => PlaceIdentity.HasValidCoordinates(item.Latitude, item.Longitude))
                .Select(item => new MapMarker(
                item.PlaceId,
                item.PlaceName,
                item.Latitude,
                item.Longitude,
                Info: $"{item.PlaceName}<br/>방문 {item.VisitRecordCount}회 · 사진 {item.PhotoCount}장",
                State: MapMarkerVisualState.Matched,
                Scale: item.MarkerScale,
                IsFavorite: item.HasFavorite,
                IsMatched: true)).ToList();

            // Replace markers entirely (clear + add). Never highlight a subset of a full set.
            await _mapController.SetMarkersAsync(markers);

            if (version != _mapSyncVersion)
            {
                return;
            }

            if (SelectedYear is int selectedYear)
            {
                StatusMessage = $"{selectedYear}년 장소 {markers.Count}곳을 지도에 표시합니다.";
            }
            else
            {
                StatusMessage = $"전체 장소 {markers.Count}곳을 지도에 표시합니다.";
            }

            if (fitBounds && markers.Count > 0)
            {
                await _mapController.FitMarkersAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to sync visit record map markers.");
        }
        finally
        {
            _mapSyncLock.Release();
        }
    }

    private async Task FocusSelectedOnMapAsync(VisitRecordPlaceItem item)
    {
        if (_mapController is null)
        {
            return;
        }

        if (!_mapController.IsReady)
        {
            _pendingMapFocus = item;
            _logger.LogInformation(
                "selection focus pending; map not ready. PlaceId={PlaceId} PlaceName={PlaceName}",
                item.PlaceId,
                item.PlaceName);
            return;
        }

        // Photo panel open can shrink WebView briefly — defer camera, never reload.
        if (!_mapController.IsHostLayoutReady)
        {
            _pendingMapFocus = item;
            _logger.LogInformation(
                "selection focus pending; host too small. PlaceId={PlaceId} SizeHint=layout-not-ready",
                item.PlaceId);
            return;
        }

        var lat = item.Latitude;
        var lon = item.Longitude;
        var placeId = item.PlaceId;

        if (!PlaceIdentity.HasValidCoordinates(lat, lon))
        {
            var fromMap = _allMapItems.FirstOrDefault(m => m.PlaceId == item.PlaceId)
                          ?? _allMapItems.FirstOrDefault(m =>
                              string.Equals(m.PlaceName, item.PlaceName, StringComparison.OrdinalIgnoreCase));
            if (fromMap is not null
                && PlaceIdentity.HasValidCoordinates(fromMap.Latitude, fromMap.Longitude))
            {
                lat = fromMap.Latitude;
                lon = fromMap.Longitude;
                placeId = fromMap.PlaceId;
            }
            else
            {
                _logger.LogInformation(
                    "selection skip center; no location. PlaceId={PlaceId} PlaceName={PlaceName}",
                    item.PlaceId,
                    item.PlaceName);
                StatusMessage = $"'{item.PlaceName}' · 위치 정보가 없습니다.";
                _pendingMapFocus = null;
                return;
            }
        }

        try
        {
            _pendingMapFocus = null;
            _logger.LogInformation(
                "selection map update (no reload/sync). PlaceId={PlaceId} PlaceName={PlaceName} Lat={Lat} Lon={Lon} Zoom=15 HostReady={Host} Tiles={Tiles}",
                placeId,
                item.PlaceName,
                lat,
                lon,
                _mapController.IsHostLayoutReady,
                _mapController.HasTilesLoaded);
            // Single JS path: selectMarker → panTo/setZoom. Do not CenterOn+forceRelayout.
            // Do not SyncMapAsync / EnsureMapReady / Initialize on selection.
            await _mapController.SelectMarkerAsync(placeId, center: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "selection map update failed. PlaceId={PlaceId}", placeId);
            StatusMessage = $"'{item.PlaceName}' · 지도를 표시하지 못했습니다.";
        }
    }

    /// <summary>
    /// Called when WebView layout becomes usable again after photo-panel resize.
    /// Applies a deferred selection camera without HTML reload.
    /// </summary>
    public async Task FlushPendingMapFocusAsync()
    {
        if (_pendingMapFocus is null || _mapController?.IsReady != true || !_mapController.IsHostLayoutReady)
        {
            return;
        }

        var item = _pendingMapFocus;
        _logger.LogInformation(
            "flush pending selection focus. PlaceId={PlaceId} PlaceName={PlaceName}",
            item.PlaceId,
            item.PlaceName);
        await FocusSelectedOnMapAsync(item);
    }

    private async Task LoadPreviewAsync(VisitRecordPlaceItem item)
    {
        _thumbCts?.Cancel();
        _thumbCts?.Dispose();
        _thumbCts = new CancellationTokenSource();
        var token = _thumbCts.Token;

        var photos = item.PreviewPhotos.Count > 0 ? item.PreviewPhotos : item.AllPhotos;
        var previews = photos.Select(photo => new VisitPreviewItem(photo)).ToList();
        PreviewPhotos = new ObservableCollection<VisitPreviewItem>(previews);

        _logger.LogInformation(
            "Visit map place selected. Place={Place}, Photos={Count}",
            item.PlaceName,
            previews.Count);

        try
        {
            foreach (var preview in previews)
            {
                token.ThrowIfCancellationRequested();
                var url = preview.ThumbnailUrl;
                _logger.LogInformation(
                    "Visit preview thumb. BackendFileId={FileId}, Url={Url}",
                    preview.BackendFileId,
                    url);

                if (!HttpImageLoader.IsHttpUrl(url))
                {
                    _logger.LogWarning(
                        "Visit preview missing HTTP ThumbnailUrl. BackendFileId={FileId}",
                        preview.BackendFileId);
                    continue;
                }

                await EnqueueAsync(() =>
                {
                    var bitmap = HttpImageLoader.TryCreate(
                        url,
                        _logger,
                        context: $"VisitPreview:{preview.BackendFileId}");
                    preview.ThumbnailImage = bitmap;
                    if (bitmap is null)
                    {
                        _logger.LogWarning(
                            "Visit preview ThumbnailImage null. BackendFileId={FileId}, Url={Url}",
                            preview.BackendFileId,
                            url);
                    }
                });
            }

            token.ThrowIfCancellationRequested();
            var firstImage = previews.FirstOrDefault(p => p.ThumbnailImage is not null)?.ThumbnailImage;
            if (firstImage is not null)
            {
                await EnqueueAsync(() =>
                {
                    item.ThumbnailImage = firstImage;
                    PropagateThumbnailToYearGroups(item.PlaceId, firstImage);
                });
            }
        }
        catch (OperationCanceledException)
        {
            // Selection changed — ignore.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Visit preview thumbnails failed. Place={Place}", item.PlaceName);
        }
    }

    private void PropagateThumbnailToYearGroups(Guid placeId, BitmapImage image)
    {
        foreach (var group in YearGroups)
        {
            foreach (var place in group.Places)
            {
                if (place.PlaceId == placeId)
                {
                    place.ThumbnailImage = image;
                }
            }
        }
    }

    private async Task RefreshSuggestionsAsync(string text)
    {
        _suggestCts?.Cancel();
        _suggestCts?.Dispose();
        _suggestCts = new CancellationTokenSource();
        var token = _suggestCts.Token;

        try
        {
            await Task.Delay(180, token);
            if (string.IsNullOrWhiteSpace(text))
            {
                await EnqueueAsync(() =>
                {
                    Suggestions = [];
                    IsSuggestionOpen = false;
                });
                return;
            }

            await EnqueueAsync(() =>
            {
                Suggestions = [];
                IsSuggestionOpen = false;
                IsRecentOpen = false;
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Visit record suggestion failed.");
        }
    }

    private Task RefreshRecentQueriesAsync()
    {
        RecentQueries = [];
        return Task.CompletedTask;
    }

    private void OnMapReady(object? sender, EventArgs e)
    {
        IsMapReady = true;
        if (_activationOwnsFocus)
        {
            return;
        }

        _ = SyncMapAsync();
        if (_mapController is not null)
        {
            _ = _mapController.NotifyLayoutAsync();
        }
    }

    private void OnMarkerClicked(object? sender, Guid placeId)
    {
        _suppressMapCamera = true;
        try
        {
            var item = _timelineItems.FirstOrDefault(place => place.PlaceId == placeId)
                       ?? _allMapItems.FirstOrDefault(place => place.PlaceId == placeId);
            if (item is not null)
            {
                // Ensure timeline contains selection visual even if filtered out.
                if (_timelineItems.All(place => place.PlaceId != placeId))
                {
                    StatusMessage = $"'{item.PlaceName}'은(는) 현재 검색 Timeline 밖입니다. 지도에서는 선택됩니다.";
                }

                SelectedPlace = item;
                EnsureYearExpanded(item);
            }
        }
        finally
        {
            _suppressMapCamera = false;
        }
    }

    private void OnMarkerHovered(object? sender, Guid? placeId)
    {
        HoveredPlaceId = placeId;
    }

    private void EnsureYearExpanded(VisitRecordPlaceItem item)
    {
        var year = (item.Place.LastCapturedDate ?? item.Place.FirstCapturedDate)?.Year ?? 0;
        var group = YearGroups.FirstOrDefault(itemGroup => itemGroup.Year == year);
        if (group is not null)
        {
            group.IsExpanded = true;
        }
    }

    private static string ReplaceLastToken(string text, string replacement)
    {
        var trimmed = text.TrimEnd();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return replacement;
        }

        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return replacement;
        }

        parts[^1] = replacement;
        return string.Join(' ', parts);
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
            tcs.SetException(new InvalidOperationException("Failed to enqueue visit record UI update."));
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Visit record operation failed.");
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
