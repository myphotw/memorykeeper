using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemoryKeeper.App.Diagnostics;
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
    private const int CleanupPageSize = 50;
    private const int CleanupGroupLimit = 5;
    private const int CleanupGroupPhotoLimit = 50;
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
    private IReadOnlyList<PendingMemoryMediaItem> _loadedMediaItems = [];
    private IReadOnlyDictionary<Guid, PlaceDto> _registeredPlacesById =
        new Dictionary<Guid, PlaceDto>();
    private int _cleanupPage;
    private string? _placeGroupCursor;
    private string? _captureDateGroupCursor;
    private string? _groupPhotoCursor;
    private bool _suppressGroupSelection;

    [ObservableProperty]
    private ObservableCollection<PendingMemoryGroupItem> groups = [];

    [ObservableProperty]
    private ObservableCollection<PlaceCleanupGroupItem> placeGroups = [];

    [ObservableProperty]
    private ObservableCollection<CaptureDateCleanupGroupItem> captureDateGroups = [];

    [ObservableProperty]
    private PlaceCleanupGroupItem? selectedPlaceGroup;

    [ObservableProperty]
    private CaptureDateCleanupGroupItem? selectedCaptureDateGroup;

    [ObservableProperty]
    private CleanupMode selectedCleanupMode = CleanupMode.Place;

    [ObservableProperty]
    private bool canLoadMorePlaceGroups;

    [ObservableProperty]
    private bool canLoadMoreCaptureDateGroups;

    [ObservableProperty]
    private bool canLoadMoreGroupPhotos;

    [ObservableProperty]
    private int placeTotalGroups;

    [ObservableProperty]
    private int placeTotalPhotos;

    [ObservableProperty]
    private int captureDateTotalGroups;

    [ObservableProperty]
    private int captureDateTotalPhotos;

    [ObservableProperty]
    private int activeGroupPhotoTotal;

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
    private string statusMessage = "장소 정리 목록을 불러오세요.";

    [ObservableProperty]
    private int cleanupTotal;

    [ObservableProperty]
    private int cleanupLoadedCount;

    [ObservableProperty]
    private bool canLoadMoreCleanup;

    public string CleanupProgressText => $"{CleanupLoadedCount:N0}/{CleanupTotal:N0}장";

    public string PlaceQueueSummaryText => $"{PlaceGroups.Count:N0}/{PlaceTotalGroups:N0}개 그룹 · {PlaceTotalPhotos:N0}장";

    public string CaptureDateQueueSummaryText =>
        $"{CaptureDateGroups.Count:N0}/{CaptureDateTotalGroups:N0}개 그룹 · {CaptureDateTotalPhotos:N0}장";

    public string ActiveGroupPhotoProgressText => $"{ActiveMediaItems.Count:N0}/{ActiveGroupPhotoTotal:N0}장";

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

    public Func<string, PlaceRadiusExpansionPlan, Task<bool>>? RadiusExpansionPreviewHandler { get; set; }

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
        IsCaptureDateMode
            ? SelectedCaptureDateGroup is null
                ? "날짜 정리 사진"
                : $"{SelectedCaptureDateGroup.Title} (체크 해제 시 제외)"
            : IsGpsSectionSelected
            ? "GPS 있음 · 장소 정리 필요 (체크 해제 시 제외)"
            : SelectedPlaceGroup is not null
                ? $"{SelectedPlaceGroup.Title} (체크 해제 시 제외)"
            : SelectedGroup is not null
                ? "장소 정리 그룹 사진 (체크 해제 시 제외)"
                : "전체 장소 정리 사진 (체크 해제 시 제외)";

    public bool IsPlaceMode => SelectedCleanupMode == CleanupMode.Place;

    public bool IsCaptureDateMode => SelectedCleanupMode == CleanupMode.CaptureDate;

    public bool CanClearCaptureDate =>
        IsCaptureDateMode
        && ActiveMediaItems.Any(item => item.IsIncluded && item.HasUserCaptureOverride);

    public string SelectedDateStatusText
    {
        get
        {
            var selected = ActiveMediaItems.Where(item => item.IsIncluded).ToList();
            if (selected.Count == 0)
            {
                return "촬영일을 변경할 사진을 선택하세요.";
            }

            var basis = selected.Select(item => item.DateBasisText).Distinct().ToList();
            return basis.Count == 1
                ? $"{selected.Count:N0}장 · {basis[0]}"
                : $"{selected.Count:N0}장 · 여러 날짜 기준";
        }
    }

    public int IncludedCount =>
        ActiveMediaItems.Count(item => item.IsIncluded);

    public bool HasSelectionForActions => IncludedCount > 0;

    public bool IsSelectionMode => HasSelectionForActions;

    /// <summary>Selected cleanup items need place registration or remapping.</summary>
    public bool EmphasizePlaceRegistration => HasSelectionForActions;

    public event EventHandler? OpenPlaceRegistrationRequested;

    public event EventHandler? OpenMemoRequested;

    public event EventHandler? OpenCaptureDateEditorRequested;

    public event EventHandler? ClearCaptureDateRequested;

    public event EventHandler<string>? CaptureDateFeedbackRequested;

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
        return ActiveMediaItems
            .Where(item => item.IsIncluded)
            .Select(item => item.MediaId)
            .Distinct()
            .ToList();
    }

    partial void OnSelectedGroupChanged(PendingMemoryGroupItem? value)
    {
        if (_suppressGroupSelection)
        {
            return;
        }

        if (value is not null)
        {
            IsGpsSectionSelected = false;
            SelectedGroupMedia = new ObservableCollection<PendingMemoryMediaItem>(value.MediaItems);
            ActiveMediaItems = SelectedGroupMedia;
            ResubscribeMediaPropertyChanged();
            NotifySelectionChanged();
            OnPropertyChanged(nameof(ActiveMediaSectionTitle));
            _ = LoadThumbnailsAsync(SelectedGroupMedia);
            return;
        }

        if (!IsGpsSectionSelected)
        {
            SelectedGroupMedia = [];
            ActiveMediaItems = new ObservableCollection<PendingMemoryMediaItem>(_loadedMediaItems);
            ResubscribeMediaPropertyChanged();
            NotifySelectionChanged();
            OnPropertyChanged(nameof(ActiveMediaSectionTitle));
            _ = LoadThumbnailsAsync(ActiveMediaItems);
        }
    }

    [RelayCommand]
    private void SelectAllCleanup()
    {
        IsGpsSectionSelected = false;
        if (SelectedGroup is not null)
        {
            SelectedGroup = null;
            return;
        }

        SelectedGroupMedia = [];
        ActiveMediaItems = new ObservableCollection<PendingMemoryMediaItem>(_loadedMediaItems);
        ResubscribeMediaPropertyChanged();
        NotifySelectionChanged();
        OnPropertyChanged(nameof(ActiveMediaSectionTitle));
        _ = LoadThumbnailsAsync(ActiveMediaItems);
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

        SelectedGroupMedia = [];
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
    private async Task LoadMoreCleanupAsync()
    {
        if (!CanLoadMoreCleanup)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var overview = await _pendingMemoryService.GetPlaceCleanupMemoriesAsync(
                _cleanupPage + 1,
                CleanupPageSize);
            ApplyOverview(overview, preserveSelection: true);
        });
    }

    [RelayCommand]
    private async Task LoadMorePlaceGroupsAsync()
    {
        if (!CanLoadMorePlaceGroups || string.IsNullOrWhiteSpace(_placeGroupCursor))
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            try
            {
                await LoadPlaceQueuePageAsync(_placeGroupCursor, selectFirst: true);
            }
            catch (ApiException ex) when (ex.DetailCode == "INVALID_CLEANUP_CURSOR")
            {
                await LoadPlaceQueuePageAsync(cursor: null, selectFirst: true);
                StatusMessage = "장소 정리 목록이 변경되어 처음부터 다시 불러왔습니다.";
            }
        });
    }

    [RelayCommand]
    private async Task LoadMoreCaptureDateGroupsAsync()
    {
        if (!CanLoadMoreCaptureDateGroups || string.IsNullOrWhiteSpace(_captureDateGroupCursor))
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            try
            {
                await LoadCaptureDateQueuePageAsync(_captureDateGroupCursor, selectFirst: true);
            }
            catch (ApiException ex) when (ex.DetailCode == "INVALID_CLEANUP_CURSOR")
            {
                await LoadCaptureDateQueuePageAsync(cursor: null, selectFirst: true);
                StatusMessage = "날짜 정리 목록이 변경되어 처음부터 다시 불러왔습니다.";
            }
        });
    }

    [RelayCommand]
    private async Task LoadMoreGroupPhotosAsync()
    {
        if (!CanLoadMoreGroupPhotos || string.IsNullOrWhiteSpace(_groupPhotoCursor))
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            try
            {
                if (IsCaptureDateMode && SelectedCaptureDateGroup is not null)
                {
                    await LoadCaptureDateGroupPhotosAsync(SelectedCaptureDateGroup, _groupPhotoCursor, append: true);
                }
                else if (SelectedPlaceGroup is not null)
                {
                    await LoadPlaceGroupPhotosAsync(SelectedPlaceGroup, _groupPhotoCursor, append: true);
                }
            }
            catch (ApiException ex) when (ex.DetailCode == "INVALID_CLEANUP_CURSOR")
            {
                if (IsCaptureDateMode && SelectedCaptureDateGroup is not null)
                {
                    await LoadCaptureDateGroupPhotosAsync(SelectedCaptureDateGroup, cursor: null, append: false);
                }
                else if (SelectedPlaceGroup is not null)
                {
                    await LoadPlaceGroupPhotosAsync(SelectedPlaceGroup, cursor: null, append: false);
                }

                StatusMessage = "사진 목록이 변경되어 처음부터 다시 불러왔습니다.";
            }
            catch (ApiException ex) when (
                ex.StatusCode == System.Net.HttpStatusCode.NotFound
                && ex.DetailCode == "CLEANUP_GROUP_NOT_FOUND")
            {
                if (IsCaptureDateMode)
                {
                    await LoadCaptureDateQueuePageAsync(cursor: null, selectFirst: true);
                }
                else
                {
                    await LoadPlaceQueuePageAsync(cursor: null, selectFirst: true);
                }

                StatusMessage = "정리 그룹이 변경되어 최신 목록을 다시 불러왔습니다.";
            }
        });
    }

    [RelayCommand]
    private void OpenPhotoDetail(PendingMemoryMediaItem? item)
    {
        if (item is null || IsSelectionMode)
        {
            return;
        }

        var playlist = ActiveMediaItems
            .Select(media => media.MediaId)
            .Distinct()
            .ToList();
        if (playlist.Count == 0)
        {
            playlist = [item.MediaId];
        }

        _photoNavigationState.RequestOpenViewer(item.MediaId, playlist, "pending", autoAdvanceAfterPlaceRegister: true);
    }

    [RelayCommand]
    private void ActivateMedia(PendingMemoryMediaItem? item)
    {
        if (item is null)
        {
            return;
        }

        if (IsSelectionMode)
        {
            ToggleInclude(item);
            return;
        }

        OpenPhotoDetail(item);
    }

    [RelayCommand]
    private void OpenSelectedPhotoDetail()
    {
        var item = ActiveMediaItems.FirstOrDefault(media => media.IsIncluded)
            ?? ActiveMediaItems.FirstOrDefault();
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
    private void OpenPlaceRegistration()
    {
        if (IsPlaceMode)
        {
            OpenPlaceRegistrationRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectionForActions))]
    private void OpenCaptureDateEditor()
    {
        if (IsCaptureDateMode)
        {
            OpenCaptureDateEditorRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand(CanExecute = nameof(CanClearCaptureDate))]
    private void RequestClearCaptureDate() => ClearCaptureDateRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenMemo() =>
        OpenMemoRequested?.Invoke(this, EventArgs.Empty);

    public PendingMemoryMediaItem? GetRepresentativeSelectedMedia() =>
        ActiveMediaItems.FirstOrDefault(item => item.IsIncluded)
        ?? ActiveMediaItems.FirstOrDefault();

    public async Task ChangeCaptureDateAsync(DateOnly? userCaptureDate, bool clearOnlyOverrides = false)
    {
        if (IsBusy)
        {
            ReportCaptureDateFeedback("다른 작업이 끝난 뒤 다시 시도해 주세요.");
            return;
        }

        if (userCaptureDate is null && !clearOnlyOverrides)
        {
            ReportCaptureDateFeedback("변경할 촬영일을 선택하세요.");
            return;
        }

        var selected = ActiveMediaItems
            .Where(item => item.IsIncluded)
            .Where(item => !clearOnlyOverrides || item.HasUserCaptureOverride)
            .GroupBy(item => item.MediaId)
            .Select(group => group.First())
            .ToList();
        if (selected.Count == 0)
        {
            ReportCaptureDateFeedback(clearOnlyOverrides
                ? "사용자가 지정한 촬영일이 있는 사진을 선택하세요."
                : "촬영일을 변경할 사진을 선택하세요.");
            return;
        }

        if (selected.Any(item => item.Media.DateRevision < 0))
        {
            ReportCaptureDateFeedback("선택한 사진의 최신 촬영일 revision을 확인할 수 없습니다. 목록을 새로 고친 뒤 다시 시도해 주세요.");
            return;
        }

        await RunBusyAsync(async () =>
        {
            StatusMessage = userCaptureDate is null
                ? $"{selected.Count:N0}장의 촬영일 보정을 해제하고 있습니다."
                : $"{selected.Count:N0}장의 촬영일을 변경하고 있습니다.";
            try
            {
                var revisions = selected.ToDictionary(item => item.MediaId, item => item.Media.DateRevision);
                var response = await _pendingMemoryService.SetCaptureDateAsync(revisions, userCaptureDate);
                await ReloadAfterCaptureDateMutationAsync();
                StatusMessage = userCaptureDate is null
                    ? $"{response.UpdatedCount:N0}장의 촬영일 보정을 해제했습니다."
                    : $"{response.UpdatedCount:N0}장의 촬영일을 변경했습니다.";
            }
            catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                LogCaptureDateFailure("Mutation", selected.Count, ex);
                await ReloadAfterCaptureDateMutationAsync();
                StatusMessage = "사진 상태가 변경되었습니다. 최신 상태를 불러왔으니 다시 시도해 주세요.";
            }
            catch (ApiException ex)
            {
                LogCaptureDateFailure("Mutation", selected.Count, ex);
                throw;
            }
            catch (Exception ex)
            {
                LogCaptureDateFailure("Mutation", selected.Count, ex);
                throw;
            }
        });

        CaptureDateFeedbackRequested?.Invoke(this, StatusMessage);
    }

    private void ReportCaptureDateFeedback(string message)
    {
        StatusMessage = message;
        CaptureDateFeedbackRequested?.Invoke(this, message);
    }

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

            var previewSource = ActiveMediaItems.FirstOrDefault(item => item.IsIncluded)
                ?? ActiveMediaItems.FirstOrDefault();

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
        MapPickRadiusMeters = Math.Clamp(radiusMeters, 20, PlaceRadiusExpansionPlanner.MaximumRadiusMeters);
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

    private async Task<CreatePlaceRequest> BuildPlaceFromMapPickRequestAsync()
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

        return new CreatePlaceRequest
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
        };
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

    private async Task<ProviderPlaceResolution> ResolveProviderPlaceAsync(
        string providerPlaceId,
        string? fallbackName,
        string? fallbackType,
        double? seedLatitude,
        double? seedLongitude)
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
        if (resolved is not null)
        {
            SelectedLocation = PlaceLocationPreview.FromLocationResult(
                resolved with
                {
                    DisplayName = normalized!.DisplayName,
                    Country = normalized.Country,
                    Province = normalized.Province,
                    City = normalized.City,
                },
                MapPickRadiusMeters,
                SelectedNearbyCandidate is null ? PlaceLocationSource.Google : PlaceLocationSource.Nearby);
        }
        if (latitude is double lat && longitude is double lon)
        {
            var matched = await _placeService.MatchPlaceAsync(
                lat, lon, providerPlaceId, normalized?.CanonicalName);
            if (matched is not null)
            {
                return new ProviderPlaceResolution { ExistingPlace = matched };
            }
        }

        if (latitude is null || longitude is null)
        {
            throw new InvalidOperationException("선택한 장소의 좌표를 확인할 수 없습니다.");
        }

        return new ProviderPlaceResolution
        {
            CreateRequest = new CreatePlaceRequest
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
            },
        };
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
        var selections = BuildRadiusSelections(mediaIds);
        PreparedPlace? completedPreparation = null;

        try
        {
            PreparedPlace? prepared;
            if (existingPlaceId is Guid placeId)
            {
                var existingPlace = await _placeService.GetPlaceAsync(placeId);
                prepared = await PrepareExistingPlaceAsync(existingPlace, selections);
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

                var resolution = await ResolveProviderPlaceAsync(
                    googlePlaceId,
                    fallbackName,
                    fallbackType,
                    seedLatitude,
                    seedLongitude);
                prepared = resolution.ExistingPlace is not null
                    ? await PrepareExistingPlaceAsync(resolution.ExistingPlace, selections)
                    : await PrepareNewPlaceAsync(
                        resolution.CreateRequest!,
                        geographyFallback,
                        selections);
            }
            else if (HasMapPickSelection)
            {
                prepared = await PrepareNewPlaceAsync(
                    await BuildPlaceFromMapPickRequestAsync(),
                    geographyFallback,
                    selections);
            }
            else
            {
                PlaceDialogStatus = "연결할 장소를 선택하세요.";
                return false;
            }

            if (prepared is null)
            {
                return false;
            }

            completedPreparation = prepared;
            var place = prepared.Place;
            var result = await AssignManualPlaceAsync(
                mediaIds,
                place.Id,
                prepared.ReclassificationPerformed);

            var supplementFailureCount = 0;
            var reclass = prepared.Reclassification;
            if (result.UpdatedCount > 0 || result.UpdatedMediaIds.Count > 0)
            {
                supplementFailureCount = await SupplementRawLocationsAsync(
                    result.UpdatedMediaIds,
                    BuildRawLocationSource(place, SelectedLocation));
            }

            await ReloadPlaceQueueAfterMutationAsync();
            var finalState = await VerifyFinalPlaceStateAsync(mediaIds, place.Id);
            var selectedIds = mediaIds.ToHashSet();
            var postReloadSelected = _loadedMediaItems
                .Where(item => selectedIds.Contains(item.MediaId))
                .ToList();
            var outcome = PendingPlaceAssignmentOutcomeEvaluator.Evaluate(
                new PendingPlaceAssignmentVerification
                {
                    RequestedCount = mediaIds.Count,
                    AssignedCount = result.UpdatedCount,
                    UpdatedIdCount = result.UpdatedMediaIds.Count,
                    ConflictCount = result.ConflictCount,
                    RevisionRefreshFailureCount = result.RevisionRefreshFailureCount,
                    ReclassUnassignedCount = reclass?.UnassignedCount ?? 0,
                    PostReloadWithPlaceIdCount = finalState.MatchedCount,
                    PostReloadRemainingSelectedCount = postReloadSelected.Count,
                    FinalStateVerificationFailureCount = finalState.FailureCount,
                    PlaceDisplayName = place.DisplayName,
                    RadiusExpanded = prepared.PreviousRadiusMeters.HasValue,
                    CreatedNewPlace = prepared.CreatedNewPlace,
                    PreviousRadiusMeters = prepared.PreviousRadiusMeters ?? place.Radius,
                    CurrentRadiusMeters = place.Radius,
                });
            PlaceDialogStatus = supplementFailureCount > 0
                ? $"{outcome.UserMessage} 일부 사진의 위치 세부정보는 보완하지 못했습니다."
                : outcome.UserMessage;
            StatusMessage = PlaceDialogStatus;

            _logger.LogInformation(
                "PLACE_CLEANUP_ASSIGN_DIAG selected_count={SelectedCount} assigned_count={AssignedCount} updated_id_count={UpdatedIdCount} reclass_assigned_count={ReclassAssignedCount} reclass_unassigned_count={ReclassUnassignedCount} pre_assign_revision_refresh_failure_count={RevisionRefreshFailureCount} conflict_count={ConflictCount} post_reload_cleanup_selected_count={PostReloadCleanupSelectedCount} post_reload_with_place_id_count={PostReloadWithPlaceIdCount}",
                mediaIds.Count,
                result.UpdatedCount,
                result.UpdatedMediaIds.Count,
                reclass?.AssignedCount ?? 0,
                reclass?.UnassignedCount ?? 0,
                result.RevisionRefreshFailureCount,
                result.ConflictCount,
                postReloadSelected.Count,
                finalState.MatchedCount);
            PlaceCleanupDiagnostics.WriteAssignment(
                mediaIds.Count,
                result.UpdatedCount,
                result.UpdatedMediaIds.Count,
                reclass?.AssignedCount ?? 0,
                reclass?.UnassignedCount ?? 0,
                result.RevisionRefreshFailureCount,
                result.ConflictCount,
                postReloadSelected.Count,
                finalState.MatchedCount);
            return outcome.IsSuccess;
        }
        catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            _logger.LogWarning(ex, "Pending place assignment revision conflict.");
            await TryReloadAfterMutationAsync();
            PlaceDialogStatus = WithRadiusChangeNotice(
                completedPreparation,
                "다른 변경이 먼저 반영되어 장소 등록을 완료하지 못했습니다. 최신 목록을 다시 불러왔습니다.");
            StatusMessage = PlaceDialogStatus;
            return false;
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Confirm pending place registration API request failed.");
            await TryReloadAfterMutationAsync();
            PlaceDialogStatus = WithRadiusChangeNotice(
                completedPreparation,
                ApiErrorClassifier.ToUserMessage(ex, "사진 또는 장소를 찾을 수 없습니다."));
            StatusMessage = PlaceDialogStatus;
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Confirm place registration failed.");
            await TryReloadAfterMutationAsync();
            PlaceDialogStatus = WithRadiusChangeNotice(
                completedPreparation,
                "장소 등록을 완료하지 못했습니다. 최신 목록을 다시 불러왔습니다.");
            StatusMessage = PlaceDialogStatus;
            return false;
        }
        finally
        {
            IsPlaceDialogBusy = false;
        }
    }

    partial void OnSelectedCleanupModeChanged(CleanupMode value)
    {
        OnPropertyChanged(nameof(IsPlaceMode));
        OnPropertyChanged(nameof(IsCaptureDateMode));
        OnPropertyChanged(nameof(ActiveMediaSectionTitle));
        OnPropertyChanged(nameof(CanClearCaptureDate));
    }

    partial void OnPlaceGroupsChanged(ObservableCollection<PlaceCleanupGroupItem> value) =>
        OnPropertyChanged(nameof(PlaceQueueSummaryText));

    partial void OnCaptureDateGroupsChanged(ObservableCollection<CaptureDateCleanupGroupItem> value) =>
        OnPropertyChanged(nameof(CaptureDateQueueSummaryText));

    partial void OnPlaceTotalGroupsChanged(int value) => OnPropertyChanged(nameof(PlaceQueueSummaryText));

    partial void OnPlaceTotalPhotosChanged(int value) => OnPropertyChanged(nameof(PlaceQueueSummaryText));

    partial void OnCaptureDateTotalGroupsChanged(int value) => OnPropertyChanged(nameof(CaptureDateQueueSummaryText));

    partial void OnCaptureDateTotalPhotosChanged(int value) => OnPropertyChanged(nameof(CaptureDateQueueSummaryText));

    partial void OnActiveGroupPhotoTotalChanged(int value) =>
        OnPropertyChanged(nameof(ActiveGroupPhotoProgressText));

    partial void OnActiveMediaItemsChanged(ObservableCollection<PendingMemoryMediaItem> value)
    {
        OnPropertyChanged(nameof(ActiveGroupPhotoProgressText));
        OnPropertyChanged(nameof(CanClearCaptureDate));
        OnPropertyChanged(nameof(SelectedDateStatusText));
    }

    partial void OnSelectedPlaceGroupChanged(PlaceCleanupGroupItem? value)
    {
        if (!_suppressGroupSelection && value is not null)
        {
            _ = RunBusyAsync(() => ActivatePlaceGroupAsync(value));
        }
    }

    partial void OnSelectedCaptureDateGroupChanged(CaptureDateCleanupGroupItem? value)
    {
        if (!_suppressGroupSelection && value is not null)
        {
            _ = RunBusyAsync(() => ActivateCaptureDateGroupAsync(value));
        }
    }

    private IReadOnlyList<PlaceRadiusPhotoSelection> BuildRadiusSelections(
        IReadOnlyCollection<Guid> mediaIds)
    {
        var selectedIds = mediaIds.ToHashSet();
        return ActiveMediaItems
            .Where(item => selectedIds.Contains(item.MediaId))
            .GroupBy(item => item.MediaId)
            .Select(group => group.First().Media)
            .Select(media => new PlaceRadiusPhotoSelection(
                media.MediaId,
                media.FileName,
                media.Latitude,
                media.Longitude))
            .ToList();
    }

    private async Task<PreparedPlace?> PrepareExistingPlaceAsync(
        PlaceDto place,
        IReadOnlyList<PlaceRadiusPhotoSelection> selections)
    {
        var plan = PlaceRadiusExpansionPlanner.Create(
            place.Latitude,
            place.Longitude,
            place.Radius,
            selections);
        if (!plan.NeedsExpansion)
        {
            return new PreparedPlace(place);
        }

        if (!await ConfirmRadiusExpansionAsync(place.DisplayName, plan))
        {
            return null;
        }

        var operation = await _placeService.UpdateWithRadiusImpactAsync(
            place,
            ToRadiusUpdateRequest(place, plan.ProposedRadiusMeters),
            (impact, token) => PlaceOverlapPrompt.ConfirmImpactIfNeededAsync(
                HostXamlRoot,
                place.DisplayName,
                impact,
                token));
        if (operation.Cancelled)
        {
            PlaceDialogStatus = "장소 등록이 취소되었습니다.";
            return null;
        }

        var updated = operation.UpdatedPlace
            ?? throw new InvalidOperationException("장소 범위 변경 결과를 확인할 수 없습니다.");
        return new PreparedPlace(
            updated,
            place.Radius,
            ReclassificationPerformed: true,
            Reclassification: operation.Reclassification,
            ExistingRadiusUpdated: true);
    }

    private async Task<PreparedPlace?> PrepareNewPlaceAsync(
        CreatePlaceRequest request,
        PlaceGeographyFallback geographyFallback,
        IReadOnlyList<PlaceRadiusPhotoSelection> selections)
    {
        var initialRadius = request.Radius ?? 100d;
        var plan = PlaceRadiusExpansionPlanner.Create(
            request.Latitude,
            request.Longitude,
            initialRadius,
            selections);
        double? previousRadius = null;
        if (plan.NeedsExpansion)
        {
            if (!await ConfirmRadiusExpansionAsync(request.DisplayName, plan))
            {
                return null;
            }

            previousRadius = initialRadius;
            request = CopyWithRadius(request, plan.ProposedRadiusMeters);
        }

        var overlapOk = await PlaceOverlapPrompt.ConfirmIfNeededAsync(
            HostXamlRoot,
            _placeService,
            request.DisplayName,
            request.Latitude,
            request.Longitude,
            request.Radius ?? 100d);
        if (!overlapOk)
        {
            PlaceDialogStatus = "장소 등록이 취소되었습니다.";
            return null;
        }

        var created = await _placeService.CreatePlaceAsync(request, geographyFallback);
        var reclassification = await _placeService.ReclassifyMediaAsync(
            created.Id,
            reassignFromOtherPlaces: true);
        return new PreparedPlace(
            created,
            previousRadius,
            ReclassificationPerformed: true,
            Reclassification: reclassification,
            CreatedNewPlace: true);
    }

    private async Task<bool> ConfirmRadiusExpansionAsync(
        string placeDisplayName,
        PlaceRadiusExpansionPlan plan)
    {
        if (plan.ExceedsMaximum)
        {
            PlaceDialogStatus =
                $"선택한 사진을 포함하려면 약 {plan.ProposedRadiusMeters:0}m 범위가 필요하지만 허용 범위 {PlaceRadiusExpansionPlanner.MaximumRadiusMeters:0}m를 초과합니다. 사진 위치를 확인해 주세요.";
            return false;
        }

        if (RadiusExpansionPreviewHandler is null || HostXamlRoot is null)
        {
            PlaceDialogStatus = "장소 범위 지도 미리보기를 표시할 수 없어 등록을 진행하지 않았습니다.";
            return false;
        }

        if (!await RadiusExpansionPreviewHandler(placeDisplayName, plan))
        {
            PlaceDialogStatus = "장소 등록이 취소되었습니다.";
            return false;
        }

        return true;
    }

    private static UpdatePlaceRequest ToRadiusUpdateRequest(PlaceDto place, double radius) => new()
    {
        Id = place.Id,
        Revision = place.Revision,
        DisplayName = place.DisplayName,
        CanonicalName = place.CanonicalName,
        Country = place.Country,
        Province = place.Province,
        City = place.City,
        District = place.District,
        Address = place.Address,
        PostalCode = place.PostalCode,
        GooglePlaceId = place.GooglePlaceId,
        Category = place.Category,
        Latitude = place.Latitude,
        Longitude = place.Longitude,
        Radius = radius,
        IsActive = place.IsActive,
        IsFavorite = place.IsFavorite,
        ReclassifyMedia = true,
        ReassignFromOtherPlaces = true,
    };

    private static CreatePlaceRequest CopyWithRadius(CreatePlaceRequest request, double radius) => new()
    {
        DisplayName = request.DisplayName,
        Country = request.Country,
        Province = request.Province,
        City = request.City,
        District = request.District,
        Address = request.Address,
        PostalCode = request.PostalCode,
        GooglePlaceId = request.GooglePlaceId,
        CanonicalName = request.CanonicalName,
        Category = request.Category,
        Latitude = request.Latitude,
        Longitude = request.Longitude,
        Radius = radius,
        IsActive = request.IsActive,
        IsFavorite = request.IsFavorite,
        ReclassifyMedia = request.ReclassifyMedia,
        ReassignFromOtherPlaces = request.ReassignFromOtherPlaces,
    };

    private async Task<AssignMediaPlaceResult> AssignManualPlaceAsync(
        IReadOnlyList<Guid> mediaIds,
        Guid placeId,
        bool refreshAfterReclassification)
    {
        var distinctIds = mediaIds.Distinct().ToList();
        IReadOnlyDictionary<Guid, int>? latestRevisions = null;
        var revisionRefreshFailureCount = 0;
        if (refreshAfterReclassification)
        {
            var refresh = await LoadLatestPlaceRevisionsAsync(distinctIds);
            latestRevisions = refresh.Revisions;
            revisionRefreshFailureCount = refresh.FailureCount;
            distinctIds = distinctIds
                .Where(id => latestRevisions.ContainsKey(id))
                .ToList();
        }

        if (distinctIds.Count == 0)
        {
            return new AssignMediaPlaceResult
            {
                PlaceId = placeId,
                RevisionRefreshFailureCount = revisionRefreshFailureCount,
            };
        }

        try
        {
            var result = await _pendingMemoryService.AssignPlaceAsync(new AssignMediaPlaceRequest
            {
                PlaceId = placeId,
                MediaIds = distinctIds,
                ExpectedPlaceRevisions = latestRevisions,
            });
            return new AssignMediaPlaceResult
            {
                PlaceId = result.PlaceId,
                UpdatedCount = result.UpdatedCount,
                UpdatedMediaIds = result.UpdatedMediaIds,
                ConflictCount = result.ConflictCount,
                RevisionRefreshFailureCount = revisionRefreshFailureCount,
            };
        }
        catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            _logger.LogWarning(
                ex,
                "Batch manual place assignment conflicted; retrying selected photos individually with authoritative revisions.");
            return await AssignIndividuallyAfterConflictAsync(
                distinctIds,
                placeId,
                revisionRefreshFailureCount);
        }
    }

    private async Task<PlaceRevisionRefresh> LoadLatestPlaceRevisionsAsync(
        IReadOnlyCollection<Guid> mediaIds)
    {
        var revisions = new Dictionary<Guid, int>();
        var failures = 0;
        foreach (var mediaId in mediaIds.Distinct())
        {
            try
            {
                var detail = await _galleryApiRepository.GetPhotoAsync(mediaId);
                if (detail.PlaceRevision is int revision)
                {
                    revisions[mediaId] = revision;
                }
                else
                {
                    failures++;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures++;
                _logger.LogWarning(ex, "Failed to refresh a selected photo place revision.");
            }
        }

        return new PlaceRevisionRefresh(revisions, failures);
    }

    private async Task<AssignMediaPlaceResult> AssignIndividuallyAfterConflictAsync(
        IReadOnlyCollection<Guid> mediaIds,
        Guid placeId,
        int priorRevisionRefreshFailureCount)
    {
        var updatedIds = new List<Guid>();
        var conflictCount = 0;
        var refreshFailureCount = priorRevisionRefreshFailureCount;
        foreach (var mediaId in mediaIds.Distinct())
        {
            try
            {
                var detail = await _galleryApiRepository.GetPhotoAsync(mediaId);
                if (detail.PlaceRevision is not int revision)
                {
                    refreshFailureCount++;
                    continue;
                }

                await _placeService.AssignFilePlaceAsync(mediaId, placeId, revision);
                updatedIds.Add(mediaId);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (MemoryKeeperPlaceRevisionConflictException ex)
            {
                conflictCount++;
                _logger.LogWarning(ex, "A selected photo changed during manual place assignment.");
            }
            catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                conflictCount++;
                _logger.LogWarning(ex, "A selected photo changed during manual place assignment.");
            }
            catch (Exception ex)
            {
                refreshFailureCount++;
                _logger.LogWarning(ex, "Individual manual place assignment failed.");
            }
        }

        return new AssignMediaPlaceResult
        {
            PlaceId = placeId,
            UpdatedCount = updatedIds.Count,
            UpdatedMediaIds = updatedIds,
            ConflictCount = conflictCount,
            RevisionRefreshFailureCount = refreshFailureCount,
        };
    }

    private async Task<(int MatchedCount, int FailureCount)> VerifyFinalPlaceStateAsync(
        IReadOnlyCollection<Guid> mediaIds,
        Guid placeId)
    {
        var matched = 0;
        var failures = 0;
        foreach (var mediaId in mediaIds.Distinct())
        {
            try
            {
                var detail = await _galleryApiRepository.GetPhotoAsync(mediaId);
                if (detail.MemorykeeperPlaceId == placeId)
                {
                    matched++;
                }
            }
            catch (Exception ex)
            {
                failures++;
                _logger.LogWarning(ex, "Final place state verification failed. MediaId={MediaId}", mediaId);
            }
        }

        return (matched, failures);
    }

    private async Task TryReloadAfterMutationAsync()
    {
        try
        {
            await ReloadPlaceQueueAfterMutationAsync();
        }
        catch (Exception reloadException)
        {
            _logger.LogWarning(reloadException, "Cleanup reload after place mutation failed.");
        }
    }

    private async Task ReloadPlaceQueueAfterMutationAsync()
    {
        _placeGroupCursor = null;
        _groupPhotoCursor = null;
        await LoadPlaceQueuePageAsync(cursor: null, selectFirst: false);
        if (PlaceGroups.FirstOrDefault() is { } placeGroup)
        {
            await ActivatePlaceGroupAsync(placeGroup);
        }
        else if (CaptureDateGroups.FirstOrDefault() is { } dateGroup)
        {
            await ActivateCaptureDateGroupAsync(dateGroup);
        }
        else
        {
            ClearActiveGroup();
        }
    }

    private static string WithRadiusChangeNotice(PreparedPlace? prepared, string message)
    {
        if (prepared is null)
        {
            return message;
        }

        if (prepared.ExistingRadiusUpdated
            && prepared.PreviousRadiusMeters is double previousRadius)
        {
            return $"'{prepared.Place.DisplayName}'의 장소 범위는 {previousRadius:0}m에서 {prepared.Place.Radius:0}m로 변경되었습니다. {message}";
        }

        return prepared.CreatedNewPlace
            ? $"새 장소 '{prepared.Place.DisplayName}'는 생성되었습니다. {message}"
            : message;
    }

    private sealed record PreparedPlace(
        PlaceDto Place,
        double? PreviousRadiusMeters = null,
        bool ReclassificationPerformed = false,
        PlaceReclassificationResult? Reclassification = null,
        bool ExistingRadiusUpdated = false,
        bool CreatedNewPlace = false);

    private sealed record PlaceRevisionRefresh(
        IReadOnlyDictionary<Guid, int> Revisions,
        int FailureCount);

    private sealed class ProviderPlaceResolution
    {
        public PlaceDto? ExistingPlace { get; init; }

        public CreatePlaceRequest? CreateRequest { get; init; }
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
        SelectedExistingPlace = ToPickerItem(place);
        SelectedNearbyCandidate = null;
        SelectedPlaceSuggestion = null;
        HasMapPickSelection = false;
        SelectedLocation = PlaceLocationPreview.FromPlaceDto(place, PlaceLocationSource.Existing);
        NotifyPlacePreviewChanged();
        await ConfirmPlaceRegistrationAsync();
    }

    private async Task<int> SupplementRawLocationsAsync(
        IReadOnlyCollection<Guid> mediaIds,
        PlaceLocationPreview selectedLocation)
    {
        List<Exception>? failures = null;
        foreach (var mediaId in mediaIds)
        {
            try
            {
                var detail = await _galleryApiRepository.GetPhotoAsync(mediaId);
                var latitude = GetMetadataDouble(detail.Metadata, "gps_lat");
                var longitude = GetMetadataDouble(detail.Metadata, "gps_lon");
                await _pendingMemoryService.SupplementRawLocationFromPlaceAsync(
                    mediaId,
                    detail.MetadataRevision,
                    latitude,
                    longitude,
                    selectedLocation);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cleanup raw geography supplement failed. MediaId={MediaId}", mediaId);
                (failures ??= []).Add(ex);
            }
        }

        return failures?.Count ?? 0;
    }

    private static PlaceLocationPreview BuildRawLocationSource(
        PlaceDto place,
        PlaceLocationPreview selection)
    {
        var placeLocation = PlaceLocationPreview.FromPlaceDto(place, selection.Source);
        return new PlaceLocationPreview
        {
            PlaceId = place.Id,
            GooglePlaceId = FirstNotBlank(selection.GooglePlaceId, place.GooglePlaceId),
            DisplayName = FirstNotBlank(selection.DisplayName, place.DisplayName) ?? place.DisplayName,
            Country = FirstNotBlank(selection.Country, place.Country) ?? string.Empty,
            Province = FirstNotBlank(selection.Province, place.Province) ?? string.Empty,
            City = FirstNotBlank(selection.City, place.City) ?? string.Empty,
            District = FirstNotBlank(selection.District, place.District) ?? string.Empty,
            Address = FirstNotBlank(selection.Address, place.Address) ?? string.Empty,
            Latitude = selection.Latitude ?? placeLocation.Latitude,
            Longitude = selection.Longitude ?? placeLocation.Longitude,
            RadiusMeters = selection.RadiusMeters > 0 ? selection.RadiusMeters : placeLocation.RadiusMeters,
            Source = selection.Source,
        };
    }

    private static double? GetMetadataDouble(
        IReadOnlyDictionary<string, JsonElement> metadata,
        string key)
    {
        if (!metadata.TryGetValue(key, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String
               && double.TryParse(
                   value.GetString(),
                   System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture,
                   out number)
            ? number
            : null;
    }

    private static string? FirstNotBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private async Task LoadPlaceQueuePageAsync(string? cursor, bool selectFirst)
    {
        var response = await _pendingMemoryService.GetPlaceCleanupGroupsAsync(
            CleanupGroupLimit, cursor);
        PlaceGroups = new ObservableCollection<PlaceCleanupGroupItem>(
            response.Items.Take(CleanupGroupLimit).Select(item => new PlaceCleanupGroupItem(item)));
        _placeGroupCursor = response.NextCursor;
        CanLoadMorePlaceGroups = response.HasMore && !string.IsNullOrWhiteSpace(response.NextCursor);
        PlaceTotalGroups = response.TotalGroups;
        PlaceTotalPhotos = response.TotalPhotos;

        if (selectFirst)
        {
            var first = PlaceGroups.FirstOrDefault();
            if (first is not null)
            {
                await ActivatePlaceGroupAsync(first);
            }
            else if (CaptureDateGroups.Count > 0)
            {
                await ActivateCaptureDateGroupAsync(CaptureDateGroups[0]);
            }
            else
            {
                ClearActiveGroup();
            }
        }
    }

    private async Task LoadCaptureDateQueuePageAsync(string? cursor, bool selectFirst)
    {
        var response = await _pendingMemoryService.GetCaptureDateCleanupGroupsAsync(
            CleanupGroupLimit, cursor);
        CaptureDateGroups = new ObservableCollection<CaptureDateCleanupGroupItem>(
            response.Items.Take(CleanupGroupLimit).Select(item => new CaptureDateCleanupGroupItem(item)));
        _captureDateGroupCursor = response.NextCursor;
        CanLoadMoreCaptureDateGroups = response.HasMore && !string.IsNullOrWhiteSpace(response.NextCursor);
        CaptureDateTotalGroups = response.TotalGroups;
        CaptureDateTotalPhotos = response.TotalPhotos;

        if (selectFirst)
        {
            var first = CaptureDateGroups.FirstOrDefault();
            if (first is not null)
            {
                await ActivateCaptureDateGroupAsync(first);
            }
            else if (PlaceGroups.Count > 0)
            {
                await ActivatePlaceGroupAsync(PlaceGroups[0]);
            }
            else
            {
                ClearActiveGroup();
            }
        }
    }

    private async Task ActivatePlaceGroupAsync(PlaceCleanupGroupItem group)
    {
        SetGroupSelection(group, null, CleanupMode.Place);
        try
        {
            await LoadPlaceGroupPhotosAsync(group, cursor: null, append: false);
        }
        catch (ApiException ex) when (
            ex.StatusCode == System.Net.HttpStatusCode.NotFound
            && ex.DetailCode == "CLEANUP_GROUP_NOT_FOUND")
        {
            await LoadPlaceQueuePageAsync(cursor: null, selectFirst: false);
            ClearActiveGroup();
            StatusMessage = "장소 정리 그룹이 변경되어 최신 목록을 다시 불러왔습니다.";
        }
    }

    private async Task ActivateCaptureDateGroupAsync(CaptureDateCleanupGroupItem group)
    {
        SetGroupSelection(null, group, CleanupMode.CaptureDate);
        try
        {
            await LoadCaptureDateGroupPhotosAsync(group, cursor: null, append: false);
        }
        catch (ApiException ex) when (
            ex.StatusCode == System.Net.HttpStatusCode.NotFound
            && ex.DetailCode == "CLEANUP_GROUP_NOT_FOUND")
        {
            await LoadCaptureDateQueuePageAsync(cursor: null, selectFirst: false);
            ClearActiveGroup();
            StatusMessage = "날짜 정리 그룹이 변경되어 최신 목록을 다시 불러왔습니다.";
        }
    }

    private async Task LoadPlaceGroupPhotosAsync(
        PlaceCleanupGroupItem group,
        string? cursor,
        bool append)
    {
        var page = await _pendingMemoryService.GetPlaceCleanupGroupPhotosAsync(
            group.GroupId, CleanupGroupPhotoLimit, cursor);
        ApplyGroupPhotos(
            page.Items,
            page.NextCursor,
            page.HasMore,
            page.TotalPhotos > 0 ? page.TotalPhotos : group.Group.MediaCount,
            append);
    }

    private async Task LoadCaptureDateGroupPhotosAsync(
        CaptureDateCleanupGroupItem group,
        string? cursor,
        bool append)
    {
        var page = await _pendingMemoryService.GetCaptureDateCleanupGroupPhotosAsync(
            group.GroupId, CleanupGroupPhotoLimit, cursor);
        ApplyGroupPhotos(
            page.Items,
            page.NextCursor,
            page.HasMore,
            page.TotalPhotos > 0 ? page.TotalPhotos : group.Group.MediaCount,
            append);
    }

    private void ApplyGroupPhotos(
        IReadOnlyList<PendingMemoryItemDto> items,
        string? nextCursor,
        bool hasMore,
        int totalPhotos,
        bool append)
    {
        var mapped = items.Select(item =>
        {
            var registeredPlace = item.MemorykeeperPlaceId is Guid placeId
                                  && _registeredPlacesById.TryGetValue(placeId, out var place)
                ? place
                : null;
            return new PendingMemoryMediaItem(item, registeredPlace, SelectedCleanupMode);
        });
        _loadedMediaItems = append
            ? _loadedMediaItems.Concat(mapped).GroupBy(item => item.MediaId).Select(group => group.First()).ToList()
            : mapped.ToList();
        SelectedGroupMedia = new ObservableCollection<PendingMemoryMediaItem>(_loadedMediaItems);
        ActiveMediaItems = SelectedGroupMedia;
        _groupPhotoCursor = nextCursor;
        CanLoadMoreGroupPhotos = hasMore && !string.IsNullOrWhiteSpace(nextCursor);
        ActiveGroupPhotoTotal = totalPhotos > 0 ? totalPhotos : ActiveMediaItems.Count;
        ResubscribeMediaPropertyChanged();
        NotifySelectionChanged();
        OnPropertyChanged(nameof(ActiveMediaSectionTitle));
        _ = LoadThumbnailsAsync(ActiveMediaItems);
    }

    private void SetGroupSelection(
        PlaceCleanupGroupItem? placeGroup,
        CaptureDateCleanupGroupItem? captureDateGroup,
        CleanupMode mode)
    {
        _suppressGroupSelection = true;
        try
        {
            SelectedPlaceGroup = placeGroup;
            SelectedCaptureDateGroup = captureDateGroup;
            SelectedCleanupMode = mode;
            IsGpsSectionSelected = false;
            SelectedGroup = null;
        }
        finally
        {
            _suppressGroupSelection = false;
        }
    }

    private void ClearActiveGroup()
    {
        _suppressGroupSelection = true;
        try
        {
            SelectedPlaceGroup = null;
            SelectedCaptureDateGroup = null;
        }
        finally
        {
            _suppressGroupSelection = false;
        }

        _loadedMediaItems = [];
        SelectedGroupMedia = [];
        ActiveMediaItems = [];
        _groupPhotoCursor = null;
        CanLoadMoreGroupPhotos = false;
        ActiveGroupPhotoTotal = 0;
        ResubscribeMediaPropertyChanged();
        NotifySelectionChanged();
    }

    private async Task ReloadAfterCaptureDateMutationAsync()
    {
        _captureDateGroupCursor = null;
        _placeGroupCursor = null;
        _groupPhotoCursor = null;
        await LoadCaptureDateQueuePageAsync(cursor: null, selectFirst: false);
        await LoadPlaceQueuePageAsync(cursor: null, selectFirst: false);

        var next = CaptureDateGroups.FirstOrDefault();
        if (next is not null)
        {
            await ActivateCaptureDateGroupAsync(next);
        }
        else if (PlaceGroups.FirstOrDefault() is { } place)
        {
            await ActivatePlaceGroupAsync(place);
        }
        else
        {
            ClearActiveGroup();
        }
    }

    private void LogCaptureDateFailure(string stage, int selectedCount, Exception exception)
    {
        var apiException = exception as ApiException;
        _logger.LogError(
            exception,
            "Capture-date cleanup failed. Operation={Operation} Stage={Stage} SelectedCount={SelectedCount} StatusCode={StatusCode} DetailCode={DetailCode} ExceptionType={ExceptionType}",
            "CaptureDateMutation",
            stage,
            selectedCount,
            apiException is null ? null : (int)apiException.StatusCode,
            apiException?.DetailCode,
            exception.GetType().Name);
    }

    private async Task LoadCoreAsync()
    {
        CancelThumbnailLoading();

        var placeGroupsTask = _pendingMemoryService.GetPlaceCleanupGroupsAsync(CleanupGroupLimit);
        var captureDateGroupsTask = _pendingMemoryService.GetCaptureDateCleanupGroupsAsync(CleanupGroupLimit);
        var placeListTask = _placeService.GetPlaceListAsync();
        await Task.WhenAll(placeGroupsTask, captureDateGroupsTask, placeListTask);
        var placeList = await placeListTask;

        _registeredPlacesById = placeList
            .GroupBy(place => place.Id)
            .ToDictionary(group => group.Key, group => group.First());

        Places = new ObservableCollection<PlaceDto>(
            placeList
                .Where(place => place.IsActive)
                .OrderByDescending(place => place.IsFavorite)
                .ThenByDescending(place => place.LastUsedAt ?? DateTime.MinValue)
                .ThenBy(place => place.DisplayName));

        var placeGroupPage = await placeGroupsTask;
        PlaceGroups = new ObservableCollection<PlaceCleanupGroupItem>(
            placeGroupPage.Items.Take(CleanupGroupLimit).Select(item => new PlaceCleanupGroupItem(item)));
        _placeGroupCursor = placeGroupPage.NextCursor;
        CanLoadMorePlaceGroups = placeGroupPage.HasMore && !string.IsNullOrWhiteSpace(placeGroupPage.NextCursor);
        PlaceTotalGroups = placeGroupPage.TotalGroups;
        PlaceTotalPhotos = placeGroupPage.TotalPhotos;

        var dateGroupPage = await captureDateGroupsTask;
        CaptureDateGroups = new ObservableCollection<CaptureDateCleanupGroupItem>(
            dateGroupPage.Items.Take(CleanupGroupLimit).Select(item => new CaptureDateCleanupGroupItem(item)));
        _captureDateGroupCursor = dateGroupPage.NextCursor;
        CanLoadMoreCaptureDateGroups = dateGroupPage.HasMore && !string.IsNullOrWhiteSpace(dateGroupPage.NextCursor);
        CaptureDateTotalGroups = dateGroupPage.TotalGroups;
        CaptureDateTotalPhotos = dateGroupPage.TotalPhotos;

        if (PlaceGroups.FirstOrDefault() is { } placeGroup)
        {
            await ActivatePlaceGroupAsync(placeGroup);
        }
        else if (CaptureDateGroups.FirstOrDefault() is { } dateGroup)
        {
            await ActivateCaptureDateGroupAsync(dateGroup);
        }
        else
        {
            ClearActiveGroup();
        }

        StatusMessage = $"장소 {PlaceTotalGroups:N0}개 · 날짜 {CaptureDateTotalGroups:N0}개 정리 그룹";
    }

    private void ApplyOverview(PendingMemoryOverviewDto overview, bool preserveSelection)
    {
        var wasGpsSectionSelected = preserveSelection && IsGpsSectionSelected;
        var selectedGroupName = preserveSelection ? SelectedGroup?.GroupName : null;
        var includedIds = preserveSelection
            ? _loadedMediaItems
                .Where(item => item.IsIncluded)
                .Select(item => item.MediaId)
                .ToHashSet()
            : [];
        _cleanupPage = overview.Page;
        CleanupTotal = overview.Total;
        CleanupLoadedCount = overview.Items.Count;
        CanLoadMoreCleanup = overview.HasMore;
        OnPropertyChanged(nameof(CleanupProgressText));

        _loadedMediaItems = overview.Items
            .Select(item =>
            {
                var registeredPlace = item.MemorykeeperPlaceId is Guid placeId
                                      && _registeredPlacesById.TryGetValue(placeId, out var place)
                    ? place
                    : null;
                return new PendingMemoryMediaItem(item, registeredPlace);
            })
            .ToList();
        var loadedItemsById = _loadedMediaItems.ToDictionary(item => item.MediaId);

        var groupItems = overview.Groups
            .Select(group => new PendingMemoryGroupItem(group, loadedItemsById))
            .ToList();

        Groups = new ObservableCollection<PendingMemoryGroupItem>(groupItems);
        ReclassificationCandidates = new ObservableCollection<PendingMemoryMediaItem>(
            overview.ReclassificationCandidates
                .Where(item => loadedItemsById.ContainsKey(item.MediaId))
                .Select(item => loadedItemsById[item.MediaId]));
        if (includedIds.Count > 0)
        {
            foreach (var item in _loadedMediaItems)
            {
                item.IsIncluded = includedIds.Contains(item.MediaId);
            }
        }

        var groupToRestore = selectedGroupName is null
            ? null
            : groupItems.FirstOrDefault(group =>
                string.Equals(group.GroupName, selectedGroupName, StringComparison.Ordinal));
        if (wasGpsSectionSelected && ReclassificationCandidates.Count > 0)
        {
            IsGpsSectionSelected = true;
            SelectedGroup = null;
            SelectedGroupMedia = [];
            ActiveMediaItems = ReclassificationCandidates;
            _ = LoadThumbnailsAsync(ReclassificationCandidates);
        }
        else if (groupToRestore is not null)
        {
            IsGpsSectionSelected = false;
            SelectedGroup = groupToRestore;
        }
        else
        {
            IsGpsSectionSelected = false;
            if (SelectedGroup is not null)
            {
                SelectedGroup = null;
            }
            else
            {
                SelectedGroupMedia = [];
                ActiveMediaItems = new ObservableCollection<PendingMemoryMediaItem>(_loadedMediaItems);
                _ = LoadThumbnailsAsync(ActiveMediaItems);
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
            StatusMessage = $"장소 정리 대상 {CleanupLoadedCount:N0}/{CleanupTotal:N0}장";
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
        SubscribeMediaPropertyChanged(ActiveMediaItems.Distinct());
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
        OnPropertyChanged(nameof(IsSelectionMode));
        OnPropertyChanged(nameof(EmphasizePlaceRegistration));
        OnPropertyChanged(nameof(CanClearCaptureDate));
        OnPropertyChanged(nameof(SelectedDateStatusText));
        OpenCaptureDateEditorCommand.NotifyCanExecuteChanged();
        RequestClearCaptureDateCommand.NotifyCanExecuteChanged();
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
