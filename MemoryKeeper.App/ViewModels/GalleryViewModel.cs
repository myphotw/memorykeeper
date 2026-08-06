using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemoryKeeper.App.Diagnostics;
using MemoryKeeper.App.Models;
using MemoryKeeper.App.Services;
using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Infrastructure.Services.Api;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;

namespace MemoryKeeper.App.ViewModels;

public partial class GalleryViewModel : ObservableObject
{
    public const int DefaultPageSize = 50;

    private readonly IGalleryApiRepository _galleryApiRepository;
    private readonly BaseApiClient _apiClient;
    private readonly IPhotoNavigationState _photoNavigationState;
    private readonly IGalleryFocusState _galleryFocusState;
    private readonly ILogger<GalleryViewModel> _logger;
    private readonly DispatcherQueue? _dispatcherQueue;
    private CancellationTokenSource? _thumbnailCts;
    private CancellationTokenSource? _searchCts;

    private int _currentPage = 1;
    private int _totalCount;
    private GalleryTreeNode? _pagingNode;

    [ObservableProperty]
    private ObservableCollection<GalleryTreeNode> treeRoots = [];

    [ObservableProperty]
    private ObservableCollection<GalleryTreeNode> visibleTreeNodes = [];

    [ObservableProperty]
    private GalleryTreeNode? selectedNode;

    [ObservableProperty]
    private ObservableCollection<GalleryItem> items = [];

    [ObservableProperty]
    private GalleryItem? selectedItem;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private string breadcrumbText = "사진첩";

    [ObservableProperty]
    private string statusMessage = "사진을 불러오는 중…";

    [ObservableProperty]
    private bool isBusy;

    /// <summary>0 = 연도 보기, 1 = 장소 보기.</summary>
    [ObservableProperty]
    private int browseModeIndex;

    public bool IsYearBrowseMode => BrowseModeIndex == 0;

    public bool IsPlaceBrowseMode => BrowseModeIndex == 1;

    /// <summary>Infinite-scroll prep: more pages available from Backend.</summary>
    public bool CanLoadMore => Items.Count < _totalCount;

    public int CurrentPage => _currentPage;

    public int PageSize => DefaultPageSize;

    public int TotalCount => _totalCount;

    public event EventHandler? BackRequested;

    public event EventHandler<Guid>? ScrollToMediaRequested;

    public event EventHandler<double>? ScrollOffsetRequested;

