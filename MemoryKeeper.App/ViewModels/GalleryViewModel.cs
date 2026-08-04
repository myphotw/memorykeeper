using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemoryKeeper.App.Diagnostics;
using MemoryKeeper.App.Models;
using MemoryKeeper.App.Services;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;

namespace MemoryKeeper.App.ViewModels;

public partial class GalleryViewModel : ObservableObject
{
    private readonly GalleryHierarchyService _hierarchyService;
    private readonly MediaService _mediaService;
    private readonly IThumbnailService _thumbnailService;
    private readonly IPhotoNavigationState _photoNavigationState;
    private readonly IGalleryFocusState _galleryFocusState;
    private readonly ILogger<GalleryViewModel> _logger;
    private readonly DispatcherQueue? _dispatcherQueue;
    private CancellationTokenSource? _thumbnailCts;
    private CancellationTokenSource? _searchCts;

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

    public event EventHandler? BackRequested;

    public event EventHandler<Guid>? ScrollToMediaRequested;

    public event EventHandler<double>? ScrollOffsetRequested;

    public GalleryViewModel(
        GalleryHierarchyService hierarchyService,
        MediaService mediaService,
        IThumbnailService thumbnailService,
        IPhotoNavigationState photoNavigationState,
        IGalleryFocusState galleryFocusState,
        ILogger<GalleryViewModel> logger)
    {
        GalleryDiagnostics.WriteStep("GalleryViewModel Created");
        _hierarchyService = hierarchyService;
        _mediaService = mediaService;
        _thumbnailService = thumbnailService;
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
        var summary = await _mediaService.GetGallerySidebarSummaryAsync();
        var years = await _hierarchyService.GetYearsAsync();

        var roots = new List<GalleryTreeNode>
        {
            new()
            {
                Kind = GalleryTreeNodeKind.All,
                Title = "전체",
                Count = summary.TotalCount,
                Depth = 0,
                CanExpand = false,
                IsSelected = true
            }
        };

        foreach (var year in years)
        {
            roots.Add(new GalleryTreeNode
            {
                Kind = GalleryTreeNodeKind.Year,
                Year = year.Year,
                Title = year.Year.ToString(),
                Count = year.Count,
                Depth = 0,
                CanExpand = true
            });
        }

        roots.Add(new GalleryTreeNode { Kind = GalleryTreeNodeKind.Separator, Title = "—", Depth = 0 });
        roots.Add(new GalleryTreeNode
        {
            Kind = GalleryTreeNodeKind.Favorites,
            Title = "즐겨찾기",
            Count = summary.FavoriteCount,
            Depth = 0
        });
        roots.Add(new GalleryTreeNode
        {
            Kind = GalleryTreeNodeKind.Recent,
            Title = "최근 등록",
            Count = summary.RecentCount,
            Depth = 0
        });
        roots.Add(new GalleryTreeNode
        {
            Kind = GalleryTreeNodeKind.Pending,
            Title = "미완성 추억",
            Count = summary.PendingCount,
            Depth = 0
        });

        TreeRoots = new ObservableCollection<GalleryTreeNode>(roots);
        RebuildVisibleTree();
    }

    private async Task RebuildPlaceTreeRootsAsync()
    {
        var places = await _hierarchyService.GetPlaceBrowseRootsAsync(
            string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim());
        var roots = places.Select(place => new GalleryTreeNode
        {
            Kind = GalleryTreeNodeKind.PlaceBrowse,
            PlaceId = place.PlaceId,
            PlaceType = place.PlaceType,
            Title = place.Title,
            Count = place.Count,
            Depth = 0,
            CanExpand = true
        }).ToList();

        TreeRoots = new ObservableCollection<GalleryTreeNode>(roots);
        RebuildVisibleTree();
    }

