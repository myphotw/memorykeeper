using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemoryKeeper.App.Models;
using MemoryKeeper.App.Services;
using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Layout;
using MemoryKeeper.Infrastructure.Services.Api;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;

namespace MemoryKeeper.App.ViewModels;

public partial class PhotoViewerViewModel : ObservableObject
{
    private readonly IGalleryApiRepository _galleryApiRepository;
    private readonly BaseApiClient _apiClient;
    private readonly IPhotoNavigationState _photoNavigationState;
    private readonly IPlaceFocusState _placeFocusState;
    private readonly ILogger<PhotoViewerViewModel> _logger;
    private readonly DispatcherQueue? _dispatcherQueue;
    private BitmapImage? _previousImage;
    private string? _preloadedNextUrl;
    private string? _preloadedPreviousUrl;
    private CancellationTokenSource? _filmStripCts;
    private int _filmStripRadius = ResponsiveLayoutRules.FilmStripVisibleRadius(LayoutBreakpoint.Medium);
    private Guid? _currentMediaId;

    [ObservableProperty] private BitmapImage? photoImage;
    [ObservableProperty] private string capturedAtText = "-";
    [ObservableProperty] private string placeOverlayText = "장소 미등록";
    [ObservableProperty] private bool canGoPrevious;
    [ObservableProperty] private bool canGoNext;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private Guid? placeId;
    [ObservableProperty] private string? country;
    [ObservableProperty] private bool hasGps;
    [ObservableProperty] private ObservableCollection<FilmStripItem> filmStripItems = [];
    [ObservableProperty] private FilmStripItem? currentFilmStripItem;

    public event EventHandler? Closed;
    public event EventHandler? OpenDetailRequested;
    public event EventHandler? OpenMapRequested;
    public event EventHandler? FilmStripUpdated;
    public event EventHandler<int>? NavigateSlideRequested;

    public PhotoViewerViewModel(
        IGalleryApiRepository galleryApiRepository,
        BaseApiClient apiClient,
        IPhotoNavigationState photoNavigationState,
        IPlaceFocusState placeFocusState,
        ILogger<PhotoViewerViewModel> logger)
    {
        _galleryApiRepository = galleryApiRepository;
        _apiClient = apiClient;
        _photoNavigationState = photoNavigationState;
        _placeFocusState = placeFocusState;
        _logger = logger;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        var mediaId = _photoNavigationState.FocusMediaId;
        if (mediaId is null)
        {
            return;
        }

        await LoadMediaAsync(mediaId.Value, slideDirection: 0);
    }

