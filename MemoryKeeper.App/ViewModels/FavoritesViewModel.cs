using MemoryKeeper.App.Models;
using MemoryKeeper.App.Services;
using MemoryKeeper.Application.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Collections.ObjectModel;

namespace MemoryKeeper.App.ViewModels;

public partial class FavoritesViewModel : ObservableObject
{
    private readonly MediaService _mediaService;
    private readonly IThumbnailService _thumbnailService;
    private readonly IPhotoNavigationState _photoNavigationState;
    private readonly ILogger<FavoritesViewModel> _logger;
    private readonly DispatcherQueue _dispatcherQueue;
    private CancellationTokenSource? _thumbnailCts;

    [ObservableProperty]
    private ObservableCollection<GalleryItem> items = [];

    [ObservableProperty]
    private string statusMessage = "즐겨찾기 사진을 불러오는 중…";

    [ObservableProperty]
    private bool isBusy;

    public event EventHandler? BackRequested;

    public FavoritesViewModel(
        MediaService mediaService,
        IThumbnailService thumbnailService,
        IPhotoNavigationState photoNavigationState,
        ILogger<FavoritesViewModel> logger)
    {
        _mediaService = mediaService;
        _thumbnailService = thumbnailService;
        _photoNavigationState = photoNavigationState;
        _logger = logger;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    [RelayCommand]
    private void GoBack() => BackRequested?.Invoke(this, EventArgs.Empty);

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
            var favorites = await _mediaService.GetFavoritesGalleryAsync();
            var galleryItems = favorites.Select(item => new GalleryItem(item)).ToList();
            Items = new ObservableCollection<GalleryItem>(galleryItems);
            StatusMessage = galleryItems.Count == 0
                ? "즐겨찾기한 사진이 없습니다. Photo Detail에서 ★로 추가하세요."
                : $"즐겨찾기 {galleryItems.Count}장";
            _ = LoadThumbnailsAsync(galleryItems);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load favorites.");
            StatusMessage = "즐겨찾기를 불러오지 못했습니다.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenPhoto(GalleryItem? item)
    {
        if (item is null)
        {
            return;
        }

        _photoNavigationState.RequestOpen(item.MediaId);
    }

    private async Task LoadThumbnailsAsync(IReadOnlyList<GalleryItem> galleryItems)
    {
        _thumbnailCts?.Cancel();
        _thumbnailCts = new CancellationTokenSource();
        var token = _thumbnailCts.Token;

        try
        {
            foreach (var item in galleryItems)
            {
                token.ThrowIfCancellationRequested();
                item.IsThumbnailLoading = true;
                try
                {
                    var path = await _thumbnailService.GetOrCreateThumbnailAsync(
                        item.MediaId,
                        item.AbsoluteLibraryPath,
                        token);
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        item.HasThumbnail = false;
                        continue;
                    }

                    await EnqueueAsync(() =>
                    {
                        item.ThumbnailImage = new BitmapImage(new Uri(path));
                        item.HasThumbnail = true;
                    });
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Favorite thumbnail failed. MediaId={MediaId}", item.MediaId);
                    item.HasThumbnail = false;
                }
                finally
                {
                    item.IsThumbnailLoading = false;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // expected
        }
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
