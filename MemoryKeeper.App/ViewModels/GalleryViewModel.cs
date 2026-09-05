using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemoryKeeper.App.Diagnostics;
using MemoryKeeper.App.Models;
using MemoryKeeper.App.Services;
using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Infrastructure.Services.Api;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using GalleryPhotoDto = MemoryKeeper.Application.DTOs.Gallery.PhotoDto;

namespace MemoryKeeper.App.ViewModels;

public partial class GalleryViewModel : ObservableObject
{
    public const int DefaultPageSize = 50;

    private readonly GalleryHierarchyService _hierarchyService;
    private readonly IFastGalleryApiRepository _fastGallery;
    private readonly BaseApiClient _apiClient;
    private readonly IPhotoNavigationState _photoNavigationState;
    private readonly IGalleryFocusState _galleryFocusState;
    private readonly ILogger<GalleryViewModel> _logger;
    private readonly DispatcherQueue? _dispatcherQueue;
    private CancellationTokenSource? _thumbnailCts;
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _pageCts;

    private int _currentPage = 1;
    private int _totalCount;
    private GalleryTreeNode? _pagingNode;
    private IReadOnlyList<GalleryPhotoDto> _matchedPhotos = [];
    private string? _nextCursor;
    private bool _hasMore;
    private int _queryGeneration;
    private int _thumbnailBatchSequence;
    private int _fastMediaDiagnosticsRemaining;
    private FastGalleryHierarchyDto? _fastHierarchy;
    private GalleryPlaceScope _placeScope;

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

    [ObservableProperty]
    private bool isDetailPanelOpen;

    /// <summary>0 = 연도 보기, 1 = 장소 보기.</summary>
    [ObservableProperty]
    private int browseModeIndex;

    public bool IsYearBrowseMode => BrowseModeIndex == 0;

    public bool IsPlaceBrowseMode => BrowseModeIndex == 1;

    /// <summary>Infinite-scroll prep: more pages available from Backend.</summary>
    public bool CanLoadMore => _hasMore;

    public int CurrentPage => _currentPage;

    public int PageSize => DefaultPageSize;

    public int TotalCount => _totalCount;

    public bool HasPendingFocusRestore => _galleryFocusState.HasPendingRestore;

    public event EventHandler? BackRequested;

    public event EventHandler<Guid>? ScrollToMediaRequested;

    public event EventHandler<double>? ScrollOffsetRequested;

    public GalleryViewModel(
        GalleryHierarchyService hierarchyService,
        IFastGalleryApiRepository fastGallery,
        BaseApiClient apiClient,
        IPhotoNavigationState photoNavigationState,
        IGalleryFocusState galleryFocusState,
        ILogger<GalleryViewModel> logger)
    {
        GalleryDiagnostics.WriteStep("GalleryViewModel Created");
        _hierarchyService = hierarchyService;
        _fastGallery = fastGallery;
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
        if (restore is not null)
        {
            BrowseModeIndex = restore.BrowseModeIndex;
            _placeScope = restore.PlaceScope;
            if (!string.IsNullOrWhiteSpace(restore.SearchText))
            {
                SearchText = restore.SearchText;
            }
        }

        await RunBusyAsync(async () =>
        {
            await RebuildTreeRootsAsync();
            if (restore?.RequestedPlaceLevel is GalleryPlaceNavigationLevel requestedLevel)
            {
                await ApplyPlaceNavigationAsync(restore.PlaceScope, requestedLevel);
            }
            else if (!string.IsNullOrWhiteSpace(restore?.CountryFilter))
            {
                await SelectCountryFilterAsync(restore.CountryFilter);
            }
            else if (restore is not null)
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
            BrowseModeIndex = BrowseModeIndex,
            PlaceScope = _placeScope,
        });
    }