    private async Task EnsureChildrenAsync(GalleryTreeNode node)
    {
        if (node.ChildrenLoaded || !node.CanExpand)
        {
            return;
        }

        node.IsBusy = true;
        try
        {
            node.Children.Clear();
            switch (node.Kind)
            {
                case GalleryTreeNodeKind.Year when node.Year is int year:
                {
                    var countries = await _hierarchyService.GetCountriesAsync(year);
                    foreach (var country in countries)
                    {
                        node.Children.Add(new GalleryTreeNode
                        {
                            Kind = country.IsUnclassified
                                ? GalleryTreeNodeKind.Unclassified
                                : GalleryTreeNodeKind.Country,
                            Year = year,
                            Country = country.Title,
                            Title = country.Title,
                            Count = country.Count,
                            Depth = node.Depth + 1,
                            CanExpand = !country.IsUnclassified
                        });
                    }

                    break;
                }
                case GalleryTreeNodeKind.Country when node.Year is int year && !string.IsNullOrWhiteSpace(node.Country):
                {
                    var cities = await _hierarchyService.GetCitiesAsync(year, node.Country);
                    foreach (var city in cities)
                    {
                        node.Children.Add(new GalleryTreeNode
                        {
                            Kind = GalleryTreeNodeKind.City,
                            Year = year,
                            Country = node.Country,
                            City = city.Title,
                            Title = city.Title,
                            Count = city.Count,
                            Depth = node.Depth + 1,
                            CanExpand = true
                        });
                    }

                    break;
                }
                case GalleryTreeNodeKind.City
                    when node.Year is int year
                         && !string.IsNullOrWhiteSpace(node.Country)
                         && !string.IsNullOrWhiteSpace(node.City):
                {
                    var places = await _hierarchyService.GetPlacesAsync(year, node.Country, node.City);
                    foreach (var place in places)
                    {
                        node.Children.Add(new GalleryTreeNode
                        {
                            Kind = GalleryTreeNodeKind.Place,
                            Year = year,
                            Country = node.Country,
                            City = node.City,
                            PlaceId = place.PlaceId,
                            PlaceType = place.PlaceType,
                            Title = place.Title,
                            Count = place.Count,
                            Depth = node.Depth + 1,
                            CanExpand = false
                        });
                    }

                    break;
                }
                case GalleryTreeNodeKind.PlaceBrowse when node.PlaceId is Guid placeId:
                {
                    var years = await _hierarchyService.GetYearsForPlaceAsync(placeId);
                    foreach (var year in years)
                    {
                        node.Children.Add(new GalleryTreeNode
                        {
                            Kind = GalleryTreeNodeKind.PlaceYear,
                            PlaceId = placeId,
                            Year = year.Year,
                            Title = year.Title,
                            Count = year.Count,
                            Depth = node.Depth + 1,
                            CanExpand = false
                        });
                    }

                    break;
                }
            }

            node.ChildrenLoaded = true;
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
        var query = BuildQuery(node);
        GalleryDiagnostics.WriteStep(
            $"Gallery hierarchy query year={query.Year}, country={query.Country}, city={query.City}, place={query.PlaceId}, search={query.SearchText}");

        IReadOnlyList<GalleryMediaDto> mediaItems;
        try
        {
            mediaItems = await _hierarchyService.QueryAsync(query);
        }
        catch (Exception ex)
        {
            GalleryDiagnostics.WriteException("GalleryHierarchyService.QueryAsync", ex);
            Items = [];
            StatusMessage = "사진을 불러오는 중 오류가 발생했습니다.";
            throw;
        }

        var galleryItems = mediaItems.Select(media => new GalleryItem(media)).ToList();
        Items = new ObservableCollection<GalleryItem>(galleryItems);
        StatusMessage = galleryItems.Count == 0
            ? "표시할 사진이 없습니다."
            : $"{node.Title} · {galleryItems.Count}장";

        _ = LoadThumbnailsAsync(galleryItems);
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
                parts.Add(GalleryHierarchyService.UnclassifiedTitle);
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
                if (item.HasThumbnail)
                {
                    continue;
                }

                item.IsThumbnailLoading = true;
                try
                {
                    if (string.IsNullOrWhiteSpace(item.AbsoluteLibraryPath))
                    {
                        item.HasThumbnail = false;
                        continue;
                    }

                    var path = await _thumbnailService.GetOrCreateThumbnailAsync(
                        item.MediaId,
                        item.AbsoluteLibraryPath,
                        token);
                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
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
                    _logger.LogWarning(ex, "Gallery thumbnail failed. MediaId={MediaId}", item.MediaId);
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
