using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemoryKeeper.App.Models;
using MemoryKeeper.App.Services;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;

namespace MemoryKeeper.App.ViewModels;

public partial class TravelRecordsViewModel : ObservableObject
{
    private readonly TravelRecordsService _travelRecordsService;
    private readonly HomeLocationService _homeLocationService;
    private readonly IThumbnailService _thumbnailService;
    private readonly IPlaceFocusState _placeFocusState;
    private readonly ITravelRecordsNavigationState _navigationState;
    private readonly ILogger<TravelRecordsViewModel> _logger;
    private readonly DispatcherQueue _dispatcherQueue;
    private CancellationTokenSource? _thumbnailCts;

    [ObservableProperty]
    private TravelPlaceCardItem? mostVisitedPlace;

    [ObservableProperty]
    private TravelPlaceCardItem? longUnvisitedPlace;

    [ObservableProperty]
    private ObservableCollection<TravelSeasonCardItem> seasonHighlights = [];

    [ObservableProperty]
    private ObservableCollection<TravelPlaceCardItem> recentPlaces = [];

    [ObservableProperty]
    private TravelCountryCardItem? topCountry;

    [ObservableProperty]
    private TravelFarthestCardItem? farthestPlace;

    [ObservableProperty]
    private ObservableCollection<TravelYearChapterItem> yearChapters = [];

    [ObservableProperty]
    private bool hasMostVisited;

    [ObservableProperty]
    private bool hasLongUnvisited;

    [ObservableProperty]
    private bool hasSeasons;

    [ObservableProperty]
    private bool hasRecent;

    [ObservableProperty]
    private bool hasTopCountry;

    [ObservableProperty]
    private bool hasFarthest;

    [ObservableProperty]
    private bool hasYearChapters;

    [ObservableProperty]
    private bool hasHighlights;

    [ObservableProperty]
    private TravelTripCardItem? featuredMemory;

    [ObservableProperty]
    private bool hasFeaturedMemory;

    [ObservableProperty]
    private string featuredMemoryCaption = string.Empty;

    [ObservableProperty]
    private bool needsHomeLocation;

    [ObservableProperty]
    private string homeAddressInput = string.Empty;

    [ObservableProperty]
    private string homeLatitudeInput = string.Empty;

    [ObservableProperty]
    private string homeLongitudeInput = string.Empty;

    [ObservableProperty]
    private string homeLocationSummary = string.Empty;

    [ObservableProperty]
    private string statusMessage = "여행기록을 불러오는 중…";

    [ObservableProperty]
    private bool isBusy;

    public event EventHandler? OpenVisitRecordRequested;
    public event EventHandler? OpenDetailRequested;
    public event EventHandler? BackRequested;