    private async Task SelectCountryFilterAsync(string country)
    {
        BrowseModeIndex = 1;
        _placeScope = GalleryPlaceScope.All;
        var normalized = PlaceNormalizer.NormalizeCountry(country);
        var node = TreeRoots.FirstOrDefault(item =>
            item.Kind == GalleryTreeNodeKind.Country
            && string.Equals(item.Country, normalized, StringComparison.OrdinalIgnoreCase));
        if (node is null)
        {
            node = new GalleryTreeNode
            {
                Kind = GalleryTreeNodeKind.Country,
                Country = normalized,
                Title = normalized,
                Count = 0,
            };
        }
        else
        {
            node.IsExpanded = node.CanExpand;
            RebuildVisibleTree();
        }

        await SelectNodeAsync(node);
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

        _placeScope = GalleryPlaceScope.All;
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

        _placeScope = GalleryPlaceScope.All;
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
                await SelectTreeNodeAsync(first);
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

        if (IsPlaceBrowseMode && node.Kind == GalleryTreeNodeKind.Country && node.CanExpand)
        {
            node.IsExpanded = true;
            RebuildVisibleTree();
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

    public void PreparePhotoDetail(GalleryItem item)
    {
        _photoNavigationState.SetPlaylist(Items.Select(galleryItem => galleryItem.MediaId).ToList());
        _photoNavigationState.FocusMediaId = item.MediaId;
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
                    await SelectTreeNodeAsync(first);
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
        FastGallerySummaryDto summary;
        try
        {
            summary = await _fastGallery.GetSummaryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fast Gallery summary failed; Gallery photos remain available.");
            summary = new FastGallerySummaryDto();
        }
        try
        {
            _fastHierarchy = await _fastGallery.GetHierarchyAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fast Gallery hierarchy failed; Gallery photos remain available.");
            _fastHierarchy = new FastGalleryHierarchyDto();
        }

        var roots = new List<GalleryTreeNode>
        {
            new()
            {
                Kind = GalleryTreeNodeKind.All,
                Title = "전체",
                Count = summary.TotalPhotos,
                Depth = 0,
                CanExpand = false,
                IsSelected = true
            }
        };

        foreach (var year in summary.ByYear
                     .Select(item => new { Year = int.TryParse(item.Name, out var value) ? value : 0, item.Count })
                     .Where(item => item.Year > 0)
                     .OrderByDescending(item => item.Year))
        {
            roots.Add(new GalleryTreeNode
            {
                Kind = GalleryTreeNodeKind.Year,
                Year = year.Year,
                Title = year.Year.ToString(),
                Count = year.Count,
                Depth = 0,
                CanExpand = true,
            });
        }

        roots.Add(new GalleryTreeNode { Kind = GalleryTreeNodeKind.Separator, Title = "—", Depth = 0 });
        roots.Add(new GalleryTreeNode
        {
            Kind = GalleryTreeNodeKind.Favorites,
            Title = "즐겨찾기",
            Count = summary.FavoriteCount,
            Depth = 0,
        });
        roots.Add(new GalleryTreeNode
        {
            Kind = GalleryTreeNodeKind.Recent,
            Title = "최근 등록",
            Count = 0,
            Depth = 0,
        });
        roots.Add(new GalleryTreeNode
        {
            Kind = GalleryTreeNodeKind.Pending,
            Title = "미완성 추억",
            Count = 0,
            Depth = 0,
        });

        TreeRoots = new ObservableCollection<GalleryTreeNode>(roots);
        RebuildVisibleTree();
    }

    private async Task RebuildPlaceTreeRootsAsync()
    {
        _fastHierarchy ??= await _fastGallery.GetHierarchyAsync();
        var term = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim();
        var roots = GalleryPlaceHierarchyProjection.Build(_fastHierarchy)
            .Where(country => _placeScope switch
            {
                GalleryPlaceScope.Domestic => country.IsDomestic,
                GalleryPlaceScope.International => !country.IsDomestic && !country.IsUnclassified,
                _ => true,
            })
            .Select(country =>
            {
                var countryMatches = term is null
                                     || country.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase);
                var places = country.Places
                    .Where(place => countryMatches
                                    || place.DisplayName.Contains(term!, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (!countryMatches && places.Count == 0)
                {
                    return null;
                }

                var root = new GalleryTreeNode
                {
                    Kind = GalleryTreeNodeKind.Country,
                    Country = country.CountryFilter,
                    Title = country.DisplayName,
                    Count = countryMatches ? country.PhotoCount : places.Sum(place => place.PhotoCount),
                    Depth = 0,
                    CanExpand = places.Count > 0,
                    ChildrenLoaded = true,
                };
                foreach (var place in places)
                {
                    root.Children.Add(new GalleryTreeNode
                    {
                        Kind = GalleryTreeNodeKind.PlaceBrowse,
                        Country = country.CountryFilter,
                        PlaceId = place.PlaceId,
                        Title = place.DisplayName,
                        Count = place.PhotoCount,
                        Depth = 1,
                        CanExpand = false,
                        ChildrenLoaded = true,
                    });
                }

                return root;
            })
            .Where(node => node is not null)
            .Cast<GalleryTreeNode>()
            .ToList();

        TreeRoots = new ObservableCollection<GalleryTreeNode>(roots);
        RebuildVisibleTree();
    }

    private async Task ApplyPlaceNavigationAsync(
        GalleryPlaceScope scope,
        GalleryPlaceNavigationLevel level)
    {
        BrowseModeIndex = 1;
        _placeScope = scope;

        if (scope == GalleryPlaceScope.Domestic)
        {
            var domestic = TreeRoots.FirstOrDefault(node =>
                node.Kind == GalleryTreeNodeKind.Country
                && string.Equals(
                    PlaceNormalizer.NormalizeCountry(node.Country),
                    "대한민국",
                    StringComparison.OrdinalIgnoreCase));
            if (domestic is not null)
            {
                domestic.IsExpanded = domestic.CanExpand;
                RebuildVisibleTree();
                await SelectNodeAsync(domestic);
                return;
            }

            ClearPlaceScopeSelection("사진첩 > 장소 > 대한민국", "표시할 국내 사진이 없습니다.");
            return;
        }

        if (scope == GalleryPlaceScope.International)
        {
            if (level is GalleryPlaceNavigationLevel.Places or GalleryPlaceNavigationLevel.Photos)
            {
                foreach (var country in TreeRoots)
                {
                    country.IsExpanded = country.CanExpand;
                }

                RebuildVisibleTree();
            }

            var message = TreeRoots.Count == 0
                ? "표시할 해외 사진이 없습니다."
                : level == GalleryPlaceNavigationLevel.Countries
                    ? "해외 국가를 선택해 사진을 탐색하세요."
                    : "해외 국가 또는 장소를 선택해 사진을 탐색하세요.";
            ClearPlaceScopeSelection("사진첩 > 장소 > 해외", message);
        }
    }

    private void ClearPlaceScopeSelection(string breadcrumb, string status, bool clearSelection = true)
    {
        if (clearSelection)
        {
            foreach (var node in VisibleTreeNodes)
            {
                node.IsSelected = false;
            }

            SelectedNode = null;
        }

        Items = [];
        _nextCursor = null;
        _hasMore = false;
        _totalCount = 0;
        OnPropertyChanged(nameof(CanLoadMore));
        OnPropertyChanged(nameof(TotalCount));
        BreadcrumbText = breadcrumb;
        StatusMessage = status;
        _photoNavigationState.SetPlaylist([]);
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
                    _fastHierarchy ??= await _fastGallery.GetHierarchyAsync();
                    var countries = FindYearNode(year)?.ChildNodes ?? [];
                    foreach (var country in countries)
                    {
                        node.Children.Add(new GalleryTreeNode
                        {
                            Kind = string.IsNullOrWhiteSpace(country.Country)
                                ? GalleryTreeNodeKind.Unclassified
                                : GalleryTreeNodeKind.Country,
                            Year = year,
                            Country = country.Country,
                            Title = country.Country ?? LibraryConstants.UnclassifiedTitle,
                            Count = country.Count,
                            Depth = node.Depth + 1,
                            CanExpand = country.ChildNodes.Count > 0,
                        });
                    }

                    break;
                }
                case GalleryTreeNodeKind.Country
                    when node.Year is int year && !string.IsNullOrWhiteSpace(node.Country):
                {
                    var cities = FindYearNode(year)?.ChildNodes
                        .FirstOrDefault(item => string.Equals(item.Country, node.Country, StringComparison.OrdinalIgnoreCase))?.ChildNodes ?? [];
                    foreach (var city in cities)
                    {
                        node.Children.Add(new GalleryTreeNode
                        {
                            Kind = GalleryTreeNodeKind.City,
                            Year = year,
                            Country = node.Country,
                            City = city.Region,
                            Title = city.Region ?? LibraryConstants.UnclassifiedTitle,
                            Count = city.Count,
                            Depth = node.Depth + 1,
                            CanExpand = true,
                        });
                    }

                    break;
                }
                case GalleryTreeNodeKind.City
                    when node.Year is int year
                         && !string.IsNullOrWhiteSpace(node.Country)
                         && !string.IsNullOrWhiteSpace(node.City):
                {
                    var places = FindYearNode(year)?.ChildNodes
                        .FirstOrDefault(item => string.Equals(item.Country, node.Country, StringComparison.OrdinalIgnoreCase))?.ChildNodes
                        .FirstOrDefault(item => string.Equals(item.Region, node.City, StringComparison.OrdinalIgnoreCase))?.ChildNodes ?? [];
                    foreach (var place in places)
                    {
                        node.Children.Add(new GalleryTreeNode
                        {
                            Kind = GalleryTreeNodeKind.Place,
                            Year = year,
                            Country = node.Country,
                            City = node.City,
                            PlaceId = place.MemorykeeperPlaceId ?? place.PlaceId,
                            Title = place.DisplayName ?? LibraryConstants.UnclassifiedTitle,
                            Count = place.Count,
                            Depth = node.Depth + 1,
                            CanExpand = false,
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

    private FastGalleryHierarchyNodeDto? FindYearNode(int year) =>
        _fastHierarchy?.Roots.FirstOrDefault(node => node.Year == year);

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
        _pageCts?.Cancel();
        _pageCts?.Dispose();
        _pageCts = new CancellationTokenSource();
        var token = _pageCts.Token;
        var generation = ++_queryGeneration;
        _pagingNode = node;
        _currentPage = 1;
        var query = BuildQuery(node);
        GalleryDiagnostics.WriteStep(
            $"Gallery hierarchy query year={query.Year}, country={query.Country}, city={query.City}, place={query.PlaceId}, search={query.SearchText}");

        try
        {
            if (IsPlaceBrowseMode
                && node.Kind == GalleryTreeNodeKind.Country
                && string.IsNullOrWhiteSpace(node.Country))
            {
                ClearPlaceScopeSelection(
                    BuildBreadcrumb(node),
                    "미분류 사진은 아래 장소를 선택하거나 연도 보기에서 탐색하세요.",
                    clearSelection: false);
                return;
            }

            // Keyword and legacy-only nodes retain their curated common-Gallery behaviour, but are lazy:
            // ordinary Gallery entry never constructs the old all-photo snapshot.
            if (!string.IsNullOrWhiteSpace(query.SearchText)
                || node.Kind is GalleryTreeNodeKind.Recent or GalleryTreeNodeKind.Pending)
            {
                await QueryLegacyAsync(query, node, token, generation);
                return;
            }

            // Keep Fast-media diagnostics useful without producing one log per gallery item.
            Interlocked.Exchange(ref _fastMediaDiagnosticsRemaining, 3);
            var page = await _fastGallery.GetPhotosAsync(ToFastQuery(node), token);
            if (generation != _queryGeneration || token.IsCancellationRequested)
            {
                return;
            }

            var galleryItems = page.Items.Select(ToGalleryItem)
                .Where(item => item.MediaId != Guid.Empty)
                .DistinctBy(item => item.BackendFileId, StringComparer.OrdinalIgnoreCase)
                .ToList();
            Items = new ObservableCollection<GalleryItem>(galleryItems);
            _nextCursor = page.NextCursor;
            _hasMore = page.HasMore && !string.IsNullOrWhiteSpace(page.NextCursor);
            _totalCount = Items.Count;
            OnPropertyChanged(nameof(CanLoadMore));
            OnPropertyChanged(nameof(TotalCount));
            StatusMessage = galleryItems.Count == 0 ? "표시할 사진이 없습니다." : $"{node.Title} · {galleryItems.Count}장";
            _photoNavigationState.SetPlaylist(galleryItems.Select(item => item.MediaId).ToList());
            _ = LoadThumbnailsAsync(galleryItems);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // A superseding filter/navigation request owns the visible state.
        }
        catch (Exception ex)
        {
            GalleryDiagnostics.WriteException("FastGallery.Query", ex);
            if (generation == _queryGeneration)
            {
                Items = [];
                _nextCursor = null;
                _hasMore = false;
                _totalCount = 0;
                OnPropertyChanged(nameof(CanLoadMore));
                StatusMessage = "사진을 불러오는 중 오류가 발생했습니다. 다시 시도해 주세요.";
            }
            throw;
        }
    }

    private async Task QueryLegacyAsync(GalleryHierarchyQuery query, GalleryTreeNode node, CancellationToken token, int generation)
    {
        try
        {
            _matchedPhotos = await _hierarchyService.QueryAsync(query);
            token.ThrowIfCancellationRequested();
            if (generation != _queryGeneration) return;
            _totalCount = _matchedPhotos.Count;
            _nextCursor = null;
            _hasMore = false;
            OnPropertyChanged(nameof(CanLoadMore));
            var firstPage = _matchedPhotos.Take(DefaultPageSize).ToList();
            var first = firstPage.FirstOrDefault();
            var firstMapped = first is null
                ? null
                : GalleryBackendMapper.ToGalleryMedia(first, _apiClient.ApiBaseUrl);

            _logger.LogInformation(
                "Gallery load. total_count={TotalCount}, items.Count={ItemsCount}, first.file_id={FileId}, thumbnail_url_raw={ThumbRaw}, thumbnail_url_abs={ThumbAbs}, apiBaseUrl={ApiBaseUrl}",
                _totalCount,
                firstPage.Count,
                first?.FileId,
                ApiErrorClassifier.SafePath(first?.ThumbnailUrl),
                ApiErrorClassifier.SafePath(firstMapped?.ThumbnailUrl),
                _apiClient.ApiBaseUrl);

            var galleryItems = firstPage
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
                firstPage.Count);
            GalleryDiagnostics.WriteStep(
                $"Gallery items mapped Count={Items.Count}, total_count={_totalCount}, firstFileId={first?.FileId}");

            _photoNavigationState.SetPlaylist(galleryItems.Select(i => i.MediaId).ToList());
            _ = LoadThumbnailsAsync(galleryItems);
        }
        catch (Exception ex)
        {
            GalleryDiagnostics.WriteException("GalleryHierarchyService.QueryAsync", ex);
            Items = [];
            _matchedPhotos = [];
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
        if (!CanLoadMore || _pagingNode is null || IsBusy || string.IsNullOrWhiteSpace(_nextCursor))
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var generation = _queryGeneration;
            var cursor = _nextCursor!;
            var page = await _fastGallery.GetPhotosAsync(ToFastQuery(_pagingNode, cursor));
            if (generation != _queryGeneration || !string.Equals(cursor, _nextCursor, StringComparison.Ordinal)) return;

            var seen = Items.Select(item => item.BackendFileId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var appended = page.Items.Select(ToGalleryItem)
                .Where(item => item.MediaId != Guid.Empty && seen.Add(item.BackendFileId))
                .ToList();
            foreach (var item in appended) Items.Add(item);
            _currentPage++;
            _nextCursor = page.NextCursor;
            _hasMore = page.HasMore && !string.IsNullOrWhiteSpace(page.NextCursor);
            _totalCount = Items.Count;
            OnPropertyChanged(nameof(CanLoadMore));
            OnPropertyChanged(nameof(TotalCount));
            StatusMessage = $"{_pagingNode.Title} · {Items.Count}장";
            _photoNavigationState.SetPlaylist(Items.Select(item => item.MediaId).ToList());
            _logger.LogInformation(
                "MK_GALLERY_THUMB_BATCH event=load_more_appended appended={AppendedCount} total={TotalCount} starts_thumbnail_batch=true",
                appended.Count,
                Items.Count);
            _ = LoadThumbnailsAsync(appended);
        }, "LoadMoreAsync");
    }

    private FastGalleryPhotoQuery ToFastQuery(GalleryTreeNode node, string? cursor = null) => new()
    {
        Limit = DefaultPageSize,
        Cursor = cursor,
        Year = node.Year,
        Country = node.Kind is GalleryTreeNodeKind.Country or GalleryTreeNodeKind.City or GalleryTreeNodeKind.Place ? node.Country : null,
        Region = node.Kind is GalleryTreeNodeKind.City or GalleryTreeNodeKind.Place ? node.City : null,
        PlaceId = node.Kind is GalleryTreeNodeKind.Place or GalleryTreeNodeKind.PlaceBrowse or GalleryTreeNodeKind.PlaceYear ? node.PlaceId : null,
        Favorite = node.Kind == GalleryTreeNodeKind.Favorites ? true : null,
    };

    private GalleryItem ToGalleryItem(FastGalleryPhotoDto photo)
    {
        var thumbnail = BackendMediaUrlResolver.ResolveThumbnailUrl(
            _apiClient.ApiBaseUrl,
            photo.FileId,
            photo.ThumbnailUrl);
        var preview = BackendMediaUrlResolver.ToAbsoluteUrl(_apiClient.ApiBaseUrl, photo.PreviewUrl);
        if (_fastMediaDiagnosticsRemaining > 0 && Interlocked.Decrement(ref _fastMediaDiagnosticsRemaining) >= 0)
        {
            _logger.LogDebug(
                "Fast media mapped. Surface=Gallery FileId={FileId} Thumbnail={Thumbnail} Preview={Preview} Candidate={Candidate}",
                photo.FileId,
                BackendMediaUrlResolver.DescribeForDiagnostics(_apiClient.ApiBaseUrl, photo.ThumbnailUrl),
                BackendMediaUrlResolver.DescribeForDiagnostics(_apiClient.ApiBaseUrl, photo.PreviewUrl),
                BackendMediaUrlResolver.DescribeForDiagnostics(_apiClient.ApiBaseUrl, thumbnail ?? preview));
        }
        return new GalleryItem(new GalleryMediaDto
        {
            Id = GalleryBackendMapper.ParseFileId(photo.FileId),
            BackendFileId = photo.FileId,
            FileName = photo.Filename,
            AbsoluteLibraryPath = preview ?? thumbnail ?? string.Empty,
            CapturedAt = photo.EffectiveCaptureDatetime,
            PlaceId = photo.MemorykeeperPlaceId,
            MediaType = MediaTypeResolver.Resolve(photo.MimeType, photo.Extension, photo.Filename),
            IsFavorite = photo.Favorite,
            ThumbnailUrl = thumbnail,
            PreviewUrl = preview,
        });
    }

    private GalleryHierarchyQuery BuildQuery(GalleryTreeNode node) =>
        node.BuildQuery(IsPlaceBrowseMode ? null : SearchText);

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
                parts.Add(node.Year.HasValue ? node.Year.Value.ToString() : "장소");
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
                parts.Add(node.Country ?? LibraryConstants.UnclassifiedTitle);
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

        return TreeRoots
            .SelectMany(root => root.Children.Prepend(root))
            .FirstOrDefault(node => node.Kind == GalleryTreeNodeKind.PlaceBrowse && node.PlaceId == placeId)
            ?.Title;
    }

    private async Task LoadThumbnailsAsync(IReadOnlyList<GalleryItem> galleryItems)
    {
        var canceledExisting = _thumbnailCts is not null;
        CancelThumbnailLoading();
        _thumbnailCts = new CancellationTokenSource();
        var token = _thumbnailCts.Token;
        var batchId = Interlocked.Increment(ref _thumbnailBatchSequence);
        var processedCount = 0;

        _logger.LogInformation(
            "MK_GALLERY_THUMB_BATCH event=start batch={BatchId} targets={TargetCount} canceled_existing={CanceledExisting}",
            batchId,
            galleryItems.Count,
            canceledExisting);

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

                    var bitmap = await HttpImageLoader.LoadFirstAvailableAsync(
                        [remoteUrl, item.Media.PreviewUrl],
                        _logger,
                        context: $"GalleryThumbnail:{item.BackendFileId}",
                        cancellationToken: token);
                    token.ThrowIfCancellationRequested();

                    await EnqueueAsync(() =>
                    {
                        item.ThumbnailImage = bitmap;
                        item.HasThumbnail = bitmap is not null;
                        if (bitmap is null)
                        {
                            _logger.LogWarning(
                                "Gallery ThumbnailImageSource null. Url={Url}",
                                ApiErrorClassifier.SafePath(remoteUrl));
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
                        ApiErrorClassifier.SafePath(item.Media.ThumbnailUrl));
                    item.HasThumbnail = false;
                }
                finally
                {
                    item.IsThumbnailLoading = false;
                    processedCount++;
                }
            }

            _logger.LogInformation(
                "MK_GALLERY_THUMB_BATCH event=complete batch={BatchId} targets={TargetCount} processed={ProcessedCount} thumbnail_null={ThumbnailNullCount}",
                batchId,
                galleryItems.Count,
                processedCount,
                galleryItems.Count(item => item.ThumbnailImage is null));
        }
        catch (OperationCanceledException)
        {
            // expected on filter change
            _logger.LogInformation(
                "MK_GALLERY_THUMB_BATCH event=cancel batch={BatchId} targets={TargetCount} processed={ProcessedCount} thumbnail_null={ThumbnailNullCount}",
                batchId,
                galleryItems.Count,
                processedCount,
                galleryItems.Count(item => item.ThumbnailImage is null));
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
