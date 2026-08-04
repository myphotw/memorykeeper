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

namespace MemoryKeeper.App.ViewModels;

public partial class PlaceMapViewModel : ObservableObject
{
    private readonly MemorySearchService _memorySearchService;
    private readonly PlaceService _placeService;
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
        MemorySearchService memorySearchService,
        PlaceService placeService,
        ISettingRepository settingRepository,
        IPlaceFocusState placeFocusState,
        IPhotoNavigationState photoNavigationState,
        ILogger<PlaceMapViewModel> logger)
    {
        _memorySearchService = memorySearchService;
        _placeService = placeService;
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
        await SearchAsync();
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        await RunBusyAsync(async () =>
        {
            var searchResults = await _memorySearchService.SearchAsync(new MemorySearchRequest
            {
                Year = SelectedYear
            });

            var placeCoordinates = (await _placeService.GetPlaceListAsync())
                .ToDictionary(place => place.Id);

            var items = searchResults.Items
                .Where(result => placeCoordinates.ContainsKey(result.PlaceId))
                .Select(result =>
                {
                    var place = placeCoordinates[result.PlaceId];
                    return new PlaceMapItem(result, place.Latitude, place.Longitude);
                })
                .ToList();

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
