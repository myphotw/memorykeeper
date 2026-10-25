using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemoryKeeper.App.Maps;
using MemoryKeeper.App.Services;
using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Infrastructure.Services.Api;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace MemoryKeeper.App.ViewModels;

public partial class PlaceManagementViewModel : ObservableObject
{
    private readonly MemoryKeeperPlaceService _placeService;
    private readonly IGalleryApiRepository _galleryApiRepository;
    private readonly ILocationResolver _locationResolver;
    private readonly ISettingRepository _settingRepository;
    private readonly IPlaceEditorSeedState _seedState;
    private readonly IPhotoNavigationState _photoNavigationState;
    private readonly BaseApiClient _apiClient;
    private readonly ILogger<PlaceManagementViewModel> _logger;

    private IMapController? _mapController;
    private CancellationTokenSource? _suggestCts;
    private CancellationTokenSource? _photoCountCts;
    private CancellationTokenSource? _mapPointCts;
    private CancellationTokenSource? _linkedPhotosCts;
    private int _suggestVersion;
    private int _photoCountVersion;
    private bool _suppressFormHandlers;
    private bool _applyingLocation;
    private List<Guid> _seedMediaIds = [];
    private Guid? _requestedPlaceId;
    private IReadOnlyList<PlacePickerItemDto> _allHierarchyPaths = [];
    private IReadOnlyList<MemoryKeeperPlaceDuplicateCandidateApiDto> _duplicateCandidates = [];

    [ObservableProperty]
    private ObservableCollection<PlaceDto> places = [];

    [ObservableProperty]
    private ObservableCollection<PlaceDto> filteredPlaces = [];

    [ObservableProperty]
    private ObservableCollection<PlaceDto> favoritePlaces = [];

    [ObservableProperty]
    private ObservableCollection<PlaceDto> recentPlaces = [];

    [ObservableProperty]
    private ObservableCollection<PlacePickerCountryNode> placeHierarchy = [];

    [ObservableProperty]
    private ObservableCollection<PlaceLinkedPhotoItem> linkedPhotos = [];

    [ObservableProperty]
    private ObservableCollection<PlaceDto> mergeTargets = [];

    [ObservableProperty]
    private PlaceDto? selectedMergeTarget;

    [ObservableProperty]
    private PlaceManagementFilterOption selectedFilter;

    [ObservableProperty]
    private ObservableCollection<PlaceSuggestionDto> placeSuggestions = [];

    [ObservableProperty]
    private bool hasPlaceSuggestions;

    [ObservableProperty]
    private PlaceDto? selectedPlace;

    [ObservableProperty]
    private string listSearchText = string.Empty;

    [ObservableProperty]
    private string placeSearchText = string.Empty;

    [ObservableProperty]
    private string displayName = string.Empty;

    [ObservableProperty]
    private string country = string.Empty;

    [ObservableProperty]
    private string province = string.Empty;

    [ObservableProperty]
    private string city = string.Empty;

    [ObservableProperty]
    private string district = string.Empty;

    [ObservableProperty]
    private string address = string.Empty;

    [ObservableProperty]
    private string postalCode = string.Empty;

    [ObservableProperty]
    private string googlePlaceId = string.Empty;

    [ObservableProperty]
    private string? category;

    [ObservableProperty]
    private string latitudeText = "0";

    [ObservableProperty]
    private string longitudeText = "0";

    [ObservableProperty]
    private string radiusText = "100";

    [ObservableProperty]
    private double radiusMeters = 100;

    [ObservableProperty]
    private bool isActive = true;

    [ObservableProperty]
    private bool isFavorite;

    [ObservableProperty]
    private bool isMapPickMode;

    [ObservableProperty]
    private bool isMapReady;

    [ObservableProperty]
    private int includedPhotoCount;

    [ObservableProperty]
    private string includedPhotoCountText = "반경 내 사진 0장";

    [ObservableProperty]
    private int linkedPhotoCount;

    [ObservableProperty]
    private string linkedPhotoCountText = "실제 연결 사진 0장";

    [ObservableProperty]
    private bool hasMoreLinkedPhotos;

    [ObservableProperty]
    private bool areLinkedPhotosVisible;

    [ObservableProperty]
    private string hierarchySummary = "전체 장소 0개";

    [ObservableProperty]
    private string selectedPathText = string.Empty;

    [ObservableProperty]
    private string cleanupReasonText = string.Empty;

    [ObservableProperty]
    private string statusMessage = "장소를 선택하거나 새로 등록하세요.";

    [ObservableProperty]
    private string infoBarMessage = string.Empty;

    [ObservableProperty]
    private bool isInfoBarOpen;

    [ObservableProperty]
    private InfoBarSeverity infoBarSeverity = InfoBarSeverity.Informational;

    [ObservableProperty]
    private bool isBusy;

    public IReadOnlyList<string> CategoryOptions { get; } =
        PlaceCategoryDefaults.Items.Select(item => item.Category).ToList();

    public IReadOnlyList<PlaceManagementFilterOption> FilterOptions { get; } =
    [
        new(PlaceManagementFilterKind.All, "전체 장소"),
        new(PlaceManagementFilterKind.Duplicates, "중복 후보"),
        new(PlaceManagementFilterKind.Incomplete, "정보 부족"),
        new(PlaceManagementFilterKind.ZeroPhotos, "사진 0장"),
        new(PlaceManagementFilterKind.Recent, "최근 장소"),
        new(PlaceManagementFilterKind.Favorites, "즐겨찾기"),
    ];

    public XamlRoot? HostXamlRoot { get; set; }

    public event EventHandler? BackRequested;

    public void RequestSelection(Guid placeId)
    {
        if (placeId != Guid.Empty)
        {
            _requestedPlaceId = placeId;
        }
    }

    public PlaceManagementViewModel(
        MemoryKeeperPlaceService placeService,
        IGalleryApiRepository galleryApiRepository,
        ILocationResolver locationResolver,
        ISettingRepository settingRepository,
        IPlaceEditorSeedState seedState,
        IPhotoNavigationState photoNavigationState,
        BaseApiClient apiClient,
        ILogger<PlaceManagementViewModel> logger)
    {
        _placeService = placeService;
        _galleryApiRepository = galleryApiRepository;
        _locationResolver = locationResolver;
        _settingRepository = settingRepository;
        _seedState = seedState;
        _photoNavigationState = photoNavigationState;
        _apiClient = apiClient;
        _logger = logger;
        selectedFilter = FilterOptions[0];
    }

    public void AttachMap(IMapController mapController)
    {
        DetachMap();
        _mapController = mapController;
        _mapController.Ready += OnMapReady;
        _mapController.MapClicked += OnMapClicked;
        _mapController.EditableMarkerDragEnded += OnEditableMarkerDragEnded;
        IsMapReady = mapController.IsReady;
    }

