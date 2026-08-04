using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemoryKeeper.App.Maps;
using MemoryKeeper.App.Models;
using MemoryKeeper.App.Services;
using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Infrastructure.Services.Api;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.App.ViewModels;

public partial class PlaceMapViewModel : ObservableObject
{
    private readonly IGalleryApiRepository _galleryApiRepository;
    private readonly BaseApiClient _apiClient;
    private readonly ISettingRepository _settingRepository;
    private readonly IPlaceFocusState _placeFocusState;
    private readonly IPhotoNavigationState _photoNavigationState;
    private readonly ILogger<PlaceMapViewModel> _logger;
    private IMapController? _mapController;
    private bool _suppressCameraUpdate;

    [ObservableProperty]
    private ObservableCollection<int> availableYears = [];

    [ObservableProperty]
    private int? selectedYear;

    [ObservableProperty]
    private ObservableCollection<PlaceMapItem> places = [];

    [ObservableProperty]
    private PlaceMapItem? selectedPlace;

    [ObservableProperty]
    private Guid? selectedPlaceId;

    [ObservableProperty]
    private int zoomLevel = 12;

    [ObservableProperty]
    private string statusMessage = "장소를 불러오는 중...";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool isMapReady;

    public PlaceMapViewModel(
        IGalleryApiRepository galleryApiRepository,
        BaseApiClient apiClient,
        ISettingRepository settingRepository,
        IPlaceFocusState placeFocusState,
        IPhotoNavigationState photoNavigationState,
        ILogger<PlaceMapViewModel> logger)
    {
        _galleryApiRepository = galleryApiRepository;
        _apiClient = apiClient;
        _settingRepository = settingRepository;
        _placeFocusState = placeFocusState;
        _photoNavigationState = photoNavigationState;
        _logger = logger;
        AvailableYears = new ObservableCollection<int>(BuildDefaultYears());
    }

    public void AttachMap(IMapController mapController)
    {
        DetachMap();
        _mapController = mapController;
        _mapController.Ready += OnMapReady;
        IsMapReady = mapController.IsReady;
    }

    public void DetachMap()
    {
        if (_mapController is not null)
        {
            _mapController.Ready -= OnMapReady;
        }

        _mapController = null;
        IsMapReady = false;
    }

    partial void OnSelectedPlaceChanged(PlaceMapItem? value)
    {
        SelectedPlaceId = value?.PlaceId;
        if (value is not null)
        {
            _placeFocusState.FocusPlaceId = value.PlaceId;
        }

        if (!_suppressCameraUpdate && value is not null)
        {
            _ = FocusPlaceOnMapAsync(value);
        }
    }

    partial void OnZoomLevelChanged(int value)
    {
        if (_suppressCameraUpdate || value is < 1 or > 21 || SelectedPlace is null || _mapController is null)
        {
            return;
        }

        _ = SetZoomSafeAsync(value);
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
            IsMapReady = true;
            await SyncMapAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize place map.");
            StatusMessage = $"지도 초기화 실패: {ex.Message}";
            IsMapReady = false;
        }
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            var years = await GalleryBackendBridge.GetTimelineYearsAsync(_galleryApiRepository);
            if (years.Count > 0)
            {
                AvailableYears = new ObservableCollection<int>(years);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Map timeline years failed.");
        }

