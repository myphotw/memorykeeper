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
    private readonly VisitRecordQueryService _visitRecordQueryService;
    private readonly MemorySearchService _memorySearchService;
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
    private bool _suppressMapCamera;
    private int _mapSyncVersion;
    private readonly SemaphoreSlim _mapSyncLock = new(1, 1);
    private IReadOnlyList<VisitRecordPlaceItem> _allMapItems = [];
    private IReadOnlyList<VisitRecordPlaceItem> _timelineItems = [];

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
        VisitRecordQueryService visitRecordQueryService,
        MemorySearchService memorySearchService,
        PhotoDetailService photoDetailService,
        IThumbnailService thumbnailService,
        ISettingRepository settingRepository,
        IPlaceFocusState placeFocusState,
        IPhotoNavigationState photoNavigationState,
        IPlaceEditorSeedState placeEditorSeedState,
        ILogger<VisitRecordViewModel> logger)
    {
        _visitRecordQueryService = visitRecordQueryService;
        _memorySearchService = memorySearchService;
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

    partial void OnSearchTextChanged(string value) => _ = RefreshSuggestionsAsync(value);

    partial void OnSelectedPlaceChanged(VisitRecordPlaceItem? value)
    {
        SelectedPlaceId = value?.PlaceId;
        foreach (var item in _timelineItems)
        {
            item.IsSelected = value is not null && item.PlaceId == value.PlaceId;
        }

        if (value is not null)
        {
            _placeFocusState.FocusPlaceId = value.PlaceId;
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

            await _mapController.InitializeAsync(apiKey);
            IsMapReady = _mapController.IsReady;
            await SyncMapAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Visit record map init failed.");
            StatusMessage = $"지도를 초기화하지 못했습니다. {ex.Message}";
        }
    }

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
                query = await _visitRecordQueryService.QueryForSeasonAsync(season);
            }
            else if (!string.IsNullOrWhiteSpace(_placeFocusState.PendingCountry))
            {
                var country = _placeFocusState.PendingCountry;
                _placeFocusState.PendingCountry = null;
                SearchText = string.Empty;
                query = await _visitRecordQueryService.QueryForCountryAsync(country);
            }
            else
            {
                MemorySearchRequest? timelineRequest = string.IsNullOrWhiteSpace(SearchText)
                    ? null
                    : new MemorySearchRequest { SearchText = SearchText.Trim() };
                query = await _visitRecordQueryService.QueryAsync(timelineRequest);
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

        ApplyPendingFocusCore(clearWhenMissing: true);

        StatusMessage = HasNoResults
            ? "검색 결과가 없습니다."
            : string.IsNullOrWhiteSpace(SearchText)
                ? $"방문지도 {_timelineItems.Count}곳 / 지도 {_allMapItems.Count}곳"
                : $"'{SearchText.Trim()}' Timeline {_timelineItems.Count}곳 (지도 전체 {_allMapItems.Count}곳 유지)";

        _ = SyncMapAsync();
        _ = LoadTimelineThumbnailsAsync(_timelineItems);
    }

    /// <summary>
    /// Applies FocusPlaceId / FocusMediaId when navigating from Photo Viewer / Detail without a full reload.
    /// </summary>
    [RelayCommand]
    private async Task ApplyPendingFocusAsync()
    {
        if (!string.IsNullOrWhiteSpace(_placeFocusState.PendingSearchText)
            || _placeFocusState.PendingSeason is not null
            || !string.IsNullOrWhiteSpace(_placeFocusState.PendingCountry))
        {
            await LoadAsync();
            return;
        }

        if (_placeFocusState.FocusPlaceId is null && _placeFocusState.FocusMediaId is null)
        {
            return;
        }

        if (_allMapItems.Count == 0 && _timelineItems.Count == 0)
        {
            await LoadAsync();
            return;
        }

        if (!ApplyPendingFocusCore(clearWhenMissing: true))
        {
            StatusMessage = "방문지도에서 해당 사진/장소를 찾지 못했습니다.";
            return;
        }

        await SyncMapAsync();
        if (SelectedPlace is not null)
        {
            await FocusSelectedOnMapAsync(SelectedPlace);
            StatusMessage = $"'{SelectedPlace.PlaceName}' · 사진 위치로 이동했습니다.";
        }
    }

    private bool ApplyPendingFocusCore(bool clearWhenMissing)
    {
        var focusId = _placeFocusState.FocusPlaceId;
        var focusMediaId = _placeFocusState.FocusMediaId;
        if (focusId is null && focusMediaId is null)
        {
            SelectedPlace = _timelineItems.FirstOrDefault()
                            ?? _allMapItems.FirstOrDefault();
            return false;
        }

        _placeFocusState.FocusPlaceId = null;
        _placeFocusState.FocusMediaId = null;

        VisitRecordPlaceItem? place = null;
        if (focusId is Guid id)
        {
            place = _timelineItems.FirstOrDefault(item => item.PlaceId == id)
                    ?? _allMapItems.FirstOrDefault(item => item.PlaceId == id);
        }

        if (place is null && focusMediaId is Guid mediaId)
        {
            place = _allMapItems.FirstOrDefault(item =>
                        item.AllPhotos.Any(photo => photo.MediaId == mediaId)
                        || item.Place.RepresentativeMediaId == mediaId)
                    ?? _timelineItems.FirstOrDefault(item =>
                        item.AllPhotos.Any(photo => photo.MediaId == mediaId)
                        || item.Place.RepresentativeMediaId == mediaId);
        }

        if (place is null)
        {
            if (clearWhenMissing && SelectedPlace is null)
            {
                SelectedPlace = _timelineItems.FirstOrDefault();
            }

            return false;
        }

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
            SelectedPlace = scoped ?? place;
        }
        else
        {
            SelectedPlace = place;
        }

        return true;
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

    private async Task SyncMapAsync()
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

            var markers = markerItems.Select(item => new MapMarker(
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

            if (markers.Count > 0)
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
        if (_mapController?.IsReady != true)
        {
            return;
        }

        try
        {
            await _mapController.SelectMarkerAsync(item.PlaceId, center: true);
            await _mapController.CenterOnAsync(item.Latitude, item.Longitude, 16);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to focus map on place.");
        }
    }

    private async Task LoadPreviewAsync(VisitRecordPlaceItem item)
    {
        var previews = item.PreviewPhotos
            .Select(photo => new VisitPreviewItem(photo))
            .ToList();
        PreviewPhotos = new ObservableCollection<VisitPreviewItem>(previews);

        foreach (var preview in previews)
        {
            try
            {
                var path = await _thumbnailService.GetOrCreateThumbnailAsync(
                    preview.MediaId,
                    preview.AbsoluteLibraryPath);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    await EnqueueAsync(() => preview.ThumbnailImage = new BitmapImage(new Uri(path)));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Preview thumbnail failed. MediaId={MediaId}", preview.MediaId);
            }
        }
    }

    private async Task LoadTimelineThumbnailsAsync(IReadOnlyList<VisitRecordPlaceItem> items)
    {
        _thumbCts?.Cancel();
        _thumbCts?.Dispose();
        _thumbCts = new CancellationTokenSource();
        var token = _thumbCts.Token;

        try
        {
            foreach (var item in items)
            {
                token.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(item.RepresentativeAbsolutePath) ||
                    item.RepresentativeMediaId is not Guid mediaId)
                {
                    continue;
                }

                try
                {
                    var path = await _thumbnailService.GetOrCreateThumbnailAsync(
                        mediaId,
                        item.RepresentativeAbsolutePath,
                        token);
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        await EnqueueAsync(() =>
                        {
                            var image = new BitmapImage(new Uri(path));
                            item.ThumbnailImage = image;
                            PropagateThumbnailToYearGroups(item.PlaceId, image);
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Timeline thumbnail failed. PlaceId={PlaceId}", item.PlaceId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // expected
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

            var items = await _memorySearchService.SuggestAsync(text, token);
            await EnqueueAsync(() =>
            {
                Suggestions = new ObservableCollection<SearchSuggestionItem>(
                    items.Select(item => new SearchSuggestionItem(item)));
                IsSuggestionOpen = Suggestions.Count > 0 && !IsBusy;
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

    private async Task RefreshRecentQueriesAsync()
    {
        var queries = await _memorySearchService.GetRecentQueriesAsync();
        RecentQueries = new ObservableCollection<string>(queries);
    }

    private void OnMapReady(object? sender, EventArgs e)
    {
        IsMapReady = true;
        _ = SyncMapAsync();
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