    public void DetachMap()
    {
        if (_mapController is not null)
        {
            _mapController.Ready -= OnMapReady;
            _mapController.MapClicked -= OnMapClicked;
            _mapController.EditableMarkerDragEnded -= OnEditableMarkerDragEnded;
        }

        _mapController = null;
        IsMapReady = false;
    }

    partial void OnSelectedPlaceChanged(PlaceDto? value)
    {
        if (_suppressFormHandlers)
        {
            return;
        }

        if (value is null)
        {
            _linkedPhotosCts?.Cancel();
            LinkedPhotos.Clear();
            LinkedPhotoCount = 0;
            LinkedPhotoCountText = "실제 연결 사진 0장";
            SelectedPathText = string.Empty;
            CleanupReasonText = string.Empty;
            return;
        }

        ApplyPlaceToForm(value);
        UpdateSelectedPlaceContext(value);
        _ = SyncEditablePinAsync();
        _ = RefreshIncludedPhotoCountAsync();
        _ = LoadLinkedPhotosAsync(reset: true);
    }

    partial void OnListSearchTextChanged(string value) => ApplyListFilter();

    partial void OnSelectedFilterChanged(PlaceManagementFilterOption value) => ApplyListFilter();

    partial void OnPlaceSearchTextChanged(string value) => _ = SuggestPlacesAsync(value);

