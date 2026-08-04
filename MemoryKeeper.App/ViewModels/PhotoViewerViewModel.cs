using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemoryKeeper.App.Models;
using MemoryKeeper.App.Services;
using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Layout;
using MemoryKeeper.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;

namespace MemoryKeeper.App.ViewModels;

public partial class PhotoViewerViewModel : ObservableObject
{
    private readonly PhotoDetailService _photoDetailService;
    private readonly IPhotoNavigationState _photoNavigationState;
    private readonly IPlaceFocusState _placeFocusState;
    private readonly IThumbnailService _thumbnailService;
    private readonly ILogger<PhotoViewerViewModel> _logger;
    private readonly DispatcherQueue? _dispatcherQueue;
    private BitmapImage? _previousImage;
    private string? _preloadedNextPath;
    private string? _preloadedPreviousPath;
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
        PhotoDetailService photoDetailService,
        IPhotoNavigationState photoNavigationState,
        IPlaceFocusState placeFocusState,
        IThumbnailService thumbnailService,
        ILogger<PhotoViewerViewModel> logger)
    {
        _photoDetailService = photoDetailService;
        _photoNavigationState = photoNavigationState;
        _placeFocusState = placeFocusState;
        _thumbnailService = thumbnailService;
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
        if (item is null || item.MediaId == _photoNavigationState.FocusMediaId)
        {
            return;
        }

        await LoadMediaAsync(item.MediaId, slideDirection: 0);
    }

    public void ApplyBreakpoint(LayoutBreakpoint breakpoint)
    {
        var radius = ResponsiveLayoutRules.FilmStripVisibleRadius(breakpoint);
        if (radius == _filmStripRadius)
        {
            return;
        }

        _filmStripRadius = radius;
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

            var detail = await _photoDetailService.GetPhotoDetailAsync(mediaId);
            _photoNavigationState.FocusMediaId = mediaId;
            _currentMediaId = mediaId;
            RefreshNavigationState();

            CapturedAtText = detail.CapturedAt?.ToLocalTime().ToString("yyyy.MM.dd HH:mm", CultureInfo.InvariantCulture) ?? "-";
            PlaceOverlayText = BuildPlaceOverlay(detail);
            PlaceId = detail.PlaceId;
            Country = detail.Country;
            HasGps = detail.HasGps;

            var path = ResolveImagePath(detail);
            var image = LoadImage(path);
            DisposePreviousImage();
            _previousImage = PhotoImage;
            PhotoImage = image;

            await RefreshFilmStripAsync(mediaId);
            await PreloadAdjacentAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load photo viewer media. MediaId={MediaId}", mediaId);
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
                var detail = await _photoDetailService.GetPhotoDetailAsync(item.MediaId);
                token.ThrowIfCancellationRequested();
                item.AbsoluteLibraryPath = detail.AbsoluteLibraryPath;
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
            if (string.IsNullOrWhiteSpace(item.AbsoluteLibraryPath) || !File.Exists(item.AbsoluteLibraryPath))
            {
                continue;
            }

            try
            {
                var path = await _thumbnailService.GetOrCreateThumbnailAsync(
                    item.MediaId,
                    item.AbsoluteLibraryPath,
                    cancellationToken);
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    continue;
                }

                Enqueue(() => item.ThumbnailImage = new BitmapImage(new Uri(path)));
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

    private void Enqueue(Action action)
    {
        if (_dispatcherQueue is null || _dispatcherQueue.HasThreadAccess)
        {
            action();
            return;
        }

        _dispatcherQueue.TryEnqueue(() => action());
    }

    private async Task PreloadAdjacentAsync()
    {
        _preloadedNextPath = null;
        _preloadedPreviousPath = null;

        if (_photoNavigationState.TryGetNext(out var nextId))
        {
            try
            {
                var next = await _photoDetailService.GetPhotoDetailAsync(nextId);
                _preloadedNextPath = ResolveImagePath(next);
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
                var previous = await _photoDetailService.GetPhotoDetailAsync(previousId);
                _preloadedPreviousPath = ResolveImagePath(previous);
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

    private static string? ResolveImagePath(PhotoDetailDto detail)
    {
        if (!string.IsNullOrWhiteSpace(detail.AbsoluteLibraryPath) && File.Exists(detail.AbsoluteLibraryPath))
        {
            return detail.AbsoluteLibraryPath;
        }

        if (!string.IsNullOrWhiteSpace(detail.OriginalPath) && File.Exists(detail.OriginalPath))
        {
            return detail.OriginalPath;
        }

        return null;
    }

    private static BitmapImage? LoadImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        return new BitmapImage
        {
            CreateOptions = BitmapCreateOptions.IgnoreImageCache,
            UriSource = new Uri(path)
        };
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