    public TravelRecordsViewModel(
        TravelRecordsService travelRecordsService,
        HomeLocationService homeLocationService,
        IThumbnailService thumbnailService,
        IPlaceFocusState placeFocusState,
        ITravelRecordsNavigationState navigationState,
        ILogger<TravelRecordsViewModel> logger)
    {
        _travelRecordsService = travelRecordsService;
        _homeLocationService = homeLocationService;
        _thumbnailService = thumbnailService;
        _placeFocusState = placeFocusState;
        _navigationState = navigationState;
        _logger = logger;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    [RelayCommand]
    private void GoBack() => BackRequested?.Invoke(this, EventArgs.Empty);

    public void ClearFeaturedMemory()
    {
        if (FeaturedMemory is not null)
        {
            FeaturedMemory.IsHighlighted = false;
        }

        FeaturedMemory = null;
        HasFeaturedMemory = false;
        FeaturedMemoryCaption = string.Empty;
        if (!NeedsHomeLocation && string.IsNullOrWhiteSpace(StatusMessage))
        {
            StatusMessage = "스크롤하며 여행을 다시 떠올려 보세요.";
        }
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            CancelThumbnails();
            _thumbnailCts = new CancellationTokenSource();
            var token = _thumbnailCts.Token;

            await RefreshHomeLocationAsync(token);
            var dashboard = await _travelRecordsService.GetDashboardAsync(token);
            ApplyDashboard(dashboard);
            ApplyPendingMemoryFocus();
            if (!HasFeaturedMemory)
            {
                StatusMessage = NeedsHomeLocation
                    ? "Home Location을 설정하면 '가장 멀리 여행한 장소'를 계산합니다."
                    : "스크롤하며 여행을 다시 떠올려 보세요.";
            }

            _ = LoadThumbnailsAsync(token);
        }
        catch (OperationCanceledException)
        {
            // reload
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Travel records dashboard load failed.");
            StatusMessage = "여행기록을 불러오지 못했습니다.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveHomeByAddressAsync()
    {
        if (string.IsNullOrWhiteSpace(HomeAddressInput))
        {
            StatusMessage = "Home 주소를 입력하세요.";
            return;
        }

        try
        {
            IsBusy = true;
            await _homeLocationService.SaveAddressAsync(HomeAddressInput.Trim());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save home address.");
            StatusMessage = ex.Message;
            return;
        }
        finally
        {
            IsBusy = false;
        }

        await LoadAsync();
    }

    [RelayCommand]
    private async Task SaveHomeByCoordinatesAsync()
    {
        if (!double.TryParse(HomeLatitudeInput, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var lat)
            || !double.TryParse(HomeLongitudeInput, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var lon))
        {
            StatusMessage = "위도/경도를 숫자로 입력하세요.";
            return;
        }

        try
        {
            IsBusy = true;
            await _homeLocationService.SaveCoordinatesAsync(lat, lon, HomeAddressInput);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save home coordinates.");
            StatusMessage = ex.Message;
            return;
        }
        finally
        {
            IsBusy = false;
        }

        await LoadAsync();
    }

    private async Task RefreshHomeLocationAsync(CancellationToken token)
    {
        var home = await _homeLocationService.GetAsync(token);
        NeedsHomeLocation = !home.IsConfigured;
        HomeAddressInput = home.Address;
        HomeLatitudeInput = home.Latitude?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        HomeLongitudeInput = home.Longitude?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        HomeLocationSummary = home.IsConfigured
            ? $"Home: {home.Latitude:F4}, {home.Longitude:F4}" +
              (string.IsNullOrWhiteSpace(home.Address) ? string.Empty : $" · {home.Address}")
            : "Home Location 미설정";
    }

    [RelayCommand]
    private void OpenMostVisitedDetail() => OpenDetail(TravelRecordsDetailKind.MostVisited);

    [RelayCommand]
    private void OpenLongUnvisitedDetail() => OpenDetail(TravelRecordsDetailKind.LongUnvisited);

    [RelayCommand]
    private void OpenRecentDetail() => OpenDetail(TravelRecordsDetailKind.Recent);

    [RelayCommand]
    private void OpenCountriesDetail() => OpenDetail(TravelRecordsDetailKind.Countries);

    [RelayCommand]
    private void OpenFarthestDetail() => OpenDetail(TravelRecordsDetailKind.Farthest);

    [RelayCommand]
    private void OpenSeasonDetail(TravelSeasonCardItem? item)
    {
        if (item is null)
        {
            return;
        }

        OpenDetail(TravelRecordsDetailKind.Season, item.Season);
    }

    [RelayCommand]
    private void OpenPlace(TravelPlaceCardItem? item)
    {
        if (item is null)
        {
            return;
        }

        ClearPendingFilters();
        _placeFocusState.FocusPlaceId = item.PlaceId;
        OpenVisitRecordRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenFarthestPlace(TravelFarthestCardItem? item)
    {
        if (item is null)
        {
            return;
        }

        ClearPendingFilters();
        _placeFocusState.FocusPlaceId = item.PlaceId;
        OpenVisitRecordRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenTrip(TravelTripCardItem? item)
    {
        if (item is null)
        {
            return;
        }

        ClearPendingFilters();
        if (!string.IsNullOrWhiteSpace(item.Country) && item.PlaceCount > 1)
        {
            _placeFocusState.PendingCountry = item.Country;
        }
        else
        {
            _placeFocusState.FocusPlaceId = item.FocusPlaceId;
        }

        OpenVisitRecordRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OpenDetail(TravelRecordsDetailKind kind, TravelSeason? season = null)
    {
        _navigationState.RequestDetail(kind, season);
        OpenDetailRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ClearPendingFilters()
    {
        _placeFocusState.PendingSearchText = null;
        _placeFocusState.PendingSeason = null;
        _placeFocusState.PendingCountry = null;
    }

    private void ApplyDashboard(TravelRecordsDashboardDto dashboard)
    {
        YearChapters = new ObservableCollection<TravelYearChapterItem>(
            dashboard.YearChapters.Select(chapter => new TravelYearChapterItem(chapter)));
        MostVisitedPlace = dashboard.MostVisitedPlace is null
            ? null
            : new TravelPlaceCardItem(dashboard.MostVisitedPlace);
        LongUnvisitedPlace = dashboard.LongUnvisitedPlace is null
            ? null
            : new TravelPlaceCardItem(dashboard.LongUnvisitedPlace);
        SeasonHighlights = new ObservableCollection<TravelSeasonCardItem>(
            dashboard.SeasonHighlights.Select(item => new TravelSeasonCardItem(item)));
        RecentPlaces = new ObservableCollection<TravelPlaceCardItem>(
            dashboard.RecentPlaces.Select(item => new TravelPlaceCardItem(item)));
        TopCountry = dashboard.TopCountry is null
            ? null
            : new TravelCountryCardItem(dashboard.TopCountry);
        FarthestPlace = dashboard.FarthestPlace is null
            ? null
            : new TravelFarthestCardItem(dashboard.FarthestPlace);

        HasYearChapters = YearChapters.Count > 0;
        HasMostVisited = MostVisitedPlace is not null;
        HasLongUnvisited = LongUnvisitedPlace is not null;
        HasSeasons = SeasonHighlights.Any(item => item.HasPlace);
        HasRecent = RecentPlaces.Count > 0;
        HasTopCountry = TopCountry is not null;
        HasFarthest = FarthestPlace is not null;
        HasHighlights = HasMostVisited || HasLongUnvisited || HasSeasons || HasRecent
            || HasTopCountry || HasFarthest;
        FeaturedMemory = null;
        HasFeaturedMemory = false;
        FeaturedMemoryCaption = string.Empty;
    }

    private void ApplyPendingMemoryFocus()
    {
        var placeId = _navigationState.PendingFocusPlaceId;
        var year = _navigationState.PendingFocusYear;
        var placeName = _navigationState.PendingFocusPlaceName;
        _navigationState.PendingFocusPlaceId = null;
        _navigationState.PendingFocusYear = null;
        _navigationState.PendingFocusPlaceName = null;
        _navigationState.PendingFocusMediaId = null;

        if (placeId is null && string.IsNullOrWhiteSpace(placeName))
        {
            return;
        }

        bool Matches(TravelTripCardItem trip) =>
            (placeId is Guid id && trip.FocusPlaceId == id)
            || (!string.IsNullOrWhiteSpace(placeName)
                && (string.Equals(trip.TripName, placeName, StringComparison.OrdinalIgnoreCase)
                    || trip.Dto.PlaceNames.Any(name =>
                        string.Equals(name, placeName, StringComparison.OrdinalIgnoreCase))));

        TravelTripCardItem? match = null;
        if (year is int focusYear)
        {
            match = YearChapters
                .Where(chapter => chapter.Year == focusYear)
                .SelectMany(chapter => chapter.Trips)
                .FirstOrDefault(Matches);
        }

        match ??= YearChapters
            .SelectMany(chapter => chapter.Trips)
            .FirstOrDefault(Matches);

        if (match is null)
        {
            if (!string.IsNullOrWhiteSpace(placeName))
            {
                StatusMessage = $"{placeName} 추억을 Timeline에서 찾아보세요.";
            }

            return;
        }

        match.IsHighlighted = true;
        FeaturedMemory = match;
        HasFeaturedMemory = true;
        FeaturedMemoryCaption = string.IsNullOrWhiteSpace(placeName)
            ? "Home에서 발견한 추억"
            : $"{placeName}, 다시 살아보는 중";
        StatusMessage = string.IsNullOrWhiteSpace(placeName)
            ? "발견한 추억을 다시 살아보세요."
            : $"{placeName} 추억을 다시 살아보세요.";
    }

    private async Task LoadThumbnailsAsync(CancellationToken token)
    {
        try
        {
            var targets = new List<(Guid? MediaId, string Path, Action<BitmapImage?> SetImage)>();

            if (FeaturedMemory is not null)
            {
                targets.Add((FeaturedMemory.RepresentativeMediaId, FeaturedMemory.AbsoluteLibraryPath,
                    image => FeaturedMemory.ThumbnailImage = image));
            }

            foreach (var chapter in YearChapters)
            {
                foreach (var trip in chapter.Trips)
                {
                    targets.Add((trip.RepresentativeMediaId, trip.AbsoluteLibraryPath,
                        image => trip.ThumbnailImage = image));
                }
            }

            if (MostVisitedPlace is not null)
            {
                targets.Add((MostVisitedPlace.RepresentativeMediaId, MostVisitedPlace.AbsoluteLibraryPath,
                    image => MostVisitedPlace.ThumbnailImage = image));
            }

            if (LongUnvisitedPlace is not null)
            {
                targets.Add((LongUnvisitedPlace.RepresentativeMediaId, LongUnvisitedPlace.AbsoluteLibraryPath,
                    image => LongUnvisitedPlace.ThumbnailImage = image));
            }

            if (FarthestPlace is not null)
            {
                targets.Add((FarthestPlace.RepresentativeMediaId, FarthestPlace.AbsoluteLibraryPath,
                    image => FarthestPlace.ThumbnailImage = image));
            }

            foreach (var recent in RecentPlaces)
            {
                targets.Add((recent.RepresentativeMediaId, recent.AbsoluteLibraryPath,
                    image => recent.ThumbnailImage = image));
            }

            foreach (var target in targets)
            {
                token.ThrowIfCancellationRequested();
                if (target.MediaId is null || string.IsNullOrWhiteSpace(target.Path))
                {
                    continue;
                }

                var path = await _thumbnailService.GetOrCreateThumbnailAsync(
                    target.MediaId.Value,
                    target.Path,
                    token);
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                await EnqueueAsync(() => target.SetImage(new BitmapImage(new Uri(path))));
            }
        }
        catch (OperationCanceledException)
        {
            // expected
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Travel records thumbnail load failed.");
        }
    }

    private void CancelThumbnails()
    {
        _thumbnailCts?.Cancel();
        _thumbnailCts?.Dispose();
        _thumbnailCts = null;
    }

    private Task EnqueueAsync(Action action)
    {
        var tcs = new TaskCompletionSource();
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
            tcs.SetResult();
        }

        return tcs.Task;
    }
}