    partial void OnCategoryChanged(string? value)
    {
        if (_suppressFormHandlers || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var recommended = PlaceCategoryDefaults.GetRecommendedRadius(value);
        SetRadiusInternal(recommended, updateMap: true, refreshCount: true);
    }

    partial void OnRadiusTextChanged(string value)
    {
        if (_suppressFormHandlers)
        {
            return;
        }

        if (!TryParseDouble(value, out var radius) || radius <= 0)
        {
            return;
        }

        if (Math.Abs(RadiusMeters - radius) < 0.01)
        {
            return;
        }

        _suppressFormHandlers = true;
        RadiusMeters = ClampRadius(radius);
        _suppressFormHandlers = false;
        _ = UpdateMapRadiusAsync(RadiusMeters);
        _ = RefreshIncludedPhotoCountAsync();
    }

    partial void OnRadiusMetersChanged(double value)
    {
        if (_suppressFormHandlers)
        {
            return;
        }

        var clamped = ClampRadius(value);
        _suppressFormHandlers = true;
        if (Math.Abs(RadiusMeters - clamped) > 0.01)
        {
            RadiusMeters = clamped;
        }

        RadiusText = FormatRadius(clamped);
        _suppressFormHandlers = false;
        _ = UpdateMapRadiusAsync(clamped);
        _ = RefreshIncludedPhotoCountAsync();
    }

    partial void OnIsMapPickModeChanged(bool value)
    {
        _ = SetMapPickModeAsync(value);
    }

    [RelayCommand]
    private void GoBack() => BackRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private async Task InitializeMapAsync()
    {
        if (_mapController is null)
        {
            return;
        }

        try
        {
            var apiKey = await MapDisplayCredentialProvider.GetAsync(_settingRepository);
            await _mapController.InitializeAsync(apiKey);
            IsMapReady = true;
            IsMapPickMode = true;
            await _mapController.EnableMapClickAsync(true);
            await SyncEditablePinAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize place management map.");
            ShowError($"지도 초기화 실패: {ex.Message}");
            IsMapReady = false;
        }
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        await RunBusyAsync(async () =>
        {
            var previousSelection = SelectedPlace?.Id;
            await ReloadListsAsync();

            if (TryApplyRequestedSelection())
            {
                return;
            }

            if (_seedState.TryConsumeSeed(out var lat, out var lng, out var mediaIds))
            {
                ClearFormInternal();
                _seedMediaIds = mediaIds.ToList();
                await ApplyCoordinatesAsync(lat, lng, reverseGeocode: true, updateMap: true);
                StatusMessage = _seedMediaIds.Count > 0
                    ? $"선택한 사진 {_seedMediaIds.Count}장만 이 장소에 연결됩니다. 저장하세요."
                    : "선택한 사진 위치에서 새 장소를 등록하세요.";
                return;
            }

            if (previousSelection.HasValue && SelectedPlace?.Id == previousSelection.Value)
            {
                StatusMessage = $"장소 {Places.Count}개를 새로 고쳤습니다.";
                return;
            }

            SelectedPlace = FilteredPlaces.FirstOrDefault() ?? Places.FirstOrDefault();
            if (SelectedPlace is null)
            {
                ClearFormInternal();
                StatusMessage = "등록된 장소가 없습니다. 검색하거나 지도에서 선택하세요.";
            }
            else
            {
                StatusMessage = $"장소 {Places.Count}개 로드됨.";
            }
        });
    }

    private bool TryApplyRequestedSelection()
    {
        if (_requestedPlaceId is not Guid requestedPlaceId)
        {
            return false;
        }

        _requestedPlaceId = null;
        var requestedPlace = Places.FirstOrDefault(place => place.Id == requestedPlaceId);
        if (requestedPlace is null)
        {
            ClearFormInternal();
            ListSearchText = string.Empty;
            StatusMessage = "요청한 장소를 찾을 수 없습니다. 장소 목록을 확인하세요.";
            return true;
        }

        ListSearchText = requestedPlace.DisplayName;
        SelectedPlace = FilteredPlaces.FirstOrDefault(place => place.Id == requestedPlaceId)
                        ?? requestedPlace;
        StatusMessage = $"'{requestedPlace.DisplayName}' 장소를 선택했습니다.";
        return true;
    }

    [RelayCommand]
    private void ClearForm()
    {
        ClearFormInternal();
        _ = ClearMapPinAsync();
        IncludedPhotoCount = 0;
        IncludedPhotoCountText = "반경 내 사진 0장";
        StatusMessage = "새 장소 입력 모드입니다.";
    }

    [RelayCommand]
    private async Task SelectSuggestionAsync(PlaceSuggestionDto? suggestion)
    {
        if (suggestion is null || string.IsNullOrWhiteSpace(suggestion.PlaceId))
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var location = await _locationResolver.ResolvePlaceIdAsync(suggestion.PlaceId)
                ?? throw new InvalidOperationException("장소 상세를 가져오지 못했습니다.");

            ApplyLocationResult(location);
            if (!string.IsNullOrWhiteSpace(location.PlaceType))
            {
                Category = location.PlaceType;
            }

            PlaceSuggestions.Clear();
            HasPlaceSuggestions = false;
            PlaceSearchText = location.DisplayName;
            await SyncEditablePinAsync();
            await RefreshIncludedPhotoCountAsync();
            StatusMessage =
                $"장소 자동완성 적용: {location.Latitude:F5}, {location.Longitude:F5}. 확인 후 저장하세요.";
        });
    }

    [RelayCommand]
    private void SelectFavorite(PlaceDto? place)
    {
        if (place is null)
        {
            return;
        }

        SelectedPlace = Places.FirstOrDefault(item => item.Id == place.Id) ?? place;
    }

    [RelayCommand]
    private void SelectRecent(PlaceDto? place) => SelectFavorite(place);

    [RelayCommand]
    private void SelectHierarchyPlace(PlacePickerItemDto? item)
    {
        if (item is null)
        {
            return;
        }

        SelectedPlace = Places.FirstOrDefault(place => place.Id == item.Id);
        SelectedPathText = item.FullPath;
    }

    [RelayCommand]
    private async Task ReverseGeocodeCurrentCoordinatesAsync()
    {
        if (!TryParseDouble(LatitudeText, out var latitude)
            || !TryParseDouble(LongitudeText, out var longitude))
        {
            ShowError("주소를 조회할 위도·경도를 확인하세요.");
            return;
        }

        await RunBusyAsync(async () =>
        {
            var originalName = DisplayName;
            var originalLatitude = LatitudeText;
            var originalLongitude = LongitudeText;
            var originalRadius = RadiusText;
            var location = await _locationResolver.ResolveAsync(latitude, longitude);
            if (location is null)
            {
                ShowError("현재 좌표의 주소를 찾지 못했습니다. 기존 정보는 유지됩니다.");
                return;
            }

            _suppressFormHandlers = true;
            Country = FirstNotBlank(location.Country, Country);
            Province = FirstNotBlank(location.Province, Province);
            City = FirstNotBlank(location.City, City);
            District = FirstNotBlank(location.District, District);
            Address = FirstNotBlank(location.Address, Address);
            PostalCode = FirstNotBlank(location.PostalCode, PostalCode);
            DisplayName = originalName;
            LatitudeText = originalLatitude;
            LongitudeText = originalLongitude;
            RadiusText = originalRadius;
            _suppressFormHandlers = false;
            StatusMessage = "현재 좌표에서 주소를 다시 조회했습니다. 내용을 확인한 뒤 저장하세요.";
        });
    }

    [RelayCommand]
    private async Task ShowLinkedPhotosAsync()
    {
        if (SelectedPlace is null)
        {
            ShowError("연결 사진을 확인할 장소를 선택하세요.");
            return;
        }

        AreLinkedPhotosVisible = true;
        await LoadLinkedPhotosAsync(reset: true);
    }

    [RelayCommand]
    private Task LoadMoreLinkedPhotosAsync() => LoadLinkedPhotosAsync(reset: false);

    [RelayCommand]
    private void OpenLinkedPhoto(PlaceLinkedPhotoItem? item)
    {
        if (item is null || item.MediaId == Guid.Empty)
        {
            return;
        }

        _photoNavigationState.RequestOpenViewer(
            item.MediaId,
            LinkedPhotos.Select(photo => photo.MediaId).Where(id => id != Guid.Empty).ToList(),
            "place-management");
    }

    [RelayCommand]
    private async Task MergeIntoSelectedTargetAsync()
    {
        var source = SelectedPlace;
        var target = SelectedMergeTarget;
        if (source is null || target is null)
        {
            ShowError("병합할 Source 장소와 유지할 Target 장소를 선택하세요.");
            return;
        }

        if (source.Id == target.Id)
        {
            ShowError("동일한 등록 장소 UUID의 여러 경로는 중복 장소가 아닙니다. 서로 다른 장소를 선택하세요.");
            return;
        }

        await RunBusyAsync(async () =>
        {
            var preview = await _placeService.GetMergePreviewAsync(source.Id, target.Id);
            var sourcePath = FirstNotBlank(preview.SourcePath, FindPrimaryPath(source.Id), source.DisplayName);
            var targetPath = FirstNotBlank(preview.TargetPath, FindPrimaryPath(target.Id), target.DisplayName);
            var movedCount = preview.MovedPhotoCount > 0
                ? preview.MovedPhotoCount
                : preview.SourceLinkedPhotoCount;
            var sourceRevision = preview.SourceRevision > 0 ? preview.SourceRevision : source.Revision;
            var targetRevision = preview.TargetRevision > 0 ? preview.TargetRevision : target.Revision;
            var confirmed = await UserFeedback.ConfirmAsync(
                HostXamlRoot,
                "장소 병합 최종 확인",
                $"Source (병합 후 비활성화)\n{sourcePath}\n좌표 {source.Latitude:F6}, {source.Longitude:F6} · 연결 {Math.Max(movedCount, source.MediaCount)}장 · Revision {sourceRevision}\n\n"
                + $"Target (유지)\n{targetPath}\n좌표 {target.Latitude:F6}, {target.Longitude:F6} · 연결 {Math.Max(preview.TargetLinkedPhotoCount, target.MediaCount)}장 · Revision {targetRevision}\n\n"
                + "Source의 연결 사진만 Target으로 이동합니다. Target 장소 정보는 유지되며 Source 정보로 덮어쓰지 않습니다.",
                primaryText: "병합");
            if (!confirmed)
            {
                StatusMessage = "장소 병합이 취소되었습니다.";
                return;
            }

            var result = await _placeService.MergePlacesAsync(new MemoryKeeperPlaceMergeApiRequest
            {
                SourcePlaceId = source.Id,
                TargetPlaceId = target.Id,
                ExpectedSourceRevision = sourceRevision,
                ExpectedTargetRevision = targetRevision,
            });
            var targetId = result.TargetPlaceId == Guid.Empty ? target.Id : result.TargetPlaceId;
            ShowSuccess($"'{source.DisplayName}'을(를) '{target.DisplayName}'에 병합했습니다. 사진 {result.MovedPhotoCount:N0}장이 이동했습니다.");
            SelectedFilter = FilterOptions[0];
            ListSearchText = string.Empty;
            await ReloadAndSelectAsync(targetId);
        });
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync()
    {
        if (SelectedPlace is null)
        {
            IsFavorite = !IsFavorite;
            return;
        }

        await RunBusyAsync(async () =>
        {
            var updated = await _placeService.SetPlaceFavoriteAsync(SelectedPlace, !SelectedPlace.IsFavorite);
            ShowSuccess(updated.IsFavorite
                ? $"'{updated.DisplayName}'을(를) 즐겨찾기에 추가했습니다."
                : $"'{updated.DisplayName}' 즐겨찾기를 해제했습니다.");
            await ReloadAndSelectAsync(updated.Id);
        });
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (SelectedPlace is null)
        {
            await CreateAsync();
        }
        else
        {
            await UpdateAsync();
        }
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        CancelMapPointApply();
        await RunBusyAsync(async () =>
        {
            var request = BuildCreateRequest();
            var confirmed = await PlaceOverlapPrompt.ConfirmIfNeededAsync(
                HostXamlRoot,
                _placeService,
                request.DisplayName,
                request.Latitude,
                request.Longitude,
                request.Radius ?? PlaceCategoryDefaults.GetRecommendedRadius(request.Category));
            if (!confirmed)
            {
                StatusMessage = "장소 저장이 취소되었습니다.";
                return;
            }

            var created = await _placeService.CreatePlaceAsync(new CreatePlaceRequest
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
                Radius = request.Radius,
                IsActive = request.IsActive,
                IsFavorite = request.IsFavorite
            });
            var assigned = await AssignSeedMediaAsync(created.Id);
            var reclass = await _placeService.ReclassifyMediaAsync(created.Id, reassignFromOtherPlaces: true);
            ShowSuccess(BuildSaveSuccessMessage(created.DisplayName, assigned, reclass, isCreate: true));
            await ReloadAndSelectAsync(created.Id);
        });
    }

    [RelayCommand]
    private async Task UpdateAsync()
    {
        if (SelectedPlace is null)
        {
            ShowError("수정할 장소를 선택하세요.");
            return;
        }

        CancelMapPointApply();
        await RunBusyAsync(async () =>
        {
            var original = SelectedPlace;
            var request = BuildUpdateRequest(original.Id);
            if (PlaceHierarchyChangePolicy.HasCanonicalGeographyChanged(original, request)
                && !await ConfirmHierarchyImpactAsync(original))
            {
                StatusMessage = "등록 장소의 위치 정보 수정이 취소되었습니다.";
                return;
            }

            var operation = await _placeService.UpdateWithRadiusImpactAsync(
                original,
                request,
                (impact, token) => PlaceOverlapPrompt.ConfirmImpactIfNeededAsync(
                    HostXamlRoot,
                    request.DisplayName,
                    impact,
                    token));
            if (operation.Cancelled)
            {
                StatusMessage = "장소 수정이 취소되었습니다.";
                return;
            }

            var updated = operation.UpdatedPlace
                ?? throw new InvalidOperationException("장소 수정 결과가 비어 있습니다.");
            var assigned = await AssignSeedMediaAsync(updated.Id);
            var message = BuildSaveSuccessMessage(
                updated.DisplayName,
                assigned,
                operation.Reclassification,
                isCreate: false);
            if (operation.ReclassificationSkippedBecauseInactive)
            {
                message += " · 비활성 장소의 기존 사진 연결은 유지됩니다.";
            }

            ShowSuccess(message);
            await ReloadAndSelectAsync(updated.Id);
        });
    }

    private async Task<bool> ConfirmHierarchyImpactAsync(PlaceDto place)
    {
        // The page assigns XamlRoot before commands can run. Without it we cannot satisfy the
        // explicit-confirmation contract, so do not mutate the registered place.
        if (HostXamlRoot is null)
        {
            _logger.LogWarning("Place hierarchy impact prompt unavailable. PlaceId={PlaceId}", place.Id);
            ShowError("위치 정보 변경 확인 창을 열 수 없습니다. 화면을 다시 연 뒤 시도하세요.");
            return false;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = HostXamlRoot,
            Title = "등록 장소 위치 정보 수정",
            Content = PlaceHierarchyChangePolicy.BuildImpactMessage(place.DisplayName, place.MediaCount),
            PrimaryButtonText = "수정",
            CloseButtonText = "취소",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private static string BuildSaveSuccessMessage(
        string displayName,
        int seedAssigned,
        PlaceReclassificationResult reclass,
        bool isCreate)
    {
        var verb = isCreate ? "저장" : "수정";
        var parts = new List<string> { $"장소 '{displayName}'을(를) {verb}했습니다." };
        if (seedAssigned > 0)
        {
            parts.Add($"선택 사진 {seedAssigned}장 연결");
        }

        if (reclass.AssignedCount > 0)
        {
            parts.Add(
                reclass.ReassignedFromOtherCount > 0
                    ? $"반경 내 {reclass.AssignedCount}장 연결(다른 장소에서 {reclass.ReassignedFromOtherCount}장 이동)"
                    : $"반경 내 {reclass.AssignedCount}장 연결");
        }

        return string.Join(" · ", parts);
    }

    private async Task<int> AssignSeedMediaAsync(Guid placeId)
    {
        if (_seedMediaIds.Count == 0)
        {
            return 0;
        }

        var mediaIds = _seedMediaIds.ToList();
        _seedMediaIds.Clear();

        var assigned = 0;
        foreach (var mediaId in mediaIds)
        {
            var detail = await _galleryApiRepository.GetPhotoAsync(mediaId);
            await _placeService.AssignFilePlaceAsync(mediaId, placeId, detail.PlaceRevision ?? 0);
            assigned++;
        }

        return assigned;
    }

    [RelayCommand]
    private async Task ToggleActiveAsync()
    {
        if (SelectedPlace is null)
        {
            ShowError("활성 상태를 변경할 장소를 선택하세요.");
            return;
        }

        await RunBusyAsync(async () =>
        {
            var updated = await _placeService.SetPlaceActiveAsync(SelectedPlace, !SelectedPlace.IsActive);
            ShowSuccess(updated.IsActive
                ? $"장소 '{updated.DisplayName}'을(를) 활성화했습니다."
                : $"장소 '{updated.DisplayName}'을(를) 비활성화했습니다.");
            await ReloadAndSelectAsync(updated.Id);
        });
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (SelectedPlace is null)
        {
            ShowError("삭제할 장소를 선택하세요.");
            return;
        }

        var place = SelectedPlace;
        if (LinkedPhotoCount > 0 || place.MediaCount > 0)
        {
            ShowError($"'{place.DisplayName}'에는 실제 연결 사진 {Math.Max(LinkedPhotoCount, place.MediaCount)}장이 있어 삭제할 수 없습니다. 다른 장소와 병합하세요.");
            return;
        }

        var confirmed = await UserFeedback.ConfirmAsync(
            HostXamlRoot,
            "장소 삭제",
            $"사용하지 않는 장소 '{place.DisplayName}'을(를) 삭제하시겠습니까?\n이 작업은 장소 자체만 삭제하며 사진 연결을 자동으로 해제하지 않습니다.",
            primaryText: "삭제");

        if (!confirmed)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            (bool Succeeded, string Message, int MediaCount) result;
            try
            {
                result = await _placeService.DeletePlaceAsync(place.Id, place.Revision);
            }
            catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                ShowError(BuildDeleteBlockedMessage(ex));
                return;
            }

            ShowSuccess(result.Message);
            await ReloadListsAsync();
            SelectedPlace = FilteredPlaces.FirstOrDefault();
            if (SelectedPlace is null)
            {
                ClearFormInternal();
                await ClearMapPinAsync();
            }
        });
    }

    [RelayCommand]
    private void EnableMapPick()
    {
        IsMapPickMode = true;
        StatusMessage = "지도를 클릭하여 위치를 선택하세요. Marker를 드래그할 수 있습니다.";
    }

    private CreatePlaceRequest BuildCreateRequest() =>
        new()
        {
            DisplayName = DisplayName,
            Country = Country,
            Province = Province,
            City = City,
            District = District,
            Address = Address,
            PostalCode = PostalCode,
            GooglePlaceId = string.IsNullOrWhiteSpace(GooglePlaceId) ? null : GooglePlaceId,
            Category = Category,
            Latitude = ParseCoordinate(LatitudeText, nameof(LatitudeText)),
            Longitude = ParseCoordinate(LongitudeText, nameof(LongitudeText)),
            Radius = ParseRadius(RadiusText),
            IsActive = IsActive,
            IsFavorite = IsFavorite
        };

    private UpdatePlaceRequest BuildUpdateRequest(Guid id) =>
        new()
        {
            Id = id,
            Revision = SelectedPlace?.Revision ?? 0,
            DisplayName = DisplayName,
            CanonicalName = SelectedPlace?.CanonicalName,
            Country = Country,
            Province = Province,
            City = City,
            District = District,
            Address = Address,
            PostalCode = PostalCode,
            GooglePlaceId = string.IsNullOrWhiteSpace(GooglePlaceId) ? null : GooglePlaceId,
            Category = Category,
            Latitude = ParseCoordinate(LatitudeText, nameof(LatitudeText)),
            Longitude = ParseCoordinate(LongitudeText, nameof(LongitudeText)),
            Radius = ParseRadius(RadiusText),
            IsActive = IsActive,
            IsFavorite = IsFavorite,
            ReclassifyMedia = false
        };

    private void ApplyPlaceToForm(PlaceDto place)
    {
        _suppressFormHandlers = true;
        DisplayName = place.DisplayName;
        Country = place.Country;
        Province = place.Province;
        City = place.City;
        District = place.District;
        Address = place.Address;
        PostalCode = place.PostalCode;
        GooglePlaceId = place.GooglePlaceId ?? string.Empty;
        Category = place.Category;
        LatitudeText = place.Latitude.ToString("F6", CultureInfo.InvariantCulture);
        LongitudeText = place.Longitude.ToString("F6", CultureInfo.InvariantCulture);
        RadiusMeters = place.Radius;
        RadiusText = FormatRadius(place.Radius);
        IsActive = place.IsActive;
        IsFavorite = place.IsFavorite;
        _suppressFormHandlers = false;
    }

    private void ApplyLocationResult(LocationResult location)
    {
        _suppressFormHandlers = true;
        if (!string.IsNullOrWhiteSpace(location.DisplayName))
        {
            DisplayName = location.DisplayName;
        }

        Country = location.Country;
        Province = location.Province;
        City = location.City;
        District = location.District;
        Address = string.IsNullOrWhiteSpace(location.Address) ? location.DisplayName : location.Address;
        PostalCode = location.PostalCode;
        GooglePlaceId = location.PlaceId ?? string.Empty;
        LatitudeText = location.Latitude.ToString("F6", CultureInfo.InvariantCulture);
        LongitudeText = location.Longitude.ToString("F6", CultureInfo.InvariantCulture);
        _suppressFormHandlers = false;
    }

    private void ClearFormInternal()
    {
        _suppressFormHandlers = true;
        SelectedPlace = null;
        _seedMediaIds.Clear();
        DisplayName = string.Empty;
        Country = string.Empty;
        Province = string.Empty;
        City = string.Empty;
        District = string.Empty;
        Address = string.Empty;
        PostalCode = string.Empty;
        GooglePlaceId = string.Empty;
        Category = null;
        LatitudeText = "0";
        LongitudeText = "0";
        RadiusMeters = 100;
        RadiusText = "100";
        IsActive = true;
        IsFavorite = false;
        PlaceSearchText = string.Empty;
        PlaceSuggestions.Clear();
        HasPlaceSuggestions = false;
        AreLinkedPhotosVisible = false;
        _linkedPhotosCts?.Cancel();
        LinkedPhotos.Clear();
        LinkedPhotoCount = 0;
        LinkedPhotoCountText = "실제 연결 사진 0장";
        _suppressFormHandlers = false;
    }

    private async Task ReloadListsAsync()
    {
        var selectedId = SelectedPlace?.Id;
        var itemsTask = _placeService.GetPlaceListAsync();
        var hierarchyTask = _placeService.GetPlaceHierarchyCatalogAsync();
        var duplicateTask = _placeService.GetDuplicateCandidatesAsync();
        await Task.WhenAll(itemsTask, hierarchyTask, duplicateTask);

        var items = await itemsTask;
        var hierarchy = await hierarchyTask;
        var duplicates = await duplicateTask;

        Places = new ObservableCollection<PlaceDto>(items);
        FavoritePlaces = new ObservableCollection<PlaceDto>(items.Where(place => place.IsFavorite));
        RecentPlaces = new ObservableCollection<PlaceDto>(items
            .Where(place => place.LastUsedAt.HasValue)
            .OrderByDescending(place => place.LastUsedAt)
            .Take(10));
        _duplicateCandidates = duplicates.Items;
        var projection = PlacePickerHierarchyProjection.Build(items, hierarchy.Items, includeInactive: true);
        _allHierarchyPaths = projection.AllPathItems;
        var totalPlaces = hierarchy.TotalPlaces > 0 ? hierarchy.TotalPlaces : items.Count;
        var totalPaths = hierarchy.TotalPaths > 0 ? hierarchy.TotalPaths : projection.AllPathItems.Count;
        HierarchySummary = totalPaths == totalPlaces
            ? $"전체 장소 {totalPlaces:N0}개"
            : $"전체 장소 {totalPlaces:N0}개 · 경로 {totalPaths:N0}개";
        ApplyListFilter();

        if (selectedId.HasValue)
        {
            SelectedPlace = Places.FirstOrDefault(place => place.Id == selectedId.Value);
        }
    }

    private async Task ReloadAndSelectAsync(Guid placeId)
    {
        await ReloadListsAsync();
        var place = Places.FirstOrDefault(item => item.Id == placeId);
        if (place is not null)
        {
            SelectedPlace = FilteredPlaces.FirstOrDefault(item => item.Id == placeId) ?? place;
            await SyncEditablePinAsync();
            await RefreshIncludedPhotoCountAsync();
        }
        else
        {
            SelectedPlace = null;
        }
    }

    private void ApplyListFilter()
    {
        IEnumerable<PlaceDto> query = Places;
        var duplicateIds = _duplicateCandidates
            .Where(candidate =>
                (candidate.SourcePlace?.Id ?? candidate.SourcePlaceId) !=
                (candidate.TargetPlace?.Id ?? candidate.TargetPlaceId))
            .SelectMany(candidate => new[]
            {
                candidate.SourcePlace?.Id ?? candidate.SourcePlaceId,
                candidate.TargetPlace?.Id ?? candidate.TargetPlaceId,
            })
            .Where(id => id != Guid.Empty)
            .ToHashSet();
        var incompleteIds = _allHierarchyPaths
            .Where(path => !path.LocationHierarchyComplete || !string.IsNullOrWhiteSpace(path.LocationCleanupReason))
            .Select(path => path.Id)
            .ToHashSet();
        foreach (var placeId in _allHierarchyPaths.GroupBy(path => path.Id)
                     .Where(group => group.Count() > 1)
                     .Select(group => group.Key))
        {
            incompleteIds.Add(placeId);
        }
        var photoCounts = _allHierarchyPaths
            .GroupBy(path => path.Id)
            .ToDictionary(group => group.Key, group => group.Sum(path => Math.Max(0, path.PhotoCount)));
        query = SelectedFilter.Kind switch
        {
            PlaceManagementFilterKind.Duplicates => query.Where(place => duplicateIds.Contains(place.Id)),
            PlaceManagementFilterKind.Incomplete => query.Where(place => incompleteIds.Contains(place.Id)),
            PlaceManagementFilterKind.ZeroPhotos => query.Where(place =>
                !photoCounts.TryGetValue(place.Id, out var count) ? place.MediaCount == 0 : count == 0),
            PlaceManagementFilterKind.Recent => query.Where(place => place.LastUsedAt.HasValue),
            PlaceManagementFilterKind.Favorites => query.Where(place => place.IsFavorite),
            _ => query,
        };

        if (!string.IsNullOrWhiteSpace(ListSearchText))
        {
            var term = ListSearchText.Trim();
            query = query.Where(place =>
                place.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || place.Address.Contains(term, StringComparison.OrdinalIgnoreCase)
                || place.City.Contains(term, StringComparison.OrdinalIgnoreCase)
                || place.Province.Contains(term, StringComparison.OrdinalIgnoreCase)
                || place.District.Contains(term, StringComparison.OrdinalIgnoreCase)
                || _allHierarchyPaths.Any(path => path.Id == place.Id
                                                   && (path.FullPath.Contains(term, StringComparison.OrdinalIgnoreCase)
                                                       || path.Country.Contains(term, StringComparison.OrdinalIgnoreCase)
                                                       || path.Region.Contains(term, StringComparison.OrdinalIgnoreCase))));
        }

        var filtered = (SelectedFilter.Kind == PlaceManagementFilterKind.Recent
                ? query.OrderByDescending(place => place.LastUsedAt)
                : query.OrderBy(place => place.DisplayName, StringComparer.CurrentCultureIgnoreCase))
            .ToList();
        var filteredIds = filtered.Select(place => place.Id).ToHashSet();
        FilteredPlaces = new ObservableCollection<PlaceDto>(filtered);
        MergeTargets = new ObservableCollection<PlaceDto>(Places.Where(place =>
            SelectedPlace is null || place.Id != SelectedPlace.Id));
        PlaceHierarchy = new ObservableCollection<PlacePickerCountryNode>(BuildHierarchy(
            _allHierarchyPaths.Where(path => filteredIds.Contains(path.Id)).ToList()));
    }

    private void UpdateSelectedPlaceContext(PlaceDto place)
    {
        var paths = _allHierarchyPaths.Where(path => path.Id == place.Id).ToList();
        var primary = paths
            .OrderByDescending(path => path.LocationHierarchyComplete)
            .ThenByDescending(path => path.PhotoCount)
            .ThenBy(path => path.FullPath, StringComparer.Ordinal)
            .FirstOrDefault();
        SelectedPathText = primary?.FullPath ?? place.DisplayName;
        CleanupReasonText = BuildCleanupReasonText(paths);
        var duplicate = _duplicateCandidates.FirstOrDefault(candidate =>
            (candidate.SourcePlace?.Id ?? candidate.SourcePlaceId) == place.Id
            || (candidate.TargetPlace?.Id ?? candidate.TargetPlaceId) == place.Id);
        if (duplicate is not null)
        {
            var distance = duplicate.DistanceM.HasValue ? $" · 거리 {duplicate.DistanceM.Value:N0}m" : string.Empty;
            CleanupReasonText += $" · 중복 후보: {FirstNotBlank(duplicate.Reason, "서버 후보")}{distance}";
        }
        LinkedPhotoCount = Math.Max(0, place.MediaCount);
        LinkedPhotoCountText = $"실제 연결 사진 {LinkedPhotoCount:N0}장";
        MergeTargets = new ObservableCollection<PlaceDto>(Places.Where(candidate => candidate.Id != place.Id));
        SelectedMergeTarget = SuggestedMergeTarget(place.Id);
    }

    private PlaceDto? SuggestedMergeTarget(Guid sourceId)
    {
        var candidate = _duplicateCandidates.FirstOrDefault(item =>
            (item.SourcePlace?.Id ?? item.SourcePlaceId) == sourceId
            && (item.TargetPlace?.Id ?? item.TargetPlaceId) != sourceId);
        var targetId = candidate?.TargetPlace?.Id ?? candidate?.TargetPlaceId ?? Guid.Empty;
        return targetId == Guid.Empty ? null : Places.FirstOrDefault(place => place.Id == targetId);
    }

    private async Task LoadLinkedPhotosAsync(bool reset)
    {
        var place = SelectedPlace;
        if (place is null || (!reset && !HasMoreLinkedPhotos))
        {
            return;
        }

        if (reset)
        {
            _linkedPhotosCts?.Cancel();
            _linkedPhotosCts?.Dispose();
            _linkedPhotosCts = new CancellationTokenSource();
            LinkedPhotos.Clear();
            HasMoreLinkedPhotos = false;
        }

        var token = _linkedPhotosCts?.Token ?? CancellationToken.None;
        var expectedPlaceId = place.Id;
        try
        {
            var page = await _placeService.GetLinkedPhotosAsync(
                place.Id,
                limit: 50,
                offset: reset ? 0 : LinkedPhotos.Count,
                token);
            if (token.IsCancellationRequested || SelectedPlace?.Id != expectedPlaceId)
            {
                return;
            }

            foreach (var raw in page.Items)
            {
                var mediaId = BackendFileIdCodec.ToGuid(raw.FileId);
                var thumbnail = BackendMediaUrlResolver.ResolveThumbnailUrl(
                    _apiClient.ApiBaseUrl,
                    raw.FileId,
                    raw.ThumbnailUrl);
                var preview = BackendMediaUrlResolver.ToAbsoluteUrl(_apiClient.ApiBaseUrl, raw.PreviewUrl);
                LinkedPhotos.Add(new PlaceLinkedPhotoItem
                {
                    MediaId = mediaId,
                    FileId = raw.FileId,
                    Filename = raw.Filename,
                    ThumbnailUrl = thumbnail ?? preview,
                    CaptureDateText = FormatCaptureDate(raw),
                });
            }

            LinkedPhotoCount = Math.Max(page.TotalCount, LinkedPhotos.Count);
            LinkedPhotoCountText = $"실제 연결 사진 {LinkedPhotoCount:N0}장";
            HasMoreLinkedPhotos = page.HasMore || LinkedPhotos.Count < page.TotalCount;
            if (AreLinkedPhotosVisible)
            {
                await LoadLinkedPhotoImagesAsync(page.Items.Count, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Selection changed; do not apply stale photo rows.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load linked photos for place {PlaceId}.", place.Id);
            ShowError($"연결 사진을 불러오지 못했습니다: {ex.Message}");
        }
    }

    private async Task LoadLinkedPhotoImagesAsync(int addedCount, CancellationToken cancellationToken)
    {
        if (addedCount <= 0)
        {
            return;
        }

        var items = LinkedPhotos.Skip(Math.Max(0, LinkedPhotos.Count - addedCount)).ToList();
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            item.ThumbnailImage = await HttpImageLoader.LoadAsync(
                item.ThumbnailUrl,
                _logger,
                "place-linked-photo",
                cancellationToken);
        }
    }

    private static string FormatCaptureDate(MemoryKeeperPlacePhotoApiDto photo)
    {
        if (string.Equals(photo.EffectiveCapturePrecision, "YEAR", StringComparison.OrdinalIgnoreCase)
            && photo.EffectiveCaptureYear is > 0)
        {
            return $"{photo.EffectiveCaptureYear}년";
        }

        if (photo.EffectiveCaptureDate is DateOnly date)
        {
            return date.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture);
        }

        return photo.EffectiveCaptureDatetime?.ToString("yyyy.MM.dd HH:mm", CultureInfo.InvariantCulture) ?? "-";
    }

    private string FindPrimaryPath(Guid placeId) =>
        _allHierarchyPaths
            .Where(path => path.Id == placeId)
            .OrderByDescending(path => path.LocationHierarchyComplete)
            .ThenByDescending(path => path.PhotoCount)
            .Select(path => path.FullPath)
            .FirstOrDefault() ?? string.Empty;

    private static IReadOnlyList<PlacePickerCountryNode> BuildHierarchy(IReadOnlyList<PlacePickerItemDto> items) =>
        items
            .GroupBy(item => item.Country, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(country => new PlacePickerCountryNode
            {
                Title = country.Key,
                Regions = country
                    .GroupBy(item => item.Region, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
                    .Select(region => new PlacePickerRegionNode
                    {
                        Title = region.Key,
                        Places = region
                            .OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                            .ThenBy(item => item.FullPath, StringComparer.Ordinal)
                            .ToList(),
                    })
                    .ToList(),
            })
            .ToList();

    private static string BuildCleanupReasonText(IReadOnlyList<PlacePickerItemDto> paths)
    {
        var reasons = paths
            .Where(path => !path.LocationHierarchyComplete || !string.IsNullOrWhiteSpace(path.LocationCleanupReason))
            .Select(path => path.LocationCleanupReason switch
            {
                "COUNTRY_MISSING" => "국가 정보 없음",
                "REGION_MISSING" => "지역 정보 없음",
                "PLACE_NAME_MISSING" or "PLACE_MISSING" => "장소명 없음",
                _ => string.IsNullOrWhiteSpace(path.LocationCleanupReason) ? "위치 계층 확인 필요" : path.LocationCleanupReason,
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (paths.Count > 1)
        {
            reasons.Add($"동일 UUID의 canonical 경로 {paths.Count}개");
        }

        return reasons.Count == 0 ? "위치 계층 정보 정상" : string.Join(" · ", reasons.Distinct());
    }

    private static string BuildDeleteBlockedMessage(ApiException exception) =>
        exception.DetailCode switch
        {
            "PLACE_HAS_LINKED_PHOTOS" or "PLACE_IN_USE" =>
                "이 장소는 사진 또는 다른 데이터에서 사용 중이므로 삭제할 수 없습니다. 연결 사진을 확인하고 필요하면 다른 장소와 병합하세요.",
            "REVISION_CONFLICT" =>
                "다른 기기에서 장소 정보가 변경되었습니다. 목록을 새로 고친 뒤 다시 시도하세요.",
            _ => "이 장소는 다른 데이터에서 참조 중이어서 삭제할 수 없습니다. 연결 항목을 확인한 뒤 다시 시도하세요.",
        };

    private async Task SuggestPlacesAsync(string input)
    {
        var version = Interlocked.Increment(ref _suggestVersion);
        _suggestCts?.Cancel();
        _suggestCts?.Dispose();
        _suggestCts = new CancellationTokenSource();
        var token = _suggestCts.Token;

        try
        {
            await Task.Delay(350, token);
            if (version != _suggestVersion)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(input) || input.Trim().Length < 2)
            {
                PlaceSuggestions.Clear();
                HasPlaceSuggestions = false;
                return;
            }

            var suggestions = await _locationResolver.SuggestPlacesAsync(input.Trim(), token);
            if (version != _suggestVersion)
            {
                return;
            }

            PlaceSuggestions = new ObservableCollection<PlaceSuggestionDto>(suggestions);
            HasPlaceSuggestions = PlaceSuggestions.Count > 0;
        }
        catch (OperationCanceledException)
        {
            // debounce cancel
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Place suggestion failed.");
            PlaceSuggestions.Clear();
            HasPlaceSuggestions = false;
            ShowError(ex.Message);
        }
    }

    private async Task ApplyCoordinatesAsync(
        double lat,
        double lng,
        bool reverseGeocode,
        bool updateMap,
        CancellationToken cancellationToken = default)
    {
        _suppressFormHandlers = true;
        LatitudeText = lat.ToString("F6", CultureInfo.InvariantCulture);
        LongitudeText = lng.ToString("F6", CultureInfo.InvariantCulture);
        _suppressFormHandlers = false;

        if (reverseGeocode)
        {
            try
            {
                _applyingLocation = true;
                var location = await _locationResolver.ResolveAsync(lat, lng, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (location is not null)
                {
                    ApplyLocationResult(location);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reverse geocode failed.");
            }
            finally
            {
                _applyingLocation = false;
            }
        }

        if (updateMap)
        {
            await SyncEditablePinAsync();
        }

        await RefreshIncludedPhotoCountAsync();
    }

    private async Task SyncEditablePinAsync()
    {
        if (_mapController is null || !IsMapReady)
        {
            return;
        }

        if (!TryParseDouble(LatitudeText, out var lat) || !TryParseDouble(LongitudeText, out var lng))
        {
            return;
        }

        if (lat is 0 && lng is 0 && string.IsNullOrWhiteSpace(DisplayName))
        {
            return;
        }

        try
        {
            var radius = TryParseDouble(RadiusText, out var r) ? ClampRadius(r) : 100;
            await _mapController.SetEditablePinAsync(lat, lng, radius, zoom: 17);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to sync editable pin.");
        }
    }

    private async Task UpdateMapRadiusAsync(double radius)
    {
        if (_mapController is null || !IsMapReady)
        {
            return;
        }

        try
        {
            await _mapController.UpdateEditableRadiusAsync(ClampRadius(radius));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update editable radius.");
        }
    }

    private async Task ClearMapPinAsync()
    {
        if (_mapController is null || !IsMapReady)
        {
            return;
        }

        try
        {
            await _mapController.ClearEditablePinAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clear editable pin.");
        }
    }

    private async Task SetMapPickModeAsync(bool enabled)
    {
        if (_mapController is null || !IsMapReady)
        {
            return;
        }

        try
        {
            await _mapController.EnableMapClickAsync(enabled);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to toggle map click mode.");
        }
    }

    private async Task RefreshIncludedPhotoCountAsync()
    {
        var version = Interlocked.Increment(ref _photoCountVersion);
        _photoCountCts?.Cancel();
        _photoCountCts?.Dispose();
        _photoCountCts = new CancellationTokenSource();
        var token = _photoCountCts.Token;

        try
        {
            await Task.Delay(200, token);
            if (version != _photoCountVersion)
            {
                return;
            }

            if (!TryParseDouble(LatitudeText, out var lat)
                || !TryParseDouble(LongitudeText, out var lng)
                || !TryParseDouble(RadiusText, out var radius)
                || radius <= 0)
            {
                IncludedPhotoCount = 0;
                IncludedPhotoCountText = "반경 내 사진 0장";
                return;
            }

            var impact = await _placeService.GetRadiusImpactAsync(
                lat,
                lng,
                radius,
                SelectedPlace?.Id,
                token);
            if (version != _photoCountVersion)
            {
                return;
            }

            IncludedPhotoCount = impact.MatchedFileCount;
            IncludedPhotoCountText = $"반경 내 사진 {impact.MatchedFileCount:N0}장";
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to count media in radius.");
        }
    }

    private void OnMapReady(object? sender, EventArgs e)
    {
        IsMapReady = true;
        _ = SyncEditablePinAsync();
    }

    private void OnMapClicked(object? sender, (double Lat, double Lng) point)
    {
        if (!IsMapPickMode || _applyingLocation)
        {
            return;
        }

        _ = HandleMapPointAsync(point.Lat, point.Lng);
    }

    private void OnEditableMarkerDragEnded(object? sender, (double Lat, double Lng) point)
    {
        if (_applyingLocation)
        {
            return;
        }

        _ = HandleMapPointAsync(point.Lat, point.Lng);
    }

    private async Task HandleMapPointAsync(double lat, double lng)
    {
        _mapPointCts?.Cancel();
        _mapPointCts?.Dispose();
        _mapPointCts = new CancellationTokenSource();
        var token = _mapPointCts.Token;

        try
        {
            // Marker already moved on the map; only refresh form + radius circle.
            await ApplyCoordinatesAsync(lat, lng, reverseGeocode: true, updateMap: false, token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            await UpdateMapRadiusAsync(ParseRadiusSafe());
            StatusMessage = "지도 위치가 적용되었습니다. 저장하면 반영됩니다.";
        }
        catch (OperationCanceledException)
        {
            // superseded by another drag/save
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Map point apply failed.");
            ShowError(ex.Message);
        }
    }

    private void CancelMapPointApply()
    {
        _mapPointCts?.Cancel();
    }

    private double ParseRadiusSafe()
    {
        return TryParseDouble(RadiusText, out var radius) && radius > 0
            ? ClampRadius(radius)
            : 100;
    }

    private void SetRadiusInternal(double radius, bool updateMap, bool refreshCount)
    {
        var clamped = ClampRadius(radius);
        _suppressFormHandlers = true;
        RadiusMeters = clamped;
        RadiusText = FormatRadius(clamped);
        _suppressFormHandlers = false;

        if (updateMap)
        {
            _ = UpdateMapRadiusAsync(clamped);
        }

        if (refreshCount)
        {
            _ = RefreshIncludedPhotoCountAsync();
        }
    }

    private void ShowSuccess(string message)
    {
        StatusMessage = message;
        InfoBarMessage = message;
        InfoBarSeverity = InfoBarSeverity.Success;
        IsInfoBarOpen = true;
    }

    private void ShowError(string message)
    {
        StatusMessage = message;
        InfoBarMessage = message;
        InfoBarSeverity = InfoBarSeverity.Error;
        IsInfoBarOpen = true;
    }

    private static double ClampRadius(double radius) =>
        Math.Clamp(radius, 10, 50000);

    private static string FormatRadius(double radius) =>
        radius.ToString("0.##", CultureInfo.InvariantCulture);

    private static bool TryParseDouble(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    private static string FirstNotBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static double ParseCoordinate(string text, string fieldName)
    {
        if (!TryParseDouble(text, out var value))
        {
            throw new ArgumentException($"{fieldName} 값이 올바르지 않습니다.");
        }

        return value;
    }

    private static double ParseRadius(string text)
    {
        if (!TryParseDouble(text, out var value) || value <= 0)
        {
            throw new ArgumentException("반경 값이 올바르지 않습니다.");
        }

        return value;
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
            _logger.LogError(ex, "Place management operation failed.");
            ShowError(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}

public enum PlaceManagementFilterKind
{
    All,
    Duplicates,
    Incomplete,
    ZeroPhotos,
    Recent,
    Favorites,
}

public sealed record PlaceManagementFilterOption(PlaceManagementFilterKind Kind, string Label);

public partial class PlaceLinkedPhotoItem : ObservableObject
{
    public Guid MediaId { get; init; }

    public string FileId { get; init; } = string.Empty;

    public string Filename { get; init; } = string.Empty;

    public string? ThumbnailUrl { get; init; }

    public string CaptureDateText { get; init; } = "-";

    [ObservableProperty]
    private BitmapImage? thumbnailImage;
}
