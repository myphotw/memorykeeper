using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MemoryKeeper.App.Diagnostics;
using MemoryKeeper.App.Services;
using MemoryKeeper.App.ViewModels;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Navigation;
using MemoryKeeper.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MemoryKeeper.App.Views;

public sealed partial class MainWindow : Window
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IPhotoNavigationState _photoNavigationState;
    private readonly IPlaceEditorSeedState _placeEditorSeedState;
    private readonly IPlaceFocusState _placeFocusState;
    private readonly IResponsiveLayoutService _responsiveLayout;
    private readonly INavigationService _navigation;
    private readonly ICatalogInvalidation _catalogInvalidation;
    private readonly BackendChangeMonitorService _backendChangeMonitor;
    private readonly Dictionary<string, Page> _pageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _loadedPages = new(StringComparer.OrdinalIgnoreCase);
    private HomeViewModel? _homeViewModel;
    private TravelRecordsViewModel? _travelRecordsViewModel;
    private TravelRecordsPage? _travelRecordsPage;
    private TravelRecordsDetailViewModel? _travelRecordsDetailViewModel;
    private VisitRecordPage? _visitRecordPage;
    private VisitRecordViewModel? _visitRecordViewModel;
    private PhotoDetailViewModel? _photoDetailViewModel;
    private PhotoViewerViewModel? _photoViewerViewModel;
    private SetupWizardViewModel? _setupWizardViewModel;
    private PlaceManagementViewModel? _placeManagementViewModel;
    private TagManagementViewModel? _tagManagementViewModel;
    private PendingMemoryViewModel? _pendingMemoryViewModel;
    private ImportPage? _importPage;
    private FavoritesViewModel? _favoritesViewModel;
    private GalleryPage? _galleryPage;
    private GalleryViewModel? _galleryViewModel;
    private SettingsPage? _settingsPage;
    private bool _isSetupMode;
    private bool _suppressSelectionNavigation;
    private bool _navigatingBack;
    private bool _navigatingForward;
    private VisitMapNavigationSource _pendingVisitNavSource = VisitMapNavigationSource.ShellNav;

    public MainViewModel ViewModel { get; }

    public MainWindow(
        MainViewModel viewModel,
        IServiceProvider serviceProvider,
        IPhotoNavigationState photoNavigationState,
        IPlaceEditorSeedState placeEditorSeedState,
        IPlaceFocusState placeFocusState,
        IResponsiveLayoutService responsiveLayout,
        INavigationService navigation,
        ICatalogInvalidation catalogInvalidation,
        BackendChangeMonitorService backendChangeMonitor)
    {
        ViewModel = viewModel;
        _serviceProvider = serviceProvider;
        _photoNavigationState = photoNavigationState;
        _placeEditorSeedState = placeEditorSeedState;
        _placeFocusState = placeFocusState;
        _responsiveLayout = responsiveLayout;
        _navigation = navigation;
        _catalogInvalidation = catalogInvalidation;
        _backendChangeMonitor = backendChangeMonitor;
        InitializeComponent();
        if (Content is FrameworkElement root)
        {
            root.DataContext = ViewModel;
            root.SizeChanged += Root_OnSizeChanged;
            root.Loaded += (_, _) => _responsiveLayout.UpdateWindowWidth(root.ActualWidth);
            root.PointerPressed += Root_OnPointerPressed;
            root.KeyDown += Root_OnKeyDown;
            if (DebugStatusText is not null)
            {
                DebugStatusText.Visibility = Visibility.Collapsed;
            }
        }
        ConfigureWindow();
        MemoryKeeper.App.Diagnostics.ErrorDialog.RegisterUiDispatcher(
            Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
        _photoNavigationState.OpenRequested += OnPhotoOpenRequested;
        Activated += OnActivated;
        Activated += OnBackendChangesActivated;
    }

    private async void OnBackendChangesActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            return;
        }

        try
        {
            var changed = await _backendChangeMonitor.CheckForChangesAsync();
            if (changed
                && (_backendChangeMonitor.LastAffectedSurfaces & CatalogSurface.AllMemoryKeeper)
                == CatalogSurface.AllMemoryKeeper)
            {
                _navigation.Clear();
                SelectNavigationItem("home");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Backend change check failed: {ex.Message}");
        }
    }

    private void Root_OnSizeChanged(object sender, SizeChangedEventArgs e) =>
        _responsiveLayout.UpdateWindowWidth(e.NewSize.Width);

    private void ConfigureWindow()
    {
        Title = "Memory Keeper";
        var appWindow = AppWindow;
        if (appWindow is not null)
        {
            appWindow.Resize(new Windows.Graphics.SizeInt32(1280, 800));
        }
    }

    private async void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnActivated;
        MemoryKeeper.App.Diagnostics.StartupDiagnostics.WriteStep("[7] MainWindow.OnActivated 시작");

        try
        {
            var status = App.LaunchSetupStatus;
            if (status is null)
            {
                using var scope = _serviceProvider.CreateScope();
                var setup = scope.ServiceProvider.GetRequiredService<SetupWizardService>();
                status = await setup.GetStatusAsync();
            }

            if (status.NeedsSetup)
            {
                MemoryKeeper.App.Diagnostics.StartupDiagnostics.WriteStep("[7] EnterSetupMode");
                EnterSetupMode();
                return;
            }
        }
        catch (Exception ex)
        {
            MemoryKeeper.App.Diagnostics.StartupDiagnostics.WriteException(
                "MainWindow.OnActivated SetupWizard",
                ex);
        }

        MemoryKeeper.App.Diagnostics.StartupDiagnostics.WriteStep("[7] NavigateToHome");
        ExitSetupMode();
        _navigation.Clear();
        _navigatingBack = true;
        try
        {
            SelectNavigationItem("home");
        }
        finally
        {
            _navigatingBack = false;
        }
    }

    private void RootNavigation_OnItemInvoked(
        NavigationView sender,
        NavigationViewItemInvokedEventArgs args)
    {
        if (_isSetupMode || _suppressSelectionNavigation)
        {
            return;
        }

        if (args.InvokedItemContainer is not NavigationViewItem { Tag: string tag })
        {
            return;
        }

        // Always honor Home clicks — SelectionChanged skips when Home is already selected
        // (common while photo viewer/detail is shown with Home still highlighted).
        if (tag == "home")
        {
            NavigateToHome();
        }
    }

    private void RootNavigation_OnSelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (_isSetupMode || _suppressSelectionNavigation)
        {
            return;
        }

        if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag)
        {
            return;
        }

        NavigateByTag(tag);
    }

    private void NavigateByTag(string tag)
    {
        switch (tag)
        {
            case "home":
                NavigateToHome();
                break;
            case "travel":
                NavigateToTravelRecords();
                break;
            case "travel-detail":
                NavigateToTravelRecordsDetail();
                break;
            case "visits":
            case "timeline":
            case "map":
            case "search":
                NavigateToVisitRecord();
                break;
            case "pending":
                NavigateToPendingMemory();
                break;
            case "gallery":
                NavigateToGallery();
                break;
            case "favorites":
                NavigateToFavorites();
                break;
            case "storage":
                NavigateToSettingsSection("storage");
                break;
            case "import":
                NavigateToImport();
                break;
            case "place":
                NavigateToPlace();
                break;
            case "tag":
                NavigateToSettingsSection("tags");
                break;
            case "settings":
                NavigateToSettings("overview");
                break;
            case "logs":
                NavigateToSettingsSection("logs");
                break;
            case "photo-viewer":
                NavigateToPhotoViewer();
                break;
            case "photo":
                NavigateToPhotoDetail();
                break;
            case "setup":
                EnterSetupMode();
                break;
            default:
                NavigateToHome();
                break;
        }
    }

    private void Track(string tag, string? settingsSection = null)
    {
        var entry = NavigationEntry.Of(tag, settingsSection);
        if (_navigatingBack || _navigatingForward)
        {
            _navigation.ReplaceCurrent(entry);
            LogNavigation($"ReplaceCurrent tag={tag}");
            return;
        }

        // Explicit Home (GNB) clears history. All other navigations push caller onto the back stack.
        if (tag == "home")
        {
            CaptureCurrentPageState();
            _navigation.NavigateRoot(NavigationEntry.Home);
            LogNavigation("NavigateRoot home");
            return;
        }

        if (_navigation.IsCurrent(entry))
        {
            LogNavigation($"Skip duplicate Track tag={tag}");
            return;
        }

        CaptureCurrentPageState();
        _navigation.Navigate(entry);
        _navigation.RemoveConsecutiveDuplicates();
        LogNavigation($"Navigate tag={tag}");
    }

    private void LogNavigation(string action)
    {
        var current = _navigation.Current?.Tag ?? "(null)";
        var stack = string.Join(" > ", _navigation.GetBackStackTags());
        StartupDiagnostics.WriteStep(
            $"[Nav] {action} | current={current} | back=[{stack}] | back/forward={_navigatingBack}/{_navigatingForward}");
    }

    private static bool IsTopLevelTag(string? tag) =>
        tag is "home" or "visits" or "gallery" or "travel" or "settings" or "search";

    private string? GetHierarchicalParentTag(string? tag) =>
        tag switch
        {
            null or "home" => null,
            "visits" or "gallery" or "travel" or "settings" or "search" => "home",
            "travel-detail" => "travel",
            "favorites" => "gallery",
            "photo-viewer" => _photoNavigationState.ReturnSourceTag,
            "photo" when _photoNavigationState.DetailOpenedFromViewer => "photo-viewer",
            "photo" => "gallery",
            "import" or "pending" or "place" or "tag" or "storage" or "logs" => "settings",
            _ => "home"
        };

    private void NavigateBack()
    {
        LogNavigation("NavigateBack requested");
        if (_navigation.TryGoBack(out var entry))
        {
            _navigatingBack = true;
            try
            {
                LogNavigation($"TryGoBack → {entry.Tag}");
                RestoreEntry(entry);
            }
            finally
            {
                _navigatingBack = false;
            }

            return;
        }

        var currentTag = _navigation.Current?.Tag;
        var parentTag = GetHierarchicalParentTag(currentTag);
        LogNavigation($"Empty back stack; hierarchical parent={parentTag ?? "(null)"} from={currentTag}");
        _navigatingBack = true;
        try
        {
            if (parentTag is null)
            {
                SelectNavigationItem("home");
            }
            else if (parentTag == "settings")
            {
                var section = currentTag switch
                {
                    "import" => "photo-management",
                    "pending" => "pending-memories",
                    "place" => "places",
                    "tag" => "tags",
                    "storage" => "storage",
                    "logs" => "logs",
                    _ => "overview"
                };
                NavigateToSettings(section);
                _suppressSelectionNavigation = true;
                try
                {
                    SyncNavigationSelection("settings");
                }
                finally
                {
                    _suppressSelectionNavigation = false;
                }
            }
            else
            {
                SelectNavigationItem(parentTag);
            }
        }
        finally
        {
            _navigatingBack = false;
        }
    }

    private void NavigateForward()
    {
        if (!_navigation.TryGoForward(out var entry))
        {
            return;
        }

        _navigatingForward = true;
        try
        {
            RestoreEntry(entry);
        }
        finally
        {
            _navigatingForward = false;
        }
    }

    private void Root_OnPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(null);
        if (point.Properties.IsXButton1Pressed)
        {
            NavigateBack();
            e.Handled = true;
        }
        else if (point.Properties.IsXButton2Pressed)
        {
            NavigateForward();
            e.Handled = true;
        }
    }

    private void Root_OnKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        var alt = Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (alt)
        {
            if (e.Key == Windows.System.VirtualKey.Left)
            {
                NavigateBack();
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Right)
            {
                NavigateForward();
                e.Handled = true;
            }

            return;
        }

        // Photo viewer often lacks keyboard focus (chrome / frame). Forward keys from the window root.
        if (ContentFrame.Content is PhotoViewerPage viewer
            && viewer.TryHandleKey(e.Key))
        {
            e.Handled = true;
        }
    }

    private void CaptureCurrentPageState()
    {
        switch (ContentFrame.Content)
        {
            case GalleryPage gallery:
            {
                var offset = gallery.GetGridScrollOffsetPublic();
                gallery.ViewModel.CaptureFocusState(offset);
                var selectedIndex = -1;
                if (gallery.ViewModel.SelectedItem is { } selected)
                {
                    for (var i = 0; i < gallery.ViewModel.Items.Count; i++)
                    {
                        if (gallery.ViewModel.Items[i].MediaId == selected.MediaId)
                        {
                            selectedIndex = i;
                            break;
                        }
                    }
                }

                _navigation.SavePageState("gallery", new NavigationPageState
                {
                    SearchText = gallery.ViewModel.SearchText,
                    ScrollPosition = offset,
                    SelectedItemKey = gallery.ViewModel.SelectedItem?.MediaId.ToString(),
                    SelectedThumbnailIndex = selectedIndex,
                    Extra = new Dictionary<string, string>
                    {
                        ["browseMode"] = gallery.ViewModel.BrowseModeIndex.ToString()
                    }
                });
                break;
            }
            case VisitRecordPage visit:
                _navigation.SavePageState("visits", new NavigationPageState
                {
                    SearchText = visit.ViewModel.SearchText,
                    Filter = visit.ViewModel.SelectedYear?.ToString()
                });
                break;
            case PhotoViewerPage viewer:
                _navigation.SavePageState("photo-viewer", new NavigationPageState
                {
                    ZoomFactor = viewer.GetZoomFactor(),
                    SelectedItemKey = _photoNavigationState.FocusMediaId?.ToString()
                });
                break;
        }
    }

    private T GetOrCreatePage<T>(string cacheKey) where T : Page
    {
        if (_pageCache.TryGetValue(cacheKey, out var cached) && cached is T typed)
        {
            return typed;
        }

        typed = _serviceProvider.GetRequiredService<T>();
        _pageCache[cacheKey] = typed;
        return typed;
    }

    private bool ShouldReload(string cacheKey, CatalogSurface surface = CatalogSurface.None)
    {
        var firstLoad = !_loadedPages.Contains(cacheKey);
        var dirty = surface != CatalogSurface.None && _catalogInvalidation.Consume(surface);
        return firstLoad || dirty;
    }

    private void MarkLoaded(string cacheKey) => _loadedPages.Add(cacheKey);

    private void RestoreEntry(NavigationEntry entry)
    {
        switch (entry.Tag)
        {
            case "home":
                SelectNavigationItem("home");
                break;
            case "travel":
                SelectNavigationItem("travel");
                break;
            case "travel-detail":
                SelectNavigationItem("travel-detail");
                break;
            case "visits":
                SelectNavigationItem("visits");
                break;
            case "pending":
                SelectNavigationItem("pending");
                break;
            case "gallery":
                SelectNavigationItem("gallery");
                break;
            case "favorites":
                SelectNavigationItem("favorites");
                break;
            case "import":
                SelectNavigationItem("import");
                break;
            case "place":
                SelectNavigationItem("place");
                break;
            case "photo-viewer":
                SelectNavigationItem("photo-viewer");
                break;
            case "photo":
                SelectNavigationItem("photo");
                break;
            case "settings":
                NavigateToSettings(entry.SettingsSection ?? "overview");
                _suppressSelectionNavigation = true;
                try
                {
                    SyncNavigationSelection("settings");
                }
                finally
                {
                    _suppressSelectionNavigation = false;
                }
                break;
            default:
                SelectNavigationItem("home");
                break;
        }
    }

    private void EnterSetupMode()
    {
        _isSetupMode = true;
        RootNavigation.IsPaneOpen = false;
        RootNavigation.IsPaneToggleButtonVisible = false;
        DetachHandlers();

        var page = _serviceProvider.GetRequiredService<SetupWizardPage>();
        if (_setupWizardViewModel is not null)
        {
            _setupWizardViewModel.SetupCompleted -= OnSetupCompleted;
        }

        _setupWizardViewModel = page.ViewModel;
        _setupWizardViewModel.SetupCompleted += OnSetupCompleted;
        ContentFrame.Content = page;
        ViewModel.SetUiStatus("최초 설정을 완료하세요.");
    }

    private void ExitSetupMode()
    {
        _isSetupMode = false;
        RootNavigation.IsPaneToggleButtonVisible = false;
    }

    private void OnSetupCompleted(object? sender, EventArgs e)
    {
        if (_setupWizardViewModel is not null)
        {
            _setupWizardViewModel.SetupCompleted -= OnSetupCompleted;
            _setupWizardViewModel = null;
        }

        ExitSetupMode();
        _navigation.Clear();
        _navigatingBack = true;
        try
        {
            SelectNavigationItem("home");
        }
        finally
        {
            _navigatingBack = false;
        }

        ViewModel.SetUiStatus("초기 설정이 완료되었습니다.");
    }

    private void NavigateToHome()
    {
        Track("home");
        DetachHandlers();
        var page = GetOrCreatePage<HomePage>("home");
        _homeViewModel = page.ViewModel;
        _homeViewModel.OpenVisitRecordRequested += OnHomeOpenVisitRecordRequested;
        _homeViewModel.OpenGalleryRequested += OnHomeOpenGalleryRequested;
        _homeViewModel.OpenPendingRequested += OnHomeOpenPendingRequested;
        _homeViewModel.OpenImportRequested += OnHomeOpenImportRequested;
        _homeViewModel.OpenTagRequested += OnHomeOpenTagRequested;
        _homeViewModel.OpenPlaceRequested += OnHomeOpenPlaceRequested;
        _homeViewModel.OpenStorageRequested += OnHomeOpenStorageRequested;
        _homeViewModel.OpenStatisticsRequested += OnHomeOpenTravelRecordsRequested;
        _homeViewModel.OpenSettingsRequested += OnHomeOpenSettingsRequested;
        ContentFrame.Content = page;
        if (ShouldReload("home", CatalogSurface.Home))
        {
            MarkLoaded("home");
            _ = page.ViewModel.LoadCommand.ExecuteAsync(null);
        }
        else
        {
            page.ViewModel.ResumeHeroCarousel();
        }
    }

    private void NavigateToTravelRecords()
    {
        Track("travel");
        DetachHandlers();
        var page = GetOrCreatePage<TravelRecordsPage>("travel");
        _travelRecordsPage = page;
        _travelRecordsViewModel = page.ViewModel;
        _travelRecordsViewModel.OpenVisitRecordRequested += OnTravelOpenVisitRecordRequested;
        _travelRecordsViewModel.OpenDetailRequested += OnTravelOpenDetailRequested;
        _travelRecordsViewModel.BackRequested += OnShellBackRequested;
        page.OpenImportRequested += OnTravelOpenImportRequested;
        ContentFrame.Content = page;
        // Always reload from Backend (Import / catalog updates must appear).
        MarkLoaded("travel");
        _ = page.ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void NavigateToTravelRecordsDetail()
    {
        Track("travel-detail");
        DetachHandlers();
        var page = GetOrCreatePage<TravelRecordsDetailPage>("travel-detail");
        _travelRecordsDetailViewModel = page.ViewModel;
        _travelRecordsDetailViewModel.OpenVisitRecordRequested += OnTravelOpenVisitRecordRequested;
        _travelRecordsDetailViewModel.BackRequested += OnShellBackRequested;
        ContentFrame.Content = page;
    }

    private void NavigateToVisitRecord()
    {
        Track("visits");
        DetachHandlers();
        var page = GetOrCreatePage<VisitRecordPage>("visits");
        _visitRecordPage = page;
        _visitRecordViewModel = page.ViewModel;
        _visitRecordViewModel.OpenGalleryRequested += OnVisitOpenGalleryRequested;
        _visitRecordViewModel.OpenPlaceManagementRequested += OnVisitOpenPlaceRequested;
        _visitRecordViewModel.BackRequested += OnShellBackRequested;
        page.OpenImportRequested += OnVisitOpenImportRequested;
        ContentFrame.Content = page;

        var reloadData = ShouldReload("visits", CatalogSurface.Visits);
        if (reloadData)
        {
            MarkLoaded("visits");
        }

        var source = _pendingVisitNavSource;
        _pendingVisitNavSource = VisitMapNavigationSource.ShellNav;
        var generation = _placeFocusState.BeginNavigation(source);

        // Do not ApplyPendingFocus here — wait for layout + mapReady via ActivateAsync.
        LogNavigation(
            $"NavigateToVisitRecord Activate Source={source} Gen={generation} Reload={reloadData} Focus={_placeFocusState.HasPendingFocus}");

        page.DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                await page.ActivateAsync(generation, source, reloadData);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"VisitRecord Activate failed: {ex}");
            }
        });
    }

    private void NavigateToPendingMemory()
    {
        Track("pending");
        DetachHandlers();
        var page = GetOrCreatePage<PendingMemoryPage>("pending");
        _pendingMemoryViewModel = page.ViewModel;
        _pendingMemoryViewModel.BackRequested += OnShellBackRequested;
        ContentFrame.Content = page;
        if (ShouldReload("pending", CatalogSurface.Pending))
        {
            MarkLoaded("pending");
            _ = page.ViewModel.LoadCommand.ExecuteAsync(null);
        }
        else if (!_navigatingBack)
        {
            _ = page.ViewModel.LoadCommand.ExecuteAsync(null);
        }
    }

    private void NavigateToGallery()
    {
        Track("gallery");
        DetachHandlers();
        MemoryKeeper.App.Diagnostics.GalleryDiagnostics.WriteStep("NavigateToGallery start");
        try
        {
            var page = GetOrCreatePage<GalleryPage>("gallery");
            _galleryPage = page;
            _galleryViewModel = page.ViewModel;
            _galleryViewModel.BackRequested += OnShellBackRequested;
            page.OpenImportRequested += OnGalleryOpenImportRequested;
            page.OpenPendingRequested += OnGalleryOpenPendingRequested;
            ContentFrame.Content = page;
            if (ShouldReload("gallery", CatalogSurface.Gallery))
            {
                MemoryKeeper.App.Diagnostics.GalleryDiagnostics.WriteStep("NavigateToGallery page set, LoadAsync fire");
                MarkLoaded("gallery");
                _ = LoadGallerySafeAsync(page);
            }
            else
            {
                MemoryKeeper.App.Diagnostics.GalleryDiagnostics.WriteStep("NavigateToGallery restore cached page");
            }
        }
        catch (Exception ex)
        {
            MemoryKeeper.App.Diagnostics.GalleryDiagnostics.WriteException("NavigateToGallery", ex);
#if DEBUG
            ViewModel.SetUiStatus("사진을 불러오는 중 오류가 발생했습니다.");
#endif
            MemoryKeeper.App.Diagnostics.ErrorDialog.Show(
                MemoryKeeper.Application.Diagnostics.ErrorReportSource.Gallery,
                "Memory Keeper — 사진첩 오류",
                ex,
                stage: "NavigateToGallery");
        }
    }

    private static async Task LoadGallerySafeAsync(GalleryPage page)
    {
        try
        {
            await page.ViewModel.LoadCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            MemoryKeeper.App.Diagnostics.GalleryDiagnostics.WriteException("LoadCommand.ExecuteAsync", ex);
            page.ViewModel.StatusMessage = "사진을 불러오는 중 오류가 발생했습니다.";
            MemoryKeeper.App.Diagnostics.ErrorDialog.Show(
                MemoryKeeper.Application.Diagnostics.ErrorReportSource.Gallery,
                "Memory Keeper — 사진첩 오류",
                ex,
                stage: "LoadCommand.ExecuteAsync");
        }
    }

    private void NavigateToFavorites()
    {
        Track("favorites");
        DetachHandlers();
        var page = GetOrCreatePage<FavoritesPage>("favorites");
        _favoritesViewModel = page.ViewModel;
        _favoritesViewModel.BackRequested += OnShellBackRequested;
        ContentFrame.Content = page;
        if (ShouldReload("favorites", CatalogSurface.Favorites))
        {
            MarkLoaded("favorites");
            _ = page.ViewModel.LoadCommand.ExecuteAsync(null);
        }
    }

    private void NavigateToPhotoViewer()
    {
        Track("photo-viewer");
        DetachHandlers();
        // Viewer always reloads current media; do not cache across sessions.
        var page = _serviceProvider.GetRequiredService<PhotoViewerPage>();
        _photoViewerViewModel = page.ViewModel;
        _photoViewerViewModel.Closed += OnPhotoViewerClosed;
        _photoViewerViewModel.OpenDetailRequested += OnPhotoViewerOpenDetailRequested;
        _photoViewerViewModel.OpenMapRequested += OnPhotoDetailOpenMapRequested;
        ContentFrame.Content = page;
        _ = page.ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void NavigateToPhotoDetail()
    {
        Track("photo");
        DetachHandlers();
        var page = _serviceProvider.GetRequiredService<PhotoDetailPage>();
        _photoDetailViewModel = page.ViewModel;
        _photoDetailViewModel.Closed += OnPhotoDetailClosed;
        _photoDetailViewModel.OpenMapRequested += OnPhotoDetailOpenMapRequested;
        ContentFrame.Content = page;
    }

    private void NavigateToImport()
    {
        Track("import");
        DetachHandlers();
        var page = _serviceProvider.GetRequiredService<ImportPage>();
        page.ViewModel.ImportCompletedNavigateHome += OnImportCompletedNavigateHome;
        page.ViewModel.BackRequested += OnShellBackRequested;
        _importPage = page;
        ContentFrame.Content = page;
        _ = page.ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void OnImportCompletedNavigateHome(object? sender, EventArgs e)
    {
        _navigation.Clear();
        _navigatingBack = true;
        try
        {
            SelectNavigationItem("home");
        }
        finally
        {
            _navigatingBack = false;
        }
    }

    private void NavigateToPlace()
    {
        Track("place");
        DetachHandlers();
        var page = _serviceProvider.GetRequiredService<PlaceManagementPage>();
        _placeManagementViewModel = page.ViewModel;
        _placeManagementViewModel.BackRequested += OnShellBackRequested;
        ContentFrame.Content = page;
        _ = page.ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void NavigateToSettings(string? section = null)
    {
        var targetSection = string.IsNullOrWhiteSpace(section) ? "overview" : section;
        Track("settings", targetSection);
        DetachHandlers();
        var page = GetOrCreatePage<SettingsPage>("settings");
        page.ViewModel.ResetCompleted += OnSettingsResetCompleted;
        _settingsPage = page;
        ContentFrame.Content = page;
        _ = page.ViewModel.LoadCommand.ExecuteAsync(targetSection);
    }

    private void NavigateToSettingsSection(string section) =>
        NavigateToSettings(section);

    private void OnSettingsResetCompleted(object? sender, EventArgs e)
    {
        _navigation.Clear();
        SelectNavigationItem("home");
    }

    private void OnShellBackRequested(object? sender, EventArgs e) =>
        NavigateBack();

    private void OnPhotoOpenRequested(object? sender, EventArgs e)
    {
        // OpenRequested is for Viewer entry from list surfaces only (not Detail back).
        var tag = _photoNavigationState.Target == PhotoNavigationTarget.Detail
            ? "photo"
            : "photo-viewer";
        SelectNavigationItem(tag);
    }

    private void OnPhotoViewerClosed(object? sender, EventArgs e) => NavigateBack();

    private void OnPhotoViewerOpenDetailRequested(object? sender, EventArgs e) =>
        SelectNavigationItem("photo");

    private void OnPhotoDetailClosed(object? sender, EventArgs e)
    {
        // Always pop BackStack — never Navigate(photo-viewer) or the stack becomes
        // gallery > viewer > detail > viewer and Detail↔Viewer loops forever.
        if (_photoNavigationState.DetailOpenedFromViewer)
        {
            _photoNavigationState.DetailOpenedFromViewer = false;
        }

        NavigateBack();
    }

    private void OnPhotoDetailOpenMapRequested(object? sender, EventArgs e) =>
        SelectNavigationItem("visits");

    private void OnGalleryOpenTravelRequested(object? sender, EventArgs e) =>
        SelectNavigationItem("travel-detail");

    private void OnVisitOpenGalleryRequested(object? sender, EventArgs e) =>
        SelectNavigationItem("gallery");

    private void OnVisitOpenPlaceRequested(object? sender, EventArgs e) =>
        SelectNavigationItem("place");

    private void OnHomeOpenVisitRecordRequested(object? sender, EventArgs e)
    {
        _pendingVisitNavSource = VisitMapNavigationSource.Home;
        SelectNavigationItem("visits");
    }

    private void OnHomeOpenGalleryRequested(object? sender, EventArgs e) =>
        SelectNavigationItem("gallery");

    private void OnHomeOpenPendingRequested(object? sender, EventArgs e) =>
        SelectNavigationItem("pending");

    private void OnHomeOpenImportRequested(object? sender, EventArgs e) =>
        SelectNavigationItem("import");

    private void OnHomeOpenTagRequested(object? sender, EventArgs e) =>
        NavigateToSettingsSection("tags");

    private void OnHomeOpenPlaceRequested(object? sender, EventArgs e) =>
        SelectNavigationItem("place");

    private void OnHomeOpenStorageRequested(object? sender, EventArgs e) =>
        NavigateToSettingsSection("storage");

    private void OnHomeOpenTravelRecordsRequested(object? sender, EventArgs e) =>
        SelectNavigationItem("travel");

    private void OnHomeOpenSettingsRequested(object? sender, EventArgs e) =>
        SelectNavigationItem("settings");

    private void OnTravelOpenVisitRecordRequested(object? sender, EventArgs e)
    {
        _pendingVisitNavSource = VisitMapNavigationSource.TravelRecord;
        SelectNavigationItem("visits");
    }

    private void OnTravelOpenImportRequested(object? sender, EventArgs e) =>
        SelectNavigationItem("import");

    private void OnVisitOpenImportRequested(object? sender, EventArgs e) =>
        SelectNavigationItem("import");

    private void OnGalleryOpenImportRequested(object? sender, EventArgs e) =>
        SelectNavigationItem("import");

    private void OnGalleryOpenPendingRequested(object? sender, EventArgs e) =>
        SelectNavigationItem("pending");

    private void OnTravelOpenDetailRequested(object? sender, EventArgs e) =>
        SelectNavigationItem("travel-detail");

    private void DetachHandlers()
    {
        if (_homeViewModel is not null)
        {
            _homeViewModel.OpenVisitRecordRequested -= OnHomeOpenVisitRecordRequested;
            _homeViewModel.OpenGalleryRequested -= OnHomeOpenGalleryRequested;
            _homeViewModel.OpenPendingRequested -= OnHomeOpenPendingRequested;
            _homeViewModel.OpenImportRequested -= OnHomeOpenImportRequested;
            _homeViewModel.OpenTagRequested -= OnHomeOpenTagRequested;
            _homeViewModel.OpenPlaceRequested -= OnHomeOpenPlaceRequested;
            _homeViewModel.OpenStorageRequested -= OnHomeOpenStorageRequested;
            _homeViewModel.OpenStatisticsRequested -= OnHomeOpenTravelRecordsRequested;
            _homeViewModel.OpenSettingsRequested -= OnHomeOpenSettingsRequested;
            _homeViewModel.Stop();
            _homeViewModel = null;
        }

        if (_settingsPage is not null)
        {
            _settingsPage.ViewModel.ResetCompleted -= OnSettingsResetCompleted;
            _settingsPage = null;
        }

        if (_placeManagementViewModel is not null)
        {
            _placeManagementViewModel.BackRequested -= OnShellBackRequested;
            _placeManagementViewModel = null;
        }

        if (_tagManagementViewModel is not null)
        {
            _tagManagementViewModel = null;
        }

        if (_pendingMemoryViewModel is not null)
        {
            _pendingMemoryViewModel.BackRequested -= OnShellBackRequested;
            _pendingMemoryViewModel = null;
        }

        if (_importPage is not null)
        {
            _importPage.ViewModel.ImportCompletedNavigateHome -= OnImportCompletedNavigateHome;
            _importPage.ViewModel.BackRequested -= OnShellBackRequested;
            _importPage = null;
        }

        if (_favoritesViewModel is not null)
        {
            _favoritesViewModel.BackRequested -= OnShellBackRequested;
            _favoritesViewModel = null;
        }

        if (_galleryPage is not null)
        {
            _galleryPage.OpenImportRequested -= OnGalleryOpenImportRequested;
            _galleryPage.OpenPendingRequested -= OnGalleryOpenPendingRequested;
            _galleryPage = null;
        }

        if (_galleryViewModel is not null)
        {
            _galleryViewModel.BackRequested -= OnShellBackRequested;
            _galleryViewModel = null;
        }

        if (_photoViewerViewModel is not null)
        {
            _photoViewerViewModel.Closed -= OnPhotoViewerClosed;
            _photoViewerViewModel.OpenDetailRequested -= OnPhotoViewerOpenDetailRequested;
            _photoViewerViewModel.OpenMapRequested -= OnPhotoDetailOpenMapRequested;
            _photoViewerViewModel.DisposeImages();
            _photoViewerViewModel = null;
        }

        if (_travelRecordsViewModel is not null)
        {
            _travelRecordsViewModel.OpenVisitRecordRequested -= OnTravelOpenVisitRecordRequested;
            _travelRecordsViewModel.OpenDetailRequested -= OnTravelOpenDetailRequested;
            _travelRecordsViewModel.BackRequested -= OnShellBackRequested;
            _travelRecordsViewModel = null;
        }

        if (_travelRecordsPage is not null)
        {
            _travelRecordsPage.OpenImportRequested -= OnTravelOpenImportRequested;
            _travelRecordsPage = null;
        }

        if (_travelRecordsDetailViewModel is not null)
        {
            _travelRecordsDetailViewModel.OpenVisitRecordRequested -= OnTravelOpenVisitRecordRequested;
            _travelRecordsDetailViewModel.BackRequested -= OnShellBackRequested;
            _travelRecordsDetailViewModel = null;
        }

        if (_visitRecordPage is not null)
        {
            _visitRecordPage.OpenImportRequested -= OnVisitOpenImportRequested;
            _visitRecordPage = null;
        }

        if (_visitRecordViewModel is not null)
        {
            _visitRecordViewModel.OpenGalleryRequested -= OnVisitOpenGalleryRequested;
            _visitRecordViewModel.OpenPlaceManagementRequested -= OnVisitOpenPlaceRequested;
            _visitRecordViewModel.BackRequested -= OnShellBackRequested;
            _visitRecordViewModel = null;
        }

        if (_photoDetailViewModel is not null)
        {
            _photoDetailViewModel.Closed -= OnPhotoDetailClosed;
            _photoDetailViewModel.OpenMapRequested -= OnPhotoDetailOpenMapRequested;
            _photoDetailViewModel = null;
        }
    }

    private void SelectNavigationItem(string tag)
    {
        if (!_navigatingBack
            && !_navigatingForward
            && _navigation.IsCurrent(NavigationEntry.Of(tag))
            && IsContentShowingTag(tag))
        {
            // Travel/Home focus onto the already-visible visit map must still Activate.
            if (tag == "visits"
                && (_placeFocusState.HasPendingFocus || _placeFocusState.HasPendingFilters
                    || _pendingVisitNavSource is VisitMapNavigationSource.TravelRecord
                        or VisitMapNavigationSource.Home))
            {
                LogNavigation($"Re-Activate visit map in place. PendingSource={_pendingVisitNavSource}");
                NavigateToVisitRecord();
                return;
            }

            LogNavigation($"Skip duplicate SelectNavigationItem tag={tag}");
            return;
        }

        NavigateByTag(tag);
        _suppressSelectionNavigation = true;
        try
        {
            SyncNavigationSelection(tag);
        }
        finally
        {
            _suppressSelectionNavigation = false;
        }
    }

    private bool IsContentShowingTag(string tag) =>
        tag switch
        {
            "photo-viewer" => ContentFrame.Content is PhotoViewerPage,
            "photo" => ContentFrame.Content is PhotoDetailPage,
            "gallery" => ContentFrame.Content is GalleryPage,
            "home" => ContentFrame.Content is HomePage,
            "visits" => ContentFrame.Content is VisitRecordPage,
            "pending" => ContentFrame.Content is PendingMemoryPage,
            "favorites" => ContentFrame.Content is FavoritesPage,
            "import" => ContentFrame.Content is ImportPage,
            "place" => ContentFrame.Content is PlaceManagementPage,
            "travel" => ContentFrame.Content is TravelRecordsPage,
            "travel-detail" => ContentFrame.Content is TravelRecordsDetailPage,
            "settings" => ContentFrame.Content is SettingsPage,
            _ => false
        };

    private void SyncNavigationSelection(string tag)
    {
        var syncTag = tag switch
        {
            "search" or "timeline" or "map" => "visits",
            "storage" or "tag" or "logs" or "import" or "pending" or "place" => "settings",
            "photo" or "photo-viewer" or "travel-detail" or "favorites" =>
                MapToTopNavTag(
                    string.IsNullOrWhiteSpace(_photoNavigationState.ReturnSourceTag)
                        ? "home"
                        : _photoNavigationState.ReturnSourceTag),
            _ => tag
        };

        var items = RootNavigation.MenuItems.OfType<NavigationViewItem>()
            .Concat(RootNavigation.FooterMenuItems.OfType<NavigationViewItem>());

        foreach (var item in items)
        {
            if (item.Tag is string itemTag && itemTag == syncTag)
            {
                RootNavigation.SelectedItem = item;
                return;
            }
        }
    }

    private static string MapToTopNavTag(string tag) =>
        tag switch
        {
            "search" or "timeline" or "map" or "visits" => "visits",
            "storage" or "tag" or "logs" or "import" or "pending" or "place" or "settings" => "settings",
            "travel-detail" or "travel" => "travel",
            "gallery" or "favorites" => "gallery",
            "home" => "home",
            _ => "home"
        };
}
