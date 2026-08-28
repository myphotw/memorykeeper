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
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MemoryKeeper.App.ViewModels;

public partial class PlaceManagementViewModel : ObservableObject
{
    private readonly MemoryKeeperPlaceService _placeService;
    private readonly IGalleryApiRepository _galleryApiRepository;
    private readonly ILocationResolver _locationResolver;
    private readonly ISettingRepository _settingRepository;
    private readonly IPlaceEditorSeedState _seedState;
    private readonly ILogger<PlaceManagementViewModel> _logger;

    private IMapController? _mapController;
    private CancellationTokenSource? _suggestCts;
    private CancellationTokenSource? _photoCountCts;
    private CancellationTokenSource? _mapPointCts;
    private int _suggestVersion;
    private int _photoCountVersion;
    private bool _suppressFormHandlers;
    private bool _applyingLocation;
    private List<Guid> _seedMediaIds = [];

    [ObservableProperty]
    private ObservableCollection<PlaceDto> places = [];

    [ObservableProperty]
    private ObservableCollection<PlaceDto> filteredPlaces = [];

    [ObservableProperty]
    private ObservableCollection<PlaceDto> favoritePlaces = [];

    [ObservableProperty]
    private ObservableCollection<PlaceDto> recentPlaces = [];

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
    private string includedPhotoCountText = "포함 사진 0장";

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

    public XamlRoot? HostXamlRoot { get; set; }

    public event EventHandler? BackRequested;

    public PlaceManagementViewModel(
        MemoryKeeperPlaceService placeService,
        IGalleryApiRepository galleryApiRepository,
        ILocationResolver locationResolver,
        ISettingRepository settingRepository,
        IPlaceEditorSeedState seedState,
        ILogger<PlaceManagementViewModel> logger)
    {
        _placeService = placeService;
        _galleryApiRepository = galleryApiRepository;
        _locationResolver = locationResolver;
        _settingRepository = settingRepository;
        _seedState = seedState;
        _logger = logger;
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
        if (_suppressFormHandlers || value is null)
        {
            return;
        }

        ApplyPlaceToForm(value);
        _ = SyncEditablePinAsync();
        _ = RefreshIncludedPhotoCountAsync();
    }

    partial void OnListSearchTextChanged(string value) => ApplyListFilter();

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
            await ReloadListsAsync();

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

    [RelayCommand]
    private void ClearForm()
    {
        ClearFormInternal();
        _ = ClearMapPinAsync();
        IncludedPhotoCount = 0;
        IncludedPhotoCountText = "포함 사진 0장";
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
        var confirmed = await UserFeedback.ConfirmAsync(
            HostXamlRoot,
            "장소 삭제",
            $"이 장소는 사진 {place.MediaCount}장에서 사용 중입니다.\n삭제하시겠습니까?",
            primaryText: "삭제");

        if (!confirmed)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _placeService.DeletePlaceAsync(place.Id);
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
            District = string.Empty,
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
            District = SelectedPlace?.District ?? string.Empty,
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
        _suppressFormHandlers = false;
    }

    private async Task ReloadListsAsync()
    {
        var items = await _placeService.GetPlaceListAsync();
        var favorites = await _placeService.GetFavoritePlacesAsync();
        var recent = await _placeService.GetRecentPlacesAsync(10);

        Places = new ObservableCollection<PlaceDto>(items);
        FavoritePlaces = new ObservableCollection<PlaceDto>(favorites);
        RecentPlaces = new ObservableCollection<PlaceDto>(recent);
        ApplyListFilter();
    }

    private async Task ReloadAndSelectAsync(Guid placeId)
    {
        await ReloadListsAsync();
        SelectedPlace = Places.FirstOrDefault(place => place.Id == placeId);
        if (SelectedPlace is not null)
        {
            await SyncEditablePinAsync();
            await RefreshIncludedPhotoCountAsync();
        }
    }

    private void ApplyListFilter()
    {
        IEnumerable<PlaceDto> query = Places;
        if (!string.IsNullOrWhiteSpace(ListSearchText))
        {
            var term = ListSearchText.Trim();
            query = query.Where(place =>
                place.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || place.Address.Contains(term, StringComparison.OrdinalIgnoreCase)
                || place.City.Contains(term, StringComparison.OrdinalIgnoreCase)
                || place.Province.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        FilteredPlaces = new ObservableCollection<PlaceDto>(query);
    }

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
                IncludedPhotoCountText = "포함 사진 0장";
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
            IncludedPhotoCountText = $"포함 사진 {impact.MatchedFileCount}장";
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
