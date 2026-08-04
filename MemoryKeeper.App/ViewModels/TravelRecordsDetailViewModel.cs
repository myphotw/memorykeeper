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
        var kind = _navigationState.PendingDetailKind ?? TravelRecordsDetailKind.MostVisited;
        var season = _navigationState.PendingDetailSeason;
        _navigationState.PendingDetailKind = null;
        _navigationState.PendingDetailSeason = null;
        _activeSeason = kind == TravelRecordsDetailKind.Season ? season : null;

        IsBusy = true;
        try
        {
            CancelThumbnails();
            _thumbnailCts = new CancellationTokenSource();
            var token = _thumbnailCts.Token;

            var detail = await _travelRecordsService.GetDetailAsync(kind, season, token);
            Title = detail.Title;
            Places = new ObservableCollection<TravelPlaceCardItem>(
                detail.Places.Select(item => new TravelPlaceCardItem(item)));
            Countries = new ObservableCollection<TravelCountryCardItem>(
                detail.Countries.Select(item => new TravelCountryCardItem(item)));
            FarthestPlaces = new ObservableCollection<TravelFarthestCardItem>(
                detail.FarthestPlaces.Select(item => new TravelFarthestCardItem(item)));

            ShowPlaces = Places.Count > 0;
            ShowCountries = Countries.Count > 0;
            ShowFarthest = FarthestPlaces.Count > 0;
            ShowSeasonAction = _activeSeason is not null;
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

    private async Task LoadOneAsync(
        Guid? mediaId,
        string path,
        Action<BitmapImage?> setImage,
        CancellationToken token)
    {
        if (mediaId is null || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var thumb = await _thumbnailService.GetOrCreateThumbnailAsync(mediaId.Value, path, token);
        if (string.IsNullOrWhiteSpace(thumb))
        {
            return;
        }

        await EnqueueAsync(() => setImage(new BitmapImage(new Uri(thumb))));
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
