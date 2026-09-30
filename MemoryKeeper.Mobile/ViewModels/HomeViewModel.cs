using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Mobile.Http;
using MemoryKeeper.Mobile.Images;
using MemoryKeeper.Mobile.Models;

namespace MemoryKeeper.Mobile.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly FastGalleryPagingService _paging;
    private readonly IMobileThumbnailSourceFactory _thumbnailSourceFactory;
    private readonly object _requestLock = new();
    private CancellationTokenSource? _requestCancellation;
    private bool _initialized;
    private double _tileSize = 120;

    [ObservableProperty]
    private bool isInitialLoading;

    [ObservableProperty]
    private bool isRefreshing;

    [ObservableProperty]
    private bool isLoadingMore;

    [ObservableProperty]
    private bool hasMore = true;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string? nextCursor;

    public HomeViewModel(
        FastGalleryPagingService paging,
        IMobileThumbnailSourceFactory thumbnailSourceFactory)
    {
        _paging = paging;
        _thumbnailSourceFactory = thumbnailSourceFactory;
        Items.CollectionChanged += OnItemsChanged;
    }

    public ObservableCollection<MobileGalleryItem> Items { get; } = [];

    public bool HasItems => Items.Count > 0;

    public bool IsGalleryVisible => HasItems;

    public bool IsEmptyStateVisible => !IsInitialLoading && !HasItems && string.IsNullOrWhiteSpace(ErrorMessage);

    public bool IsErrorStateVisible => !IsInitialLoading && !HasItems && !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool IsInlineErrorVisible => HasItems && !string.IsNullOrWhiteSpace(ErrorMessage);

    public async Task InitializeAsync()
    {
        if (_initialized && Items.Count > 0)
        {
            return;
        }

        _initialized = true;
        await LoadFirstPageAsync(isRefresh: false);
    }

    [RelayCommand]
    public Task RefreshAsync() => LoadFirstPageAsync(isRefresh: true);

    public async Task LoadMoreAsync()
    {
        if (IsInitialLoading || IsRefreshing || IsLoadingMore || !HasMore)
        {
            return;
        }

        IsLoadingMore = true;
        ErrorMessage = null;
        var request = BeginRequest(cancelCurrent: false);
        try
        {
            var update = await _paging.LoadNextPageAsync(request.Token);
            if (!update.Applied || request.IsCancellationRequested)
            {
                return;
            }

            foreach (var item in update.AddedItems)
            {
                Items.Add(ToMobileItem(item));
            }

            ApplyContinuation(update);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ErrorMessage = ToUserMessage(ex);
        }
        finally
        {
            EndRequest(request);
            IsLoadingMore = false;
        }
    }

    public void CancelPendingRequests()
    {
        lock (_requestLock)
        {
            _requestCancellation?.Cancel();
        }
    }

    public void HandleUnexpectedLoadFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _initialized = false;
        IsInitialLoading = false;
        IsRefreshing = false;
        IsLoadingMore = false;
        ErrorMessage = ToUserMessage(exception);
    }

    public void SetTileSize(double tileSize)
    {
        _tileSize = tileSize;
        foreach (var item in Items)
        {
            item.TileSize = tileSize;
        }
    }

    private async Task LoadFirstPageAsync(bool isRefresh)
    {
        if (isRefresh)
        {
            IsRefreshing = true;
        }
        else
        {
            IsInitialLoading = true;
        }

        ErrorMessage = null;
        var request = BeginRequest(cancelCurrent: true);
        try
        {
            var update = await _paging.LoadFirstPageAsync(request.Token);
            if (request.IsCancellationRequested)
            {
                return;
            }

            var replacement = update.Items.Select(ToMobileItem).ToArray();
            Items.Clear();
            foreach (var item in replacement)
            {
                Items.Add(item);
            }

            ApplyContinuation(update);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ErrorMessage = ToUserMessage(ex);
        }
        finally
        {
            EndRequest(request);
            IsInitialLoading = false;
            IsRefreshing = false;
        }
    }

    private MobileGalleryItem ToMobileItem(FastGalleryPhotoDto photo) => new()
    {
        FileId = photo.FileId,
        TileSize = _tileSize,
        // Grid policy: use only thumbnail_url. Never fall back to preview or original.
        ThumbnailSource = _thumbnailSourceFactory.Create(photo.FileId, photo.ThumbnailUrl),
    };

    private void ApplyContinuation(FastGalleryPagingUpdate update)
    {
        NextCursor = update.NextCursor;
        HasMore = update.HasMore;
    }

    private CancellationTokenSource BeginRequest(bool cancelCurrent)
    {
        lock (_requestLock)
        {
            if (cancelCurrent)
            {
                _requestCancellation?.Cancel();
            }

            var request = new CancellationTokenSource();
            _requestCancellation = request;
            return request;
        }
    }

    private void EndRequest(CancellationTokenSource request)
    {
        lock (_requestLock)
        {
            if (ReferenceEquals(_requestCancellation, request))
            {
                _requestCancellation = null;
            }
        }

        request.Dispose();
    }

    private static string ToUserMessage(Exception exception) => exception switch
    {
        MobileBackendConfigurationException => "사진첩 연결 설정을 확인할 수 없습니다.",
        MobileBackendApiException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } =>
            "사진첩에 연결할 수 없습니다.",
        _ => "사진첩을 불러올 수 없습니다. 잠시 후 다시 시도해 주세요.",
    };

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => NotifyViewStateChanged();

    partial void OnIsInitialLoadingChanged(bool value) => NotifyViewStateChanged();

    partial void OnErrorMessageChanged(string? value) => NotifyViewStateChanged();

    private void NotifyViewStateChanged()
    {
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(IsGalleryVisible));
        OnPropertyChanged(nameof(IsEmptyStateVisible));
        OnPropertyChanged(nameof(IsErrorStateVisible));
        OnPropertyChanged(nameof(IsInlineErrorVisible));
    }
}