    [RelayCommand]
    private void GoBack() => Closed?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenDetail()
    {
        if (_photoNavigationState.FocusMediaId is Guid mediaId)
        {
            // State only — MainWindow navigates via OpenDetailRequested (single push).
            _photoNavigationState.RequestOpenDetail(mediaId);
            OpenDetailRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private void OpenMap()
    {
        if (_currentMediaId is Guid mediaId)
        {
            _placeFocusState.FocusMediaId = mediaId;
        }

        if (PlaceId is Guid place)
        {
            _placeFocusState.FocusPlaceId = place;
            _placeFocusState.PendingSearchText = null;
        }
        else if (!string.IsNullOrWhiteSpace(PlaceOverlayText) && PlaceOverlayText != "장소 미등록")
        {
            _placeFocusState.FocusPlaceId = null;
            _placeFocusState.PendingSearchText = PlaceOverlayText;
        }
        else
        {
            // Unclassified / no place name — still jump to visit map and try media match.
            _placeFocusState.FocusPlaceId = LibraryConstants.UnclassifiedPlaceId;
            _placeFocusState.PendingSearchText = null;
        }

        OpenMapRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task GoPreviousAsync()
    {
        if (!_photoNavigationState.TryGetPrevious(out var mediaId))
        {
            return;
        }

        await LoadMediaAsync(mediaId, slideDirection: -1);
    }

    [RelayCommand]
    private async Task GoNextAsync()
    {
        if (!_photoNavigationState.TryGetNext(out var mediaId))
        {
            return;
        }

        await LoadMediaAsync(mediaId, slideDirection: 1);
    }

    [RelayCommand]
    private async Task SelectFilmStripItemAsync(FilmStripItem? item)
    {
        if (item is null)
        {
            return;
        }

        await LoadMediaAsync(item.MediaId, slideDirection: 0);
    }

    public void ApplyBreakpoint(LayoutBreakpoint breakpoint)
    {
        _filmStripRadius = ResponsiveLayoutRules.FilmStripVisibleRadius(breakpoint);
        if (_currentMediaId is Guid mediaId)
        {
            _ = RefreshFilmStripAsync(mediaId);
        }
    }

    public async Task LoadMediaAsync(Guid mediaId, int slideDirection = 0)
    {
        IsBusy = true;
        try
        {
            if (slideDirection != 0)
            {
                NavigateSlideRequested?.Invoke(this, slideDirection);
            }

            var apiDetail = await _galleryApiRepository.GetPhotoAsync(mediaId);
            var detail = GalleryBackendMapper.ToPhotoDetail(apiDetail, _apiClient.ApiBaseUrl);
            _photoNavigationState.FocusMediaId = mediaId;
            _currentMediaId = mediaId;
            RefreshNavigationState();

            CapturedAtText = detail.CapturedAt?.ToLocalTime().ToString("yyyy.MM.dd HH:mm", CultureInfo.InvariantCulture) ?? "-";
            PlaceOverlayText = BuildPlaceOverlay(detail);
            PlaceId = detail.PlaceId;
            Country = detail.Country;
            HasGps = detail.HasGps;

            var displayUrl = ResolveDisplayUrl(detail);
            _logger.LogInformation(
                "PhotoViewer display URL. MediaId={MediaId}, BackendFileId={FileId}, PreviewUrl={Preview}, ThumbnailUrl={Thumb}, DisplayUrl={Display}, OriginalUrl={Original}",
                mediaId,
                apiDetail.FileId,
                ApiErrorClassifier.SafePath(detail.PreviewUrl),
                ApiErrorClassifier.SafePath(detail.ThumbnailUrl),
                ApiErrorClassifier.SafePath(displayUrl),
                ApiErrorClassifier.SafePath(detail.OriginalPath));

            BitmapImage? image = null;
            await EnqueueAsync(() =>
            {
                image = LoadDisplayImage(displayUrl, context: $"PhotoViewer:{apiDetail.FileId}");
                DisposePreviousImage();
                _previousImage = PhotoImage;
                PhotoImage = image;
                _logger.LogInformation(
                    "PhotoViewer ImageSource set. IsNull={IsNull}, Url={Url}",
                    image is null,
                    ApiErrorClassifier.SafePath(displayUrl));
            });

            await RefreshFilmStripAsync(mediaId);
            await PreloadAdjacentAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load photo viewer media. MediaId={MediaId}", mediaId);
            await EnqueueAsync(() => PhotoImage = null);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RefreshNavigationState()
    {
        CanGoPrevious = _photoNavigationState.TryGetPrevious(out _);
        CanGoNext = _photoNavigationState.TryGetNext(out _);
    }

    private async Task RefreshFilmStripAsync(Guid currentMediaId)
    {
        _filmStripCts?.Cancel();
        _filmStripCts?.Dispose();
        _filmStripCts = new CancellationTokenSource();
        var token = _filmStripCts.Token;

        var playlist = _photoNavigationState.Playlist;
        if (playlist.Count == 0)
        {
            FilmStripItems = [];
            CurrentFilmStripItem = null;
            return;
        }

        var currentIndex = playlist.ToList().IndexOf(currentMediaId);
        if (currentIndex < 0)
        {
            currentIndex = 0;
        }

        var window = Math.Min(playlist.Count, _filmStripRadius * 2 + 1);
        var items = BuildCenteredWindow(playlist, currentIndex, window, currentMediaId);

        foreach (var item in items)
        {
            try
            {
                var apiDetail = await _galleryApiRepository.GetPhotoAsync(item.MediaId, token);
                token.ThrowIfCancellationRequested();
                var detail = GalleryBackendMapper.ToPhotoDetail(apiDetail, _apiClient.ApiBaseUrl);
                item.AbsoluteLibraryPath = detail.ThumbnailUrl ?? detail.PreviewUrl ?? string.Empty;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Film strip path resolve failed. MediaId={MediaId}", item.MediaId);
            }
        }

        FilmStripItems = new ObservableCollection<FilmStripItem>(items);
        CurrentFilmStripItem = FilmStripItems.FirstOrDefault(item => item.IsCurrent);
        FilmStripUpdated?.Invoke(this, EventArgs.Empty);
        _ = LoadFilmStripThumbnailsAsync(FilmStripItems, token);
    }

    private static List<FilmStripItem> BuildCenteredWindow(
        IReadOnlyList<Guid> playlist,
        int currentIndex,
        int window,
        Guid currentMediaId)
    {
        var half = window / 2;
        var items = new List<FilmStripItem>(window);
        for (var offset = -half; offset <= half && items.Count < window; offset++)
        {
            var index = (currentIndex + offset + playlist.Count * 8) % playlist.Count;
            items.Add(new FilmStripItem
            {
                MediaId = playlist[index],
                IsCurrent = playlist[index] == currentMediaId
            });
        }

        return items;
    }

    private async Task LoadFilmStripThumbnailsAsync(
        IReadOnlyList<FilmStripItem> items,
        CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!HttpImageLoader.IsHttpUrl(item.AbsoluteLibraryPath))
            {
                continue;
            }

            try
            {
                await EnqueueAsync(() =>
                {
                    item.ThumbnailImage = HttpImageLoader.TryCreate(
                        item.AbsoluteLibraryPath,
                        _logger,
                        context: $"FilmStrip:{item.MediaId:N}");
                });
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Film strip thumbnail failed. MediaId={MediaId}", item.MediaId);
            }
        }
    }

    private Task EnqueueAsync(Action action)
    {
        if (_dispatcherQueue is null || _dispatcherQueue.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

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
            tcs.SetException(new InvalidOperationException("Failed to enqueue UI work."));
        }

        return tcs.Task;
    }

    private async Task PreloadAdjacentAsync()
    {
        _preloadedNextUrl = null;
        _preloadedPreviousUrl = null;

        if (_photoNavigationState.TryGetNext(out var nextId))
        {
            try
            {
                var next = GalleryBackendMapper.ToPhotoDetail(
                    await _galleryApiRepository.GetPhotoAsync(nextId),
                    _apiClient.ApiBaseUrl);
                _preloadedNextUrl = ResolveDisplayUrl(next);
                if (HttpImageLoader.IsHttpUrl(_preloadedNextUrl))
                {
                    await EnqueueAsync(() =>
                        _ = HttpImageLoader.TryCreate(_preloadedNextUrl, _logger, "PhotoViewer:PreloadNext"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Preload next failed.");
            }
        }

        if (_photoNavigationState.TryGetPrevious(out var previousId))
        {
            try
            {
                var previous = GalleryBackendMapper.ToPhotoDetail(
                    await _galleryApiRepository.GetPhotoAsync(previousId),
                    _apiClient.ApiBaseUrl);
                _preloadedPreviousUrl = ResolveDisplayUrl(previous);
                if (HttpImageLoader.IsHttpUrl(_preloadedPreviousUrl))
                {
                    await EnqueueAsync(() =>
                        _ = HttpImageLoader.TryCreate(_preloadedPreviousUrl, _logger, "PhotoViewer:PreloadPrevious"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Preload previous failed.");
            }
        }
    }

    private static string BuildPlaceOverlay(PhotoDetailDto detail)
    {
        if (string.IsNullOrWhiteSpace(detail.PlaceName))
        {
            return "장소 미등록";
        }

        var city = string.IsNullOrWhiteSpace(detail.City) ? null : detail.City.Trim();
        var place = detail.PlaceName.Trim();
        return string.IsNullOrWhiteSpace(city) || string.Equals(city, place, StringComparison.OrdinalIgnoreCase)
            ? place
            : $"{city} · {place}";
    }

    /// <summary>PreviewUrl → ThumbnailUrl. Never OriginalUrl for auto display.</summary>
    internal static string? ResolveDisplayUrl(PhotoDetailDto detail)
    {
        if (HttpImageLoader.IsHttpUrl(detail.PreviewUrl))
        {
            return detail.PreviewUrl;
        }

        if (HttpImageLoader.IsHttpUrl(detail.AbsoluteLibraryPath))
        {
            return detail.AbsoluteLibraryPath;
        }

        if (HttpImageLoader.IsHttpUrl(detail.ThumbnailUrl))
        {
            return detail.ThumbnailUrl;
        }

        if (HttpImageLoader.IsHttpUrl(detail.ThumbnailPath))
        {
            return detail.ThumbnailPath;
        }

        return null;
    }

    private BitmapImage? LoadDisplayImage(string? url, string context)
    {
        if (HttpImageLoader.IsHttpUrl(url))
        {
            return HttpImageLoader.TryCreate(url, _logger, context);
        }

        _logger.LogWarning(
            "PhotoViewer has no HTTP display URL. Context={Context}, Url={Url}",
            context,
            ApiErrorClassifier.SafePath(url));
        return null;
    }

    private void DisposePreviousImage()
    {
        _previousImage = null;
    }

    public void DisposeImages()
    {
        _filmStripCts?.Cancel();
        _filmStripCts?.Dispose();
        _filmStripCts = null;
        DisposePreviousImage();
    }
}