        await SearchAsync();
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        await RunBusyAsync(async () =>
        {
            List<PlaceMapItem> items;
            try
            {
                var map = await _galleryApiRepository.GetMapAsync(year: SelectedYear);
                items = GalleryBackendBridge.GroupMarkersToVisitPlaces(map.Items, _apiClient.ApiBaseUrl)
                    .Select(place => new PlaceMapItem(
                        new MemorySearchResult
                        {
                            PlaceId = place.PlaceId,
                            PlaceName = place.PlaceName,
                            Country = place.Country,
                            City = place.City,
                            PhotoCount = place.PhotoCount,
                            VisitRecordCount = place.VisitRecordCount,
                            FavoriteCount = place.FavoriteCount,
                            RepresentativeMediaId = place.RepresentativeMediaId,
                            FirstCapturedDate = place.FirstCapturedDate,
                            LastCapturedDate = place.LastCapturedDate,
                        },
                        place.Latitude,
                        place.Longitude))
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Backend map failed.");
                StatusMessage = $"지도를 불러오지 못했습니다. {ex.Message}";
                items = [];
            }

            Places = new ObservableCollection<PlaceMapItem>(items);

            var focusId = _placeFocusState.FocusPlaceId;
            PlaceMapItem? focused = null;
            if (focusId is Guid id)
            {
                focused = items.FirstOrDefault(item => item.PlaceId == id);
            }

            _suppressCameraUpdate = true;
            try
            {
                SelectedPlace = focused ?? items.FirstOrDefault();
            }
            finally
            {
                _suppressCameraUpdate = false;
            }

            StatusMessage = items.Count == 0
                ? SelectedYear is null
                    ? "지도에 표시할 장소가 없습니다."
                    : $"{SelectedYear}년 장소가 없습니다."
                : SelectedYear is null
                    ? $"전체 {items.Count}개 장소를 지도에 표시합니다."
                    : $"{SelectedYear}년 장소 {items.Count}개를 지도에 표시합니다.";

            await SyncMapAsync();
        });
    }

    [RelayCommand]
    private void ClearYearFilter()
    {
        SelectedYear = null;
    }

    [RelayCommand]
    private void OpenPhotoDetail()
    {
        if (SelectedPlace?.RepresentativeMediaId is not Guid mediaId)
        {
            StatusMessage = "이 장소에서 열 수 있는 사진이 없습니다.";
            return;
        }

        _photoNavigationState.RequestOpen(mediaId);
    }

    [RelayCommand]
    private async Task ZoomInAsync()
    {
        ZoomLevel = Math.Min(21, ZoomLevel + 1);
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ZoomOutAsync()
    {
        ZoomLevel = Math.Max(1, ZoomLevel - 1);
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task FitAllAsync()
    {
        if (_mapController is null || !IsMapReady)
        {
            return;
        }

        try
        {
            await _mapController.FitMarkersAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fit map markers.");
        }
    }

    private async Task SyncMapAsync()
    {
        if (_mapController is null || !IsMapReady)
        {
            return;
        }

        try
        {
            var markers = Places
                .Select(place => new MapMarker(
                    place.PlaceId,
                    place.PlaceName,
                    place.Latitude,
                    place.Longitude,
                    place.MarkerInfo))
                .ToList();

            await _mapController.SetMarkersAsync(markers);

            if (SelectedPlace is not null)
            {
                await FocusPlaceOnMapAsync(SelectedPlace);
            }
            else if (markers.Count > 0)
            {
                await _mapController.FitMarkersAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to sync map markers.");
            StatusMessage = $"지도 동기화 실패: {ex.Message}";
        }
    }

    private async Task FocusPlaceOnMapAsync(PlaceMapItem place)
    {
        if (_mapController is null || !IsMapReady)
        {
            return;
        }

        try
        {
            await _mapController.CenterOnAsync(place.Latitude, place.Longitude, ZoomLevel);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to center map on place. PlaceId={PlaceId}", place.PlaceId);
        }
    }

    private async Task SetZoomSafeAsync(int zoom)
    {
        if (_mapController is null || !IsMapReady)
        {
            return;
        }

        try
        {
            await _mapController.SetZoomAsync(zoom);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to change map zoom. Zoom={Zoom}", zoom);
        }
    }

    private void OnMapReady(object? sender, EventArgs e)
    {
        IsMapReady = true;
        _ = SyncMapAsync();
    }

    private static IEnumerable<int> BuildDefaultYears()
    {
        var currentYear = DateTime.Now.Year;
        for (var year = currentYear; year >= currentYear - 30; year--)
        {
            yield return year;
        }
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
            _logger.LogError(ex, "Place map search failed.");
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
