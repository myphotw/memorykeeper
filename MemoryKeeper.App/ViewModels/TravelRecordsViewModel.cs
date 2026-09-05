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
    private ObservableCollection<TravelCountryVisitItem> countryVisitStatistics = [];

    [ObservableProperty]
    private ObservableCollection<TravelMemoryCardItem> memoryCards = [];

    [ObservableProperty]
    private ObservableCollection<TravelYearChapterItem> yearChapters = [];

    [ObservableProperty]
    private int domesticTripCount;

    [ObservableProperty]
    private int foreignTripCount;

    [ObservableProperty]
    private int foreignPlaceCount;

    [ObservableProperty]
    private int foreignPhotoCount;

    [ObservableProperty]
    private int domesticPlaceCount;

    [ObservableProperty]
    private int domesticPhotoCount;

    [ObservableProperty]
    private int uniquePhotoCount;

    [ObservableProperty]
    private int distinctPlaceCount;

    [ObservableProperty]
    private int visitedForeignCountryCount;

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
    private bool hasCountryVisitStatistics;

    [ObservableProperty]
    private bool hasMemoryCards;

    [ObservableProperty]
    private string farthestEmptyMessage = "위치가 있는 여행 기록이 필요해요";

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
    private string homeLocationSummary = string.Empty;

    [ObservableProperty]
    private string statusMessage = "여행기록을 불러오는 중…";

    [ObservableProperty]
    private bool isBusy;

    public event EventHandler? OpenVisitRecordRequested;
    public event EventHandler? OpenDetailRequested;
    public event EventHandler<GalleryPlaceNavigationRequestedEventArgs>? OpenGalleryRequested;
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
            _logger.LogInformation(
                "TravelRecords UI applied. YearChapters={Years}, Trips={Trips}, MemoryCards={MemoryCards}, HasMostVisited={Most}, HasRecent={Recent}, HasFarthest={Farthest}",
                YearChapters.Count,
                YearChapters.Sum(c => c.Trips.Count),
                MemoryCards.Count,
                HasMostVisited,
                HasRecent,
                HasFarthest);
            ApplyPendingMemoryFocus();
            if (!HasFeaturedMemory)
            {
                StatusMessage = NeedsHomeLocation
                    ? "Home Location을 설정하면 '가장 멀리 여행한 장소'를 계산합니다."
                    : HasYearChapters
                        ? "스크롤하며 여행을 다시 떠올려 보세요."
                        : "표시할 여행기록이 없습니다. Backend Gallery에 장소/촬영일 메타데이터가 필요합니다.";
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
            StatusMessage = $"여행기록을 불러오지 못했습니다. {ex.Message}";
            HasYearChapters = false;
            HasHighlights = false;
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
            StatusMessage = "집 위치를 저장하지 못했습니다. 잠시 후 다시 시도해 주세요.";
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
        HomeLocationSummary = home.IsConfigured
            ? (string.IsNullOrWhiteSpace(home.Address) ? "집 위치가 설정되었습니다." : $"집 위치: {home.Address}")
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
    private void OpenForeignCountries() => RequestGallery(
        GalleryPlaceScope.International,
        GalleryPlaceNavigationLevel.Countries);

    [RelayCommand]
    private void OpenForeignPlaces() => RequestGallery(
        GalleryPlaceScope.International,
        GalleryPlaceNavigationLevel.Places);

    [RelayCommand]
    private void OpenDomesticPlaces() => RequestGallery(
        GalleryPlaceScope.Domestic,
        GalleryPlaceNavigationLevel.Places);

    [RelayCommand]
    private void OpenForeignPhotos() => RequestGallery(
        GalleryPlaceScope.International,
        GalleryPlaceNavigationLevel.Photos);

    [RelayCommand]
    private void OpenDomesticPhotos() => RequestGallery(
        GalleryPlaceScope.Domestic,
        GalleryPlaceNavigationLevel.Photos);

    private void RequestGallery(GalleryPlaceScope scope, GalleryPlaceNavigationLevel level) =>
        OpenGalleryRequested?.Invoke(this, new GalleryPlaceNavigationRequestedEventArgs(scope, level));

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
        _placeFocusState.FocusPlaceName = item.PlaceName;
        _placeFocusState.FocusMediaId = item.RepresentativeMediaId;
        _logger.LogInformation(
            "TravelRecords open place → VisitMap. PlaceId={PlaceId} PlaceName={PlaceName} RepMediaId={MediaId}",
            item.PlaceId,
            item.PlaceName,
            item.RepresentativeMediaId);
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
        _placeFocusState.FocusPlaceName = item.PlaceName;
        _placeFocusState.FocusMediaId = item.RepresentativeMediaId;
        _logger.LogInformation(
            "TravelRecords open place → VisitMap. PlaceId={PlaceId} PlaceName={PlaceName} RepMediaId={MediaId}",
            item.PlaceId,
            item.PlaceName,
            item.RepresentativeMediaId);
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
            _placeFocusState.FocusPlaceId = null;
            _placeFocusState.FocusPlaceName = null;
        }
        else
        {
            _placeFocusState.FocusPlaceId = item.FocusPlaceId;
            _placeFocusState.FocusPlaceName = item.TripName;
        }

        OpenVisitRecordRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenMemoryCard(TravelMemoryCardItem? item)
    {
        if (item is null || item.FocusPlaceId == Guid.Empty)
        {
            return;
        }

        ClearPendingFilters();
        _placeFocusState.FocusPlaceId = item.FocusPlaceId;
        _placeFocusState.FocusPlaceName = item.FocusPlaceName;
        _placeFocusState.FocusMediaId = item.RepresentativeMediaId;
        _logger.LogInformation(
            "TravelRecords open memory card → VisitMap. Kind={Kind} PlaceId={PlaceId} MediaId={MediaId}",
            item.Dto.Kind,
            item.FocusPlaceId,
            item.RepresentativeMediaId);
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
        DomesticTripCount = dashboard.DomesticTripCount;
        ForeignTripCount = dashboard.ForeignTripCount;
        ForeignPlaceCount = dashboard.ForeignPlaceCount;
        ForeignPhotoCount = dashboard.ForeignPhotoCount;
        DomesticPlaceCount = dashboard.DomesticPlaceCount;
        DomesticPhotoCount = dashboard.DomesticPhotoCount;
        UniquePhotoCount = dashboard.UniquePhotoCount;
        DistinctPlaceCount = dashboard.DistinctPlaceCount;
        VisitedForeignCountryCount = dashboard.VisitedForeignCountryCount;
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
        var countryMaximum = dashboard.CountryVisitStatistics
            .Select(item => item.VisitCount)
            .DefaultIfEmpty(1)
            .Max();
        CountryVisitStatistics = new ObservableCollection<TravelCountryVisitItem>(
            dashboard.CountryVisitStatistics.Select(item => new TravelCountryVisitItem(item, countryMaximum)));
        _navigationState.SetForeignCountries(dashboard.ForeignCountries);
        MemoryCards = new ObservableCollection<TravelMemoryCardItem>(
            dashboard.MemoryCards.Select(item => new TravelMemoryCardItem(item)));

        HasYearChapters = YearChapters.Count > 0;
        HasMostVisited = MostVisitedPlace is not null;
        HasLongUnvisited = LongUnvisitedPlace is not null;
        HasSeasons = SeasonHighlights.Any(item => item.HasPlace);
        HasRecent = RecentPlaces.Count > 0;
        HasTopCountry = TopCountry is not null;
        HasFarthest = FarthestPlace is not null;
        HasCountryVisitStatistics = CountryVisitStatistics.Count > 0;
        HasMemoryCards = MemoryCards.Count > 0;
        FarthestEmptyMessage = NeedsHomeLocation
            ? "Home을 설정하면 보여요"
            : "위치가 있는 여행 기록이 필요해요";
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
                StatusMessage = $"{placeName} 추억을 찾지 못했습니다.";
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

            foreach (var card in MemoryCards)
            {
                foreach (var photo in card.Photos)
                {
                    targets.Add((photo.MediaId, photo.ThumbnailPath,
                        image => photo.ThumbnailImage = image));
                }
            }

            foreach (var target in targets)
            {
                token.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(target.Path) || !HttpImageLoader.IsHttpUrl(target.Path))
                {
                    continue;
                }

                var image = await HttpImageLoader.LoadAsync(
                    target.Path,
                    _logger,
                    context: $"TravelThumb:{target.MediaId:N}",
                    cancellationToken: token);
                token.ThrowIfCancellationRequested();
                await EnqueueAsync(() => target.SetImage(image));
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