    public GalleryViewModel(
        IGalleryApiRepository galleryApiRepository,
        BaseApiClient apiClient,
        IPhotoNavigationState photoNavigationState,
        IGalleryFocusState galleryFocusState,
        ILogger<GalleryViewModel> logger)
    {
        GalleryDiagnostics.WriteStep("GalleryViewModel Created");
        _galleryApiRepository = galleryApiRepository;
        _apiClient = apiClient;
        _photoNavigationState = photoNavigationState;
        _galleryFocusState = galleryFocusState;
        _logger = logger;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    [RelayCommand]
    private void GoBack() => BackRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private async Task LoadAsync()
    {
        GalleryDiagnostics.WriteStep("GalleryViewModel.LoadAsync start");
        var restore = _galleryFocusState.ConsumeRestore();
        await RunBusyAsync(async () =>
        {
            await RebuildTreeRootsAsync();
            if (restore is not null)
            {
                await RestoreSnapshotAsync(restore);
            }
            else
            {
                var allNode = TreeRoots.FirstOrDefault(node => node.Kind == GalleryTreeNodeKind.All);
                if (allNode is not null)
                {
                    await SelectNodeAsync(allNode);
                }
            }
        }, "LoadAsync");
        GalleryDiagnostics.WriteStep($"GalleryViewModel.LoadAsync finish Items={Items.Count}");
    }

    public void CaptureFocusState(double gridScrollOffset, Guid? mediaId = null)
    {
        _galleryFocusState.Save(new GalleryFocusSnapshot
        {
            SearchText = SearchText,
            SelectedNodeKey = SelectedNode?.BuildNodeKey(),
            ExpandedNodeKeys = CollectExpandedNodeKeys(),
            FocusMediaId = mediaId ?? SelectedItem?.MediaId,
            GridScrollOffset = gridScrollOffset,
            BrowseModeIndex = BrowseModeIndex
        });
    }

    partial void OnBrowseModeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsYearBrowseMode));
        OnPropertyChanged(nameof(IsPlaceBrowseMode));
    }

    [RelayCommand]
    private async Task SelectYearBrowseAsync()
    {
        if (BrowseModeIndex == 0)
        {
            return;
        }

        BrowseModeIndex = 0;
        await SwitchBrowseModeAsync();
    }

    [RelayCommand]
    private async Task SelectPlaceBrowseAsync()
    {
        if (BrowseModeIndex == 1)
        {
            return;
        }

        BrowseModeIndex = 1;
        await SwitchBrowseModeAsync();
    }

    private async Task SwitchBrowseModeAsync()
    {
        await RunBusyAsync(async () =>
        {
            await RebuildTreeRootsAsync();
            var first = TreeRoots.FirstOrDefault(node => node.Kind != GalleryTreeNodeKind.Separator);
            if (first is not null)
            {
                await SelectNodeAsync(first);
            }
        }, "SwitchBrowseMode");
    }

    [RelayCommand]
    private async Task ToggleNodeAsync(GalleryTreeNode? node)
    {
        if (node is null || !node.CanExpand)
        {
            return;
        }

        if (node.IsExpanded)
        {
            node.IsExpanded = false;
            RebuildVisibleTree();
            return;
        }

        await EnsureChildrenAsync(node);
        node.IsExpanded = true;
        RebuildVisibleTree();
    }

    [RelayCommand]
    private async Task SelectTreeNodeAsync(GalleryTreeNode? node)
    {
        if (node is null || node.Kind == GalleryTreeNodeKind.Separator)
        {
            return;
        }

        await SelectNodeAsync(node);
    }

    [RelayCommand]
    private void OpenPhotoViewer(GalleryItem? item)
    {
        if (item is null)
        {
            return;
        }

        var playlist = Items.Select(galleryItem => galleryItem.MediaId).Distinct().ToList();
        _photoNavigationState.RequestOpenViewer(item.MediaId, playlist, "gallery");
    }

    partial void OnSearchTextChanged(string value) => _ = DebouncedSearchAsync();

    private async Task DebouncedSearchAsync()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;
        try
        {
            await Task.Delay(250, token);
            if (IsPlaceBrowseMode)
            {
                await RebuildPlaceTreeRootsAsync();
                var first = TreeRoots.FirstOrDefault();
                if (first is not null)
                {
                    await SelectNodeAsync(first);
                }
                else
                {
                    Items = [];
                    StatusMessage = "검색 결과가 없습니다.";
                }
            }
            else if (SelectedNode is not null)
            {
                await QueryForNodeAsync(SelectedNode);
            }
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
    }

    private async Task RebuildTreeRootsAsync()
    {
        if (IsPlaceBrowseMode)
        {
            await RebuildPlaceTreeRootsAsync();
            return;
        }

        await RebuildYearTreeRootsAsync();
    }

    private async Task RebuildYearTreeRootsAsync()
    {
        // Sidebar counts from Backend gallery list/search (not SQLite).
        var allPage = await _galleryApiRepository.GetPhotosAsync(page: 1, pageSize: 1);
        var favoritePage = await _galleryApiRepository.SearchAsync(favorite: true, page: 1, pageSize: 1);

        var roots = new List<GalleryTreeNode>
        {
            new()
            {
                Kind = GalleryTreeNodeKind.All,
                Title = "전체",
                Count = allPage.TotalCount,
                Depth = 0,
                CanExpand = false,
                IsSelected = true
            },
            new() { Kind = GalleryTreeNodeKind.Separator, Title = "—", Depth = 0 },
            new()
            {
                Kind = GalleryTreeNodeKind.Favorites,
                Title = "즐겨찾기",
                Count = favoritePage.TotalCount,
                Depth = 0
            }
        };

        TreeRoots = new ObservableCollection<GalleryTreeNode>(roots);
        RebuildVisibleTree();
    }

    private async Task RebuildPlaceTreeRootsAsync()
    {
        // Place browse remains available in UI; Backend V1.0 has no place-tree API.
        // Keep empty roots rather than SQLite hierarchy for Gallery photo source consistency.
        TreeRoots = new ObservableCollection<GalleryTreeNode>();
        RebuildVisibleTree();
        await Task.CompletedTask;
    }

    private async Task EnsureChildrenAsync(GalleryTreeNode node)
    {
        if (node.ChildrenLoaded || !node.CanExpand)
        {
            return;
        }

        // V2: Gallery photo source is Backend API; SQLite hierarchy expansion removed.
        node.IsBusy = true;
        try
        {
            node.Children.Clear();
            node.ChildrenLoaded = true;
            await Task.CompletedTask;
        }
        finally
        {
            node.IsBusy = false;
        }
    }

    private void RebuildVisibleTree()
    {
        var visible = new List<GalleryTreeNode>();
        foreach (var root in TreeRoots)
        {
            AppendVisible(root, visible);
        }

        VisibleTreeNodes = new ObservableCollection<GalleryTreeNode>(visible);
    }

    private static void AppendVisible(GalleryTreeNode node, List<GalleryTreeNode> visible)
    {
        visible.Add(node);
        if (!node.IsExpanded)
        {
            return;
        }

        foreach (var child in node.Children)
        {
            AppendVisible(child, visible);
        }
    }

    private async Task SelectNodeAsync(GalleryTreeNode node)
    {
        foreach (var visible in VisibleTreeNodes)
        {
            visible.IsSelected = ReferenceEquals(visible, node);
        }

        SelectedNode = node;
        BreadcrumbText = BuildBreadcrumb(node);
        await QueryForNodeAsync(node);
    }

    private async Task RestoreSnapshotAsync(GalleryFocusSnapshot snapshot)
    {
        BrowseModeIndex = snapshot.BrowseModeIndex;
        if (!string.IsNullOrWhiteSpace(snapshot.SearchText))
        {
            SearchText = snapshot.SearchText;
        }

        await RebuildTreeRootsAsync();
        await ExpandNodesByKeysAsync(snapshot.ExpandedNodeKeys);
        var node = FindNodeByKey(snapshot.SelectedNodeKey)
                   ?? TreeRoots.FirstOrDefault(n => n.Kind != GalleryTreeNodeKind.Separator);
        if (node is not null)
        {
            await SelectNodeAsync(node);
        }

        if (snapshot.FocusMediaId is Guid mediaId)
        {
            ScrollToMediaRequested?.Invoke(this, mediaId);
        }
        else if (snapshot.GridScrollOffset > 0)
        {
            ScrollOffsetRequested?.Invoke(this, snapshot.GridScrollOffset);
        }
    }

    private List<string> CollectExpandedNodeKeys()
    {
        var keys = new List<string>();
        foreach (var root in TreeRoots)
        {
            CollectExpanded(root, keys);
        }

        return keys;
    }

    private static void CollectExpanded(GalleryTreeNode node, List<string> keys)
    {
        if (node.IsExpanded)
        {
            keys.Add(node.BuildNodeKey());
        }

        foreach (var child in node.Children)
        {
            CollectExpanded(child, keys);
        }
    }

    private async Task ExpandNodesByKeysAsync(IReadOnlyList<string> keys)
    {
        if (keys.Count == 0)
        {
            return;
        }

        foreach (var root in TreeRoots)
        {
            await ExpandNodePathAsync(root, keys);
        }

        RebuildVisibleTree();
    }

    private async Task ExpandNodePathAsync(GalleryTreeNode node, IReadOnlyList<string> keys)
    {
        if (keys.Contains(node.BuildNodeKey()) && node.CanExpand)
        {
            await EnsureChildrenAsync(node);
            node.IsExpanded = true;
        }

        foreach (var child in node.Children)
        {
            await ExpandNodePathAsync(child, keys);
        }
    }

    private GalleryTreeNode? FindNodeByKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        foreach (var root in TreeRoots)
        {
            var found = FindNodeByKey(root, key);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static GalleryTreeNode? FindNodeByKey(GalleryTreeNode node, string key)
    {
        if (string.Equals(node.BuildNodeKey(), key, StringComparison.Ordinal))
        {
            return node;
        }

        foreach (var child in node.Children)
        {
            var found = FindNodeByKey(child, key);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private async Task QueryForNodeAsync(GalleryTreeNode node)
    {
        CancelThumbnailLoading();
        _pagingNode = node;
        _currentPage = 1;
        GalleryDiagnostics.WriteStep($"Gallery API query node={node.Kind}, title={node.Title}");

        try
        {
            var page = await FetchPhotosPageAsync(node, page: 1);
            _totalCount = page.TotalCount;
            var first = page.Items.FirstOrDefault();
            var firstMapped = first is null
                ? null
                : GalleryBackendMapper.ToGalleryMedia(first, _apiClient.ApiBaseUrl);

            _logger.LogInformation(
                "Gallery load. total_count={TotalCount}, items.Count={ItemsCount}, first.file_id={FileId}, thumbnail_url_raw={ThumbRaw}, thumbnail_url_abs={ThumbAbs}, apiBaseUrl={ApiBaseUrl}",
                page.TotalCount,
                page.Items.Count,
                first?.FileId,
                first?.ThumbnailUrl,
                firstMapped?.ThumbnailUrl,
                _apiClient.ApiBaseUrl);

            var galleryItems = page.Items
                .Select(photo => new GalleryItem(GalleryBackendMapper.ToGalleryMedia(photo, _apiClient.ApiBaseUrl)))
                .Where(item => item.MediaId != Guid.Empty)
                .ToList();

            Items = new ObservableCollection<GalleryItem>(galleryItems);
            StatusMessage = galleryItems.Count == 0
                ? "표시할 사진이 없습니다."
                : $"{node.Title} · {galleryItems.Count}/{_totalCount}장";

            _logger.LogInformation(
                "Gallery ViewModel collection Count={Count} (filtered from API items={ApiCount})",
                Items.Count,
                page.Items.Count);
            GalleryDiagnostics.WriteStep(
                $"Gallery items mapped Count={Items.Count}, total_count={_totalCount}, firstFileId={first?.FileId}");

            _photoNavigationState.SetPlaylist(galleryItems.Select(i => i.MediaId).ToList());
            _ = LoadThumbnailsAsync(galleryItems);
        }
        catch (Exception ex)
        {
            GalleryDiagnostics.WriteException("IGalleryApiRepository.GetPhotosAsync", ex);
            Items = [];
            _totalCount = 0;
            StatusMessage = "사진을 불러오는 중 오류가 발생했습니다.";
            throw;
        }
    }

    /// <summary>
    /// Infinite-scroll preparation: loads the next Backend page and appends items.
    /// Not wired to Gallery UI in this step.
    /// </summary>
    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (!CanLoadMore || _pagingNode is null || IsBusy)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var nextPage = _currentPage + 1;
            var page = await FetchPhotosPageAsync(_pagingNode, nextPage);
            _currentPage = nextPage;
            _totalCount = page.TotalCount;

            var appended = new List<GalleryItem>();
            foreach (var photo in page.Items)
            {
                var item = new GalleryItem(GalleryBackendMapper.ToGalleryMedia(photo, _apiClient.ApiBaseUrl));
                if (item.MediaId == Guid.Empty)
                {
                    continue;
                }

                Items.Add(item);
                appended.Add(item);
            }

            StatusMessage = $"{_pagingNode.Title} · {Items.Count}/{_totalCount}장";
            _photoNavigationState.SetPlaylist(Items.Select(i => i.MediaId).ToList());
            _ = LoadThumbnailsAsync(appended);
        }, "LoadMoreAsync");
    }

    private Task<Application.DTOs.PagedResult<Application.DTOs.Gallery.PhotoDto>> FetchPhotosPageAsync(
        GalleryTreeNode node,
        int page)
    {
        var keyword = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim();
        return node.Kind switch
        {
            GalleryTreeNodeKind.Favorites => _galleryApiRepository.SearchAsync(
                favorite: true,
                keyword: keyword,
                page: page,
                pageSize: DefaultPageSize),
            GalleryTreeNodeKind.Year when node.Year is int year => _galleryApiRepository.SearchAsync(
                year: year,
                keyword: keyword,
                page: page,
                pageSize: DefaultPageSize),
            GalleryTreeNodeKind.Country => _galleryApiRepository.SearchAsync(
                country: node.Country,
                keyword: keyword,
                page: page,
                pageSize: DefaultPageSize),
            GalleryTreeNodeKind.City => _galleryApiRepository.SearchAsync(
                country: node.Country,
                city: node.City,
                keyword: keyword,
                page: page,
                pageSize: DefaultPageSize),
            _ when !string.IsNullOrWhiteSpace(keyword) => _galleryApiRepository.SearchAsync(
                keyword: keyword,
                page: page,
                pageSize: DefaultPageSize),
            _ => _galleryApiRepository.GetPhotosAsync(
                page: page,
                pageSize: DefaultPageSize),
        };
    }

    private GalleryHierarchyQuery BuildQuery(GalleryTreeNode node) =>
        new()
        {
            SearchText = IsPlaceBrowseMode
                ? null
                : (string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim()),
            FavoritesOnly = node.Kind == GalleryTreeNodeKind.Favorites,
            RecentOnly = node.Kind == GalleryTreeNodeKind.Recent,
            PendingOnly = node.Kind == GalleryTreeNodeKind.Pending,
            Year = node.Kind is GalleryTreeNodeKind.All or GalleryTreeNodeKind.Favorites
                or GalleryTreeNodeKind.Recent or GalleryTreeNodeKind.Pending or GalleryTreeNodeKind.PlaceBrowse
                ? null
                : node.Year,
            Country = node.Kind is GalleryTreeNodeKind.Country or GalleryTreeNodeKind.City or GalleryTreeNodeKind.Place
                ? node.Country
                : null,
            City = node.Kind is GalleryTreeNodeKind.City or GalleryTreeNodeKind.Place
                ? node.City
                : null,
            PlaceId = node.Kind is GalleryTreeNodeKind.Place or GalleryTreeNodeKind.PlaceBrowse or GalleryTreeNodeKind.PlaceYear
                ? node.PlaceId
                : null,
            UnclassifiedOnly = node.Kind == GalleryTreeNodeKind.Unclassified
        };

    private string BuildBreadcrumb(GalleryTreeNode node)
    {
        var parts = new List<string> { "사진첩" };
        switch (node.Kind)
        {
            case GalleryTreeNodeKind.All:
                parts.Add("전체");
                break;
            case GalleryTreeNodeKind.Favorites:
                parts.Add("즐겨찾기");
                break;
            case GalleryTreeNodeKind.Recent:
                parts.Add("최근 등록");
                break;
            case GalleryTreeNodeKind.Pending:
                parts.Add("미완성 추억");
                break;
            case GalleryTreeNodeKind.Year:
                parts.Add(node.Year?.ToString() ?? node.Title);
                break;
            case GalleryTreeNodeKind.Unclassified:
                parts.Add(node.Year?.ToString() ?? "");
                parts.Add(LibraryConstants.UnclassifiedTitle);
                break;
            case GalleryTreeNodeKind.Country:
                parts.Add(node.Year?.ToString() ?? "");
                parts.Add(node.Country ?? node.Title);
                break;
            case GalleryTreeNodeKind.City:
                parts.Add(node.Year?.ToString() ?? "");
                parts.Add(node.Country ?? "");
                parts.Add(node.City ?? node.Title);
                break;
            case GalleryTreeNodeKind.Place:
                parts.Add(node.Year?.ToString() ?? "");
                parts.Add(node.Country ?? "");
                parts.Add(node.City ?? "");
                parts.Add(node.Title);
                break;
            case GalleryTreeNodeKind.PlaceBrowse:
                parts.Add("장소");
                parts.Add(node.Title);
                break;
            case GalleryTreeNodeKind.PlaceYear:
                parts.Add("장소");
                parts.Add(FindPlaceBrowseTitle(node.PlaceId) ?? "장소");
                parts.Add(node.Year?.ToString() ?? node.Title);
                break;
        }

        return string.Join(" > ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private string? FindPlaceBrowseTitle(Guid? placeId)
    {
        if (placeId is null)
        {
            return null;
        }

        return TreeRoots.FirstOrDefault(node =>
                node.Kind == GalleryTreeNodeKind.PlaceBrowse && node.PlaceId == placeId)
            ?.Title;
    }

    private async Task LoadThumbnailsAsync(IReadOnlyList<GalleryItem> galleryItems)
    {
        CancelThumbnailLoading();
        _thumbnailCts = new CancellationTokenSource();
        var token = _thumbnailCts.Token;

        try
        {
            foreach (var item in galleryItems)
            {
                token.ThrowIfCancellationRequested();
                if (item.HasThumbnail && item.ThumbnailImage is not null)
                {
                    continue;
                }

                item.IsThumbnailLoading = true;
                try
                {
                    // Backend gallery: ThumbnailUrl only (absolute HTTP).
                    var remoteUrl = item.Media.ThumbnailUrl;
                    if (string.IsNullOrWhiteSpace(remoteUrl))
                    {
                        _logger.LogWarning(
                            "Gallery thumbnail missing ThumbnailUrl. MediaId={MediaId}, BackendFileId={FileId}",
                            item.MediaId,
                            item.BackendFileId);
                        item.HasThumbnail = false;
                        continue;
                    }

                    await EnqueueAsync(() =>
                    {
                        var bitmap = HttpImageLoader.TryCreate(
                            remoteUrl,
                            _logger,
                            context: $"GalleryThumbnail:{item.BackendFileId}");
                        item.ThumbnailImage = bitmap;
                        item.HasThumbnail = bitmap is not null;
                        if (bitmap is null)
                        {
                            _logger.LogWarning(
                                "Gallery ThumbnailImageSource null. Url={Url}",
                                remoteUrl);
                        }
                    });
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Gallery thumbnail failed. MediaId={MediaId}, Url={Url}",
                        item.MediaId,
                        item.Media.ThumbnailUrl);
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
            // expected on filter change
        }
    }

    public Task EnsureThumbnailAsync(GalleryItem item) => LoadThumbnailsAsync([item]);

    private Task EnqueueAsync(Action action)
    {
        if (_dispatcherQueue is null)
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
            tcs.SetException(new InvalidOperationException("Failed to enqueue UI update."));
        }

        return tcs.Task;
    }

    private void CancelThumbnailLoading()
    {
        _thumbnailCts?.Cancel();
        _thumbnailCts?.Dispose();
        _thumbnailCts = null;
    }

    private async Task RunBusyAsync(Func<Task> action, string stage)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            GalleryDiagnostics.WriteException(stage, ex);
            StatusMessage = "오류가 발생했습니다.";
            _logger.LogError(ex, "Gallery stage failed. Stage={Stage}", stage);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
