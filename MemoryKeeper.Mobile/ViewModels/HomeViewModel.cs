using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Mobile.Http;
using MemoryKeeper.Mobile.Images;
using MemoryKeeper.Mobile.Models;

namespace MemoryKeeper.Mobile.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly FastGalleryPagingService _paging;
    private readonly IFastGalleryApiRepository _repository;
    private readonly IMobileThumbnailSourceFactory _thumbnailSourceFactory;
    private readonly object _requestLock = new();
    private readonly SemaphoreSlim _hierarchyGate = new(1, 1);
    private CancellationTokenSource? _requestCancellation;
    private FastGalleryHierarchyDto? _hierarchy;
    private readonly MobileGalleryNavigationHistory _contextHistory = new();
    private readonly List<MobileGalleryTreeNode> _placeTreeRoots = [];
    private readonly Dictionary<int, PlaceTreeState> _placeTreesByYear = [];
    private GalleryYearBrowseTree? _placeTreeSource;
    private GalleryBrowseScope _loadedScope = GalleryBrowseScope.DefaultFeed;
    private int? _placeTreeYear;
    private GalleryRetryOperation _retryOperation;
    private long _scopeGeneration;
    private bool _browseOptionsLoaded;
    private bool _initialized;
    private bool _suppressBrowseSelection;
    private double _tileSize = 120;

    [ObservableProperty]
    private bool isInitialLoading;

    [ObservableProperty]
    private bool isRefreshing;

    [ObservableProperty]
    private bool isLoadingMore;

    [ObservableProperty]
    private bool isBrowseOptionsLoading;

    [ObservableProperty]
    private bool hasMore = true;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string? browseOptionsErrorMessage;

    [ObservableProperty]
    private string? nextCursor;

    [ObservableProperty]
    private MobileGalleryBrowseOption? selectedBrowseOption;

    [ObservableProperty]
    private MobileGalleryBrowseOption? selectedSearchOption;

    [ObservableProperty]
    private MobileGalleryBrowseOption? selectedSiblingYearOption;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private MobileGalleryViewMode viewMode = MobileGalleryViewMode.Gallery;

    [ObservableProperty]
    private MobileGalleryContext currentContext = MobileGalleryContext.Recent;

    [ObservableProperty]
    private bool isSearchRunning;

    [ObservableProperty]
    private bool isSearchNoResultsVisible;

    public HomeViewModel(
        FastGalleryPagingService paging,
        IFastGalleryApiRepository repository,
        IMobileThumbnailSourceFactory thumbnailSourceFactory)
    {
        _paging = paging;
        _repository = repository;
        _thumbnailSourceFactory = thumbnailSourceFactory;

        var defaultFeed = new MobileGalleryBrowseOption("최근 사진", GalleryBrowseScope.DefaultFeed);
        BrowseOptions.Add(defaultFeed);
        selectedBrowseOption = defaultFeed;
        Items.CollectionChanged += OnItemsChanged;
        SearchResults.CollectionChanged += OnSearchResultsChanged;
    }

    public event EventHandler? AddPhotosRequested;

    public event Action<MobileGalleryTreeNode>? PlaceTreeFocusRequested;

    public ObservableCollection<MobileGalleryItem> Items { get; } = [];

    public ObservableCollection<MobileGalleryBrowseOption> BrowseOptions { get; } = [];

    public ObservableCollection<MobileGalleryTreeNode> VisiblePlaceTreeNodes { get; } = [];

    public ObservableCollection<MobileGalleryBrowseOption> SearchResults { get; } = [];

    public ObservableCollection<MobileGalleryBrowseOption> SiblingYearOptions { get; } = [];

    public GalleryBrowseScope CurrentScope => CurrentContext.Scope;

    public bool HasItems => Items.Count > 0;

    public bool IsGalleryMode => ViewMode == MobileGalleryViewMode.Gallery;

    public bool IsYearSelectionMode => ViewMode == MobileGalleryViewMode.YearSelection;

    public bool IsPlaceSelectionMode => ViewMode == MobileGalleryViewMode.PlaceSelection;

    public bool IsPlaceYearSelectionMode => ViewMode == MobileGalleryViewMode.PlaceYearSelection;

    public bool IsSearchMode => ViewMode == MobileGalleryViewMode.Search;

    public bool IsGalleryVisible => IsGalleryMode && HasItems;

    public bool IsEmptyStateVisible => !IsInitialLoading
                                       && !HasItems
                                       && IsGalleryMode
                                       && string.IsNullOrWhiteSpace(ErrorMessage);

    public bool IsErrorStateVisible => !IsInitialLoading
                                       && !HasItems
                                       && IsGalleryMode
                                       && !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool IsInlineErrorVisible => IsGalleryMode
                                        && HasItems
                                        && !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool IsBrowseOptionsErrorVisible => !string.IsNullOrWhiteSpace(BrowseOptionsErrorMessage);

    public bool IsSearchResultsVisible => SearchResults.Count > 0;

    public bool IsSearchErrorVisible => IsSearchMode
                                        && !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool CanNavigateBack => ViewMode != MobileGalleryViewMode.Gallery
                                   || _contextHistory.Count > 0
                                   || CurrentContext.Kind != MobileGalleryContextKind.Recent;

    public bool CanOpenHierarchyNavigator => CurrentContext.HasHierarchyYear
                                             && _placeTreeRoots.Count > 0;

    public bool CanOpenSiblingYearSelection => CurrentContext.IsPlaceLeaf
                                                && SiblingYearOptions.Count > 1;

    public bool IsGalleryYearVisible => CurrentContext.Year.HasValue;

    public bool IsStaticGalleryYearVisible => IsGalleryYearVisible && !CanOpenSiblingYearSelection;

    public bool IsRecentContext => CurrentContext.Kind == MobileGalleryContextKind.Recent;

    public string PlaceSelectionTitle => CurrentContext.Year is int year
        ? $"{year}년"
        : "장소";

    public async Task InitializeAsync()
    {
        _ = LoadBrowseOptionsSafelyAsync();

        if (_initialized && Items.Count > 0)
        {
            return;
        }

        _initialized = true;
        var generation = Volatile.Read(ref _scopeGeneration);
        await LoadFirstPageAsync(
            isRefresh: false,
            CurrentContext.Scope,
            generation,
            clearItems: false);
    }

    [RelayCommand]
    public Task RefreshAsync()
    {
        var generation = Volatile.Read(ref _scopeGeneration);
        return LoadFirstPageAsync(
            isRefresh: HasItems,
            CurrentContext.Scope,
            generation,
            clearItems: false);
    }

    [RelayCommand]
    public async Task LoadMoreAsync()
    {
        if (IsInitialLoading || IsRefreshing || IsLoadingMore || !HasMore)
        {
            return;
        }

        var generation = Volatile.Read(ref _scopeGeneration);
        IsLoadingMore = true;
        ErrorMessage = null;
        _retryOperation = GalleryRetryOperation.None;
        var request = BeginRequest(cancelCurrent: false, generation);
        try
        {
            var update = await _paging.LoadNextPageAsync(request.Cancellation.Token);
            if (!update.Applied || !IsCurrentRequest(request))
            {
                return;
            }

            foreach (var item in update.AddedItems)
            {
                Items.Add(ToMobileItem(item));
            }

            ApplyContinuation(update);
        }
        catch (OperationCanceledException) when (request.Cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (IsCurrentRequest(request))
            {
                _retryOperation = GalleryRetryOperation.LoadMore;
                ErrorMessage = ToUserMessage(ex);
            }
        }
        finally
        {
            if (EndRequest(request))
            {
                IsLoadingMore = false;
            }
        }
    }

    [RelayCommand]
    public Task RetryAsync() => _retryOperation == GalleryRetryOperation.LoadMore
        ? LoadMoreAsync()
        : RefreshAsync();

    [RelayCommand]
    public Task RetryBrowseOptionsAsync() => LoadBrowseOptionsSafelyAsync();

    [RelayCommand]
    private void AddPhotos() => AddPhotosRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenYearSelection()
    {
        ClearTransientSelection();
        ViewMode = MobileGalleryViewMode.YearSelection;
    }

    [RelayCommand]
    private void OpenHierarchyNavigator()
    {
        PreparePlaceTree(CurrentContext.Scope, focusCurrentPath: true);
        if (!CanOpenHierarchyNavigator)
        {
            return;
        }

        ViewMode = MobileGalleryViewMode.PlaceSelection;
        var currentNode = VisiblePlaceTreeNodes.FirstOrDefault(node => node.IsCurrent);
        if (currentNode is not null)
        {
            PlaceTreeFocusRequested?.Invoke(currentNode);
        }
    }

    [RelayCommand]
    private void OpenPlaceYearSelection()
    {
        PrepareSiblingYearOptions(CurrentContext);
        if (!CanOpenSiblingYearSelection)
        {
            return;
        }

        ViewMode = MobileGalleryViewMode.PlaceYearSelection;
    }

    [RelayCommand]
    private void OpenSearch()
    {
        ClearSearchPresentation();
        ViewMode = MobileGalleryViewMode.Search;
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        var query = (SearchText ?? string.Empty).Trim();
        if (query.Length == 0)
        {
            await CancelSearchAsync();
            return;
        }

        IsSearchRunning = true;
        IsSearchNoResultsVisible = false;
        ErrorMessage = null;
        _suppressBrowseSelection = true;
        try
        {
            SelectedSearchOption = null;
            SearchResults.Clear();
        }
        finally
        {
            _suppressBrowseSelection = false;
        }
        try
        {
            var hierarchy = await GetHierarchyAsync();
            foreach (var result in GalleryBrowseCatalog.Search(hierarchy, query))
            {
                SearchResults.Add(new MobileGalleryBrowseOption(
                    result.DisplayName,
                    result.Scope,
                    result.PhotoCount,
                    result.Context));
            }

            if (SearchResults.Count == 0)
            {
                IsSearchNoResultsVisible = true;
                return;
            }

        }
        catch (Exception ex)
        {
            SearchResults.Clear();
            IsSearchNoResultsVisible = false;
            ErrorMessage = ToUserMessage(ex);
        }
        finally
        {
            IsSearchRunning = false;
            NotifySearchStateChanged();
        }
    }

    [RelayCommand]
    private Task CancelSearchAsync()
    {
        ClearSearchPresentation();
        ViewMode = MobileGalleryViewMode.Gallery;
        return Task.CompletedTask;
    }

    [RelayCommand]
    public async Task NavigateBackAsync()
    {
        if (ViewMode != MobileGalleryViewMode.Gallery)
        {
            var needsReload = !Equals(_loadedScope, CurrentContext.Scope);
            ClearTransientSelection();
            ViewMode = MobileGalleryViewMode.Gallery;
            if (needsReload)
            {
                await ChangeScopeSafelyAsync(CurrentContext.Scope);
            }

            return;
        }

        if (_contextHistory.TryPop(out var previous))
        {
            if (previous.ReturnMode == MobileGalleryViewMode.PlaceSelection)
            {
                CurrentContext = previous.Context;
                ClearTransientSelection();
                PreparePlaceTree(previous.Context.Scope);
                ViewMode = MobileGalleryViewMode.PlaceSelection;
                NotifyContextStateChanged();
            }
            else
            {
                await ApplyContextSafelyAsync(previous.Context, pushCurrentContext: false);
            }

            return;
        }

        if (CurrentContext.Kind != MobileGalleryContextKind.Recent)
        {
            await ApplyContextSafelyAsync(MobileGalleryContext.Recent, pushCurrentContext: false);
        }
    }

    public bool HandleBackRequested()
    {
        if (!CanNavigateBack)
        {
            return false;
        }

        _ = NavigateBackAsync();
        return true;
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
        _retryOperation = GalleryRetryOperation.FirstPage;
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

    partial void OnSelectedBrowseOptionChanged(MobileGalleryBrowseOption? value)
    {
        if (!_suppressBrowseSelection && value is not null)
        {
            _ = ApplyOptionSafelyAsync(value, pushCurrentContext: true);
        }
    }

    partial void OnSelectedSearchOptionChanged(MobileGalleryBrowseOption? value)
    {
        if (!_suppressBrowseSelection && value is not null)
        {
            _ = ApplyOptionSafelyAsync(
                value,
                pushCurrentContext: true,
                returnMode: MobileGalleryViewMode.Gallery);
        }
    }

    partial void OnSelectedSiblingYearOptionChanged(MobileGalleryBrowseOption? value)
    {
        if (_suppressBrowseSelection || value is null)
        {
            return;
        }

        if (Equals(value.Scope, CurrentContext.Scope))
        {
            SelectedSiblingYearOption = null;
            ViewMode = MobileGalleryViewMode.Gallery;
            return;
        }

        _ = ApplyOptionSafelyAsync(
            value,
            pushCurrentContext: true,
            returnMode: MobileGalleryViewMode.Gallery);
    }

    partial void OnSearchTextChanged(string value) => NotifySearchStateChanged();

    partial void OnIsSearchNoResultsVisibleChanged(bool value) => NotifyViewStateChanged();

    partial void OnViewModeChanged(MobileGalleryViewMode value) => NotifyContextStateChanged();

    partial void OnCurrentContextChanged(MobileGalleryContext value)
    {
        PrepareSiblingYearOptions(value);
        OnPropertyChanged(nameof(CurrentScope));
        NotifyContextStateChanged();
    }

    private async Task ApplyOptionSafelyAsync(
        MobileGalleryBrowseOption option,
        bool pushCurrentContext,
        MobileGalleryViewMode returnMode = MobileGalleryViewMode.Gallery)
    {
        try
        {
            await ApplyOptionAsync(option, pushCurrentContext, returnMode);
        }
        catch (Exception exception)
        {
            HandleUnexpectedLoadFailure(exception);
        }
    }

    private Task ApplyOptionAsync(
        MobileGalleryBrowseOption option,
        bool pushCurrentContext,
        MobileGalleryViewMode returnMode) =>
        ApplyContextSafelyAsync(
            MobileGalleryContext.FromOption(option),
            pushCurrentContext,
            returnMode);

    private async Task ApplyContextSafelyAsync(
        MobileGalleryContext context,
        bool pushCurrentContext,
        MobileGalleryViewMode returnMode = MobileGalleryViewMode.Gallery)
    {
        try
        {
            var contextChanged = !Equals(CurrentContext.Scope, context.Scope);
            var needsReload = !Equals(_loadedScope, context.Scope);
            if (pushCurrentContext && contextChanged)
            {
                _contextHistory.Remember(CurrentContext, returnMode, context);
            }

            CurrentContext = context;
            ViewMode = MobileGalleryViewMode.Gallery;
            ClearTransientSelection();
            PreparePlaceTree(context.Scope);
            NotifyContextStateChanged();
            if (needsReload)
            {
                await ChangeScopeSafelyAsync(context.Scope);
            }
        }
        catch (Exception exception)
        {
            HandleUnexpectedLoadFailure(exception);
        }
    }

    private void PreparePlaceTree(
        GalleryBrowseScope scope,
        bool focusCurrentPath = false)
    {
        var year = scope switch
        {
            GalleryBrowseScope.YearScope yearScope => yearScope.Year,
            GalleryBrowseScope.HierarchyScope { Year: int hierarchyYear } => hierarchyYear,
            GalleryBrowseScope.CanonicalRegionScope regionScope => regionScope.Year,
            _ => (int?)null,
        };
        if (year is null || _hierarchy is null)
        {
            return;
        }

        if (_placeTreeYear != year.Value)
        {
            _placeTreeRoots.Clear();
            if (!_placeTreesByYear.TryGetValue(year.Value, out var treeState))
            {
                var source = GalleryBrowseTree.ForYear(_hierarchy, year.Value);
                if (source is not null)
                {
                    treeState = new PlaceTreeState(
                        source,
                        source.Roots.Select(root => new MobileGalleryTreeNode(root)).ToArray());
                    _placeTreesByYear.Add(year.Value, treeState);
                }
            }

            _placeTreeSource = treeState?.Source;
            if (treeState is not null)
            {
                _placeTreeRoots.AddRange(treeState.Roots);
            }

            _placeTreeYear = year.Value;
        }

        if (focusCurrentPath)
        {
            foreach (var root in _placeTreeRoots)
            {
                CollapsePlaceTree(root);
            }
        }

        ApplyCurrentPlaceTreePath(scope);
        RebuildVisiblePlaceTree();
        OnPropertyChanged(nameof(CanOpenHierarchyNavigator));
    }

    private void PrepareSiblingYearOptions(MobileGalleryContext context)
    {
        var wasSuppressed = _suppressBrowseSelection;
        _suppressBrowseSelection = true;
        try
        {
            SelectedSiblingYearOption = null;
            SiblingYearOptions.Clear();
            if (_hierarchy is null || !context.IsPlaceLeaf)
            {
                return;
            }

            foreach (var sibling in GalleryPlaceYearCatalog.FindSiblings(_hierarchy, context.Scope))
            {
                SiblingYearOptions.Add(new MobileGalleryBrowseOption(
                    $"{sibling.Year}년",
                    sibling.Scope,
                    sibling.PhotoCount,
                    sibling.Context,
                    IsCurrent: sibling.IsCurrent,
                    GalleryTitle: sibling.DisplayName));
            }
        }
        finally
        {
            _suppressBrowseSelection = wasSuppressed;
            OnPropertyChanged(nameof(CanOpenSiblingYearSelection));
            OnPropertyChanged(nameof(IsStaticGalleryYearVisible));
        }
    }

    private void ApplyCurrentPlaceTreePath(GalleryBrowseScope scope)
    {
        var focus = _placeTreeSource is null
            ? new GalleryBrowseTreeFocus(new HashSet<string>(StringComparer.Ordinal), null)
            : GalleryBrowseTree.CreateFocus(_placeTreeSource, scope);
        foreach (var root in _placeTreeRoots)
        {
            ApplyCurrentPlaceTreePath(root, focus.ExpandedNodeKeys, focus.CurrentNodeKey);
        }
    }

    private static void ApplyCurrentPlaceTreePath(
        MobileGalleryTreeNode node,
        IReadOnlySet<string> pathKeys,
        string? currentKey)
    {
        node.IsCurrent = string.Equals(node.Key, currentKey, StringComparison.Ordinal);
        if (pathKeys.Contains(node.Key) && node.HasChildren)
        {
            node.IsExpanded = true;
        }

        foreach (var child in node.Children)
        {
            ApplyCurrentPlaceTreePath(child, pathKeys, currentKey);
        }
    }

    private static void CollapsePlaceTree(MobileGalleryTreeNode node)
    {
        node.IsExpanded = false;
        node.IsCurrent = false;
        foreach (var child in node.Children)
        {
            CollapsePlaceTree(child);
        }
    }

    private void ClearTransientSelection()
    {
        var wasSuppressed = _suppressBrowseSelection;
        _suppressBrowseSelection = true;
        try
        {
            SelectedBrowseOption = null;
            SelectedSearchOption = null;
            SelectedSiblingYearOption = null;
            ClearSearchPresentation();
        }
        finally
        {
            _suppressBrowseSelection = wasSuppressed;
        }
    }

    private void ClearSearchPresentation()
    {
        SearchText = string.Empty;
        SearchResults.Clear();
        SelectedSearchOption = null;
        IsSearchNoResultsVisible = false;
        NotifySearchStateChanged();
    }

    [RelayCommand]
    private void TogglePlaceTreeNode(MobileGalleryTreeNode? node)
    {
        if (node is null || !node.HasChildren)
        {
            return;
        }

        node.IsExpanded = !node.IsExpanded;
        RebuildVisiblePlaceTree();
    }

    [RelayCommand]
    private Task SelectPlaceTreeNodeAsync(MobileGalleryTreeNode? node)
    {
        if (node is null)
        {
            return Task.CompletedTask;
        }

        var option = new MobileGalleryBrowseOption(
            node.DisplayName,
            node.Scope,
            node.PhotoCount,
            node.Context);
        return ApplyOptionSafelyAsync(
            option,
            pushCurrentContext: true,
            returnMode: MobileGalleryViewMode.PlaceSelection);
    }

    private void RebuildVisiblePlaceTree()
    {
        VisiblePlaceTreeNodes.Clear();
        foreach (var root in _placeTreeRoots)
        {
            AppendVisibleNode(root);
        }
    }

    private void AppendVisibleNode(MobileGalleryTreeNode node)
    {
        VisiblePlaceTreeNodes.Add(node);
        if (!node.IsExpanded)
        {
            return;
        }

        foreach (var child in node.Children)
        {
            AppendVisibleNode(child);
        }
    }

    private async Task ChangeScopeSafelyAsync(GalleryBrowseScope scope)
    {
        _loadedScope = scope;
        var generation = Interlocked.Increment(ref _scopeGeneration);
        CancelPendingRequests();
        await LoadFirstPageAsync(
            isRefresh: false,
            scope,
            generation,
            clearItems: true);
    }

    private async Task LoadFirstPageAsync(
        bool isRefresh,
        GalleryBrowseScope scope,
        long generation,
        bool clearItems)
    {
        var showRefresh = isRefresh && HasItems;
        if (showRefresh)
        {
            IsRefreshing = true;
        }
        else
        {
            IsInitialLoading = true;
        }

        ErrorMessage = null;
        _retryOperation = GalleryRetryOperation.None;
        if (clearItems)
        {
            IsRefreshing = false;
            IsLoadingMore = false;
            HasMore = false;
            NextCursor = null;
            Items.Clear();
        }

        var request = BeginRequest(cancelCurrent: true, generation);
        try
        {
            var queries = GalleryBrowseScopeQueryMapper.ToQueries(
                scope,
                FastGalleryPagingService.PageSize);
            var update = await _paging.LoadFirstPageAsync(queries, request.Cancellation.Token);
            if (!IsCurrentRequest(request))
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
        catch (OperationCanceledException) when (request.Cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (IsCurrentRequest(request))
            {
                HasMore = false;
                NextCursor = null;
                _retryOperation = GalleryRetryOperation.FirstPage;
                ErrorMessage = ToUserMessage(ex);
            }
        }
        finally
        {
            if (EndRequest(request))
            {
                IsInitialLoading = false;
                IsRefreshing = false;
            }
        }
    }

    private async Task LoadBrowseOptionsSafelyAsync()
    {
        if (_browseOptionsLoaded || IsBrowseOptionsLoading)
        {
            return;
        }

        IsBrowseOptionsLoading = true;
        BrowseOptionsErrorMessage = null;
        try
        {
            var hierarchy = await GetHierarchyAsync();
            var years = FastGalleryYearCatalog.FromHierarchy(hierarchy);

            while (BrowseOptions.Count > 1)
            {
                BrowseOptions.RemoveAt(BrowseOptions.Count - 1);
            }

            foreach (var year in years)
            {
                var count = hierarchy.Roots.First(node => node.Year == year).Count;
                BrowseOptions.Add(new MobileGalleryBrowseOption(
                    $"{year}년",
                    GalleryBrowseScope.ForYear(year),
                    count));
            }

            var unclassifiedCount = GalleryBrowseCatalog.CountUnclassified(hierarchy);
            if (unclassifiedCount > 0)
            {
                BrowseOptions.Add(new MobileGalleryBrowseOption(
                    "장소 미분류",
                    GalleryBrowseScope.ForUnclassified(),
                    unclassifiedCount));
            }

            _browseOptionsLoaded = true;
        }
        catch (Exception)
        {
            BrowseOptionsErrorMessage = "연도 목록을 불러오지 못했습니다.";
        }
        finally
        {
            IsBrowseOptionsLoading = false;
        }
    }

    private async Task<FastGalleryHierarchyDto> GetHierarchyAsync()
    {
        if (_hierarchy is not null)
        {
            return _hierarchy;
        }

        await _hierarchyGate.WaitAsync();
        try
        {
            _hierarchy ??= await _repository.GetHierarchyAsync();
            return _hierarchy;
        }
        finally
        {
            _hierarchyGate.Release();
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
        _retryOperation = GalleryRetryOperation.None;
    }

    private GalleryRequestContext BeginRequest(bool cancelCurrent, long generation)
    {
        lock (_requestLock)
        {
            if (cancelCurrent)
            {
                _requestCancellation?.Cancel();
            }

            var cancellation = new CancellationTokenSource();
            _requestCancellation = cancellation;
            return new GalleryRequestContext(cancellation, generation);
        }
    }

    private bool IsCurrentRequest(GalleryRequestContext request)
    {
        lock (_requestLock)
        {
            return ReferenceEquals(_requestCancellation, request.Cancellation)
                   && request.Generation == Volatile.Read(ref _scopeGeneration)
                   && !request.Cancellation.IsCancellationRequested;
        }
    }

    private bool EndRequest(GalleryRequestContext request)
    {
        bool wasCurrent;
        lock (_requestLock)
        {
            wasCurrent = ReferenceEquals(_requestCancellation, request.Cancellation)
                         && request.Generation == Volatile.Read(ref _scopeGeneration);
            if (ReferenceEquals(_requestCancellation, request.Cancellation))
            {
                _requestCancellation = null;
            }
        }

        request.Cancellation.Dispose();
        return wasCurrent;
    }

    private static string ToUserMessage(Exception exception) => exception switch
    {
        MobileBackendConfigurationException => "사진첩 연결 설정을 확인할 수 없습니다.",
        MobileBackendApiException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } =>
            "사진첩에 연결할 수 없습니다.",
        _ => "사진첩을 불러올 수 없습니다. 잠시 후 다시 시도해 주세요.",
    };

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => NotifyViewStateChanged();

    private void OnSearchResultsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        OnPropertyChanged(nameof(IsSearchResultsVisible));

    partial void OnIsInitialLoadingChanged(bool value) => NotifyViewStateChanged();

    partial void OnErrorMessageChanged(string? value) => NotifyViewStateChanged();

    partial void OnBrowseOptionsErrorMessageChanged(string? value) =>
        OnPropertyChanged(nameof(IsBrowseOptionsErrorVisible));

    private void NotifyViewStateChanged()
    {
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(IsGalleryVisible));
        OnPropertyChanged(nameof(IsEmptyStateVisible));
        OnPropertyChanged(nameof(IsErrorStateVisible));
        OnPropertyChanged(nameof(IsInlineErrorVisible));
        OnPropertyChanged(nameof(IsSearchErrorVisible));
    }

    private void NotifySearchStateChanged()
    {
        OnPropertyChanged(nameof(IsSearchResultsVisible));
        NotifyViewStateChanged();
    }

    private void NotifyContextStateChanged()
    {
        OnPropertyChanged(nameof(IsGalleryMode));
        OnPropertyChanged(nameof(IsYearSelectionMode));
        OnPropertyChanged(nameof(IsPlaceSelectionMode));
        OnPropertyChanged(nameof(IsPlaceYearSelectionMode));
        OnPropertyChanged(nameof(IsSearchMode));
        OnPropertyChanged(nameof(CanNavigateBack));
        OnPropertyChanged(nameof(CanOpenHierarchyNavigator));
        OnPropertyChanged(nameof(CanOpenSiblingYearSelection));
        OnPropertyChanged(nameof(IsGalleryYearVisible));
        OnPropertyChanged(nameof(IsStaticGalleryYearVisible));
        OnPropertyChanged(nameof(IsRecentContext));
        OnPropertyChanged(nameof(PlaceSelectionTitle));
        OnPropertyChanged(nameof(IsSearchErrorVisible));
        NotifyViewStateChanged();
    }

    private sealed record GalleryRequestContext(
        CancellationTokenSource Cancellation,
        long Generation);

    private sealed record PlaceTreeState(
        GalleryYearBrowseTree Source,
        IReadOnlyList<MobileGalleryTreeNode> Roots);

    private enum GalleryRetryOperation
    {
        None,
        FirstPage,
        LoadMore,
    }
}
