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

public partial class TravelRecordsDetailViewModel : ObservableObject
{
    private readonly TravelRecordsService _travelRecordsService;
    private readonly ITravelRecordsNavigationState _navigationState;
    private readonly IPlaceFocusState _placeFocusState;
    private readonly IThumbnailService _thumbnailService;
    private readonly ILogger<TravelRecordsDetailViewModel> _logger;
    private readonly DispatcherQueue _dispatcherQueue;
    private CancellationTokenSource? _thumbnailCts;
    private TravelRecordsDetailKind? _activeDetailKind;
    private TravelSeason? _activeSeason;

    [ObservableProperty]
    private string title = "상세";

    [ObservableProperty]
    private ObservableCollection<TravelPlaceCardItem> places = [];

    [ObservableProperty]
    private ObservableCollection<TravelCountryCardItem> countries = [];

    [ObservableProperty]
    private ObservableCollection<TravelFarthestCardItem> farthestPlaces = [];

    [ObservableProperty]
    private bool showPlaces;

    [ObservableProperty]
    private bool showCountries;

    [ObservableProperty]
    private bool showFarthest;

    [ObservableProperty]
    private bool showSeasonAction;

    [ObservableProperty]
    private bool showFarthestHomeHint;

    [ObservableProperty]
    private string farthestHomeHint = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    public event EventHandler? OpenVisitRecordRequested;
    public event EventHandler? BackRequested;

    public TravelRecordsDetailViewModel(
        TravelRecordsService travelRecordsService,
        ITravelRecordsNavigationState navigationState,
        IPlaceFocusState placeFocusState,
        IThumbnailService thumbnailService,
        ILogger<TravelRecordsDetailViewModel> logger)
    {
        _travelRecordsService = travelRecordsService;
        _navigationState = navigationState;
        _placeFocusState = placeFocusState;
        _thumbnailService = thumbnailService;
        _logger = logger;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        var requestedKind = _navigationState.PendingDetailKind;
        var kind = requestedKind ?? _activeDetailKind ?? TravelRecordsDetailKind.MostVisited;
        var season = requestedKind.HasValue
            ? _navigationState.PendingDetailSeason
            : _activeSeason;
        _navigationState.PendingDetailKind = null;
        _navigationState.PendingDetailSeason = null;
        _activeDetailKind = kind;
        _activeSeason = kind == TravelRecordsDetailKind.Season ? season : null;
        FarthestHomeHint = string.Empty;
        ShowFarthestHomeHint = false;

        IsBusy = true;
        try
        {
            CancelThumbnails();
            _thumbnailCts = new CancellationTokenSource();
            var token = _thumbnailCts.Token;

            var detail = await _travelRecordsService.GetDetailAsync(kind, season, token);
            Title = detail.Title;
            var isRecentDetail = kind == TravelRecordsDetailKind.Recent;
            var isLongUnvisitedDetail = kind == TravelRecordsDetailKind.LongUnvisited;
            var placeItems = detail.Places
                .Select(item => new TravelPlaceCardItem(item)
                {
                    IsRecentDetail = isRecentDetail,
                    IsLongUnvisitedDetail = isLongUnvisitedDetail,
                    IsStandardPlaceDetail = !isRecentDetail && !isLongUnvisitedDetail,
                })
                .ToList();
            Places = new ObservableCollection<TravelPlaceCardItem>(placeItems);
            Countries = new ObservableCollection<TravelCountryCardItem>(
                detail.Countries.Select(item => new TravelCountryCardItem(item)));
            FarthestPlaces = new ObservableCollection<TravelFarthestCardItem>(
                detail.FarthestPlaces.Select(item => new TravelFarthestCardItem(item)));

            ShowPlaces = Places.Count > 0;
            ShowCountries = Countries.Count > 0;
            ShowFarthest = FarthestPlaces.Count > 0;
            ShowSeasonAction = _activeSeason is not null;
            FarthestHomeHint = kind == TravelRecordsDetailKind.Farthest
                ? FarthestPlaces.FirstOrDefault()?.HomeHint ?? string.Empty
                : string.Empty;
            ShowFarthestHomeHint = !string.IsNullOrWhiteSpace(FarthestHomeHint);
            StatusMessage = ShowPlaces || ShowCountries || ShowFarthest
                ? "항목을 선택하면 방문지도로 이동합니다."
                : "표시할 기록이 없습니다.";

            _ = LoadThumbnailsAsync(token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Travel records detail load failed.");
            StatusMessage = "상세 기록을 불러오지 못했습니다.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenPlace(TravelPlaceCardItem? item)
    {
        if (item is null)
        {
            return;
        }

        ClearPending();
        _placeFocusState.FocusPlaceId = item.PlaceId;
        _placeFocusState.FocusPlaceName = item.PlaceName;
        _placeFocusState.FocusMediaId = item.RepresentativeMediaId;
        _logger.LogInformation(
            "TravelRecords detail open place → VisitMap. PlaceId={PlaceId} PlaceName={PlaceName}",
            item.PlaceId,
            item.PlaceName);
        OpenVisitRecordRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenCountry(TravelCountryCardItem? item)
    {
        if (item is null)
        {
            return;
        }

        ClearPending();
        _placeFocusState.PendingCountry = item.Country;
        _placeFocusState.FocusPlaceId = null;
        _placeFocusState.FocusPlaceName = null;
        OpenVisitRecordRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenFarthest(TravelFarthestCardItem? item)
    {
        if (item is null)
        {
            return;
        }

        ClearPending();
        _placeFocusState.FocusPlaceId = item.PlaceId;
        _placeFocusState.FocusPlaceName = item.PlaceName;
        _placeFocusState.FocusMediaId = item.RepresentativeMediaId;
        _logger.LogInformation(
            "TravelRecords detail open place → VisitMap. PlaceId={PlaceId} PlaceName={PlaceName}",
            item.PlaceId,
            item.PlaceName);
        OpenVisitRecordRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenSeasonVisitRecord()
    {
        ClearPending();
        if (_activeSeason is TravelSeason value)
        {
            _placeFocusState.PendingSeason = value;
        }

        if (Places.FirstOrDefault() is { } first)
        {
            _placeFocusState.FocusPlaceId = first.PlaceId;
            _placeFocusState.FocusPlaceName = first.PlaceName;
        }

        OpenVisitRecordRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Back() => BackRequested?.Invoke(this, EventArgs.Empty);

    private void ClearPending()
    {
        _placeFocusState.PendingSearchText = null;
        _placeFocusState.PendingSeason = null;
        _placeFocusState.PendingCountry = null;
    }

    private async Task LoadThumbnailsAsync(CancellationToken token)
    {
        try
        {
            foreach (var item in Places)
            {
                token.ThrowIfCancellationRequested();
                await LoadOneAsync(item.RepresentativeMediaId, item.AbsoluteLibraryPath,
                    image => item.ThumbnailImage = image, token);
            }

            foreach (var item in FarthestPlaces)
            {
                token.ThrowIfCancellationRequested();
                await LoadOneAsync(item.RepresentativeMediaId, item.AbsoluteLibraryPath,
                    image => item.ThumbnailImage = image, token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Travel detail thumbnail load failed.");
        }
    }

    private Task LoadOneAsync(
        Guid? mediaId,
        string path,
        Action<BitmapImage?> setImage,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(path) || !HttpImageLoader.IsHttpUrl(path))
        {
            return Task.CompletedTask;
        }

        return EnqueueAsync(() =>
            setImage(HttpImageLoader.TryCreate(
                path,
                _logger,
                context: $"TravelDetailThumb:{mediaId:N}")));
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
