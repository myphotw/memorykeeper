using MemoryKeeper.App.ViewModels;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Navigation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MemoryKeeper.App.Views;

public sealed partial class SettingsPage : Page
{
    private readonly PhotoManagementView _photoManagementView;
    private readonly PendingMemoryView _pendingView;
    private readonly PlaceManagementView _placeView;
    private readonly GalleryPage _embeddedGalleryPage;
    private readonly TagManagementView _tagView;
    private readonly INavigationService _navigation;
    private readonly ICatalogInvalidation _catalogInvalidation;
    private readonly HashSet<SettingsSection> _activatedSections = [];
    private readonly HashSet<SettingsSection> _activatingSections = [];
    private bool _syncingNavigation;
    private bool _isLoaded;
    private bool _preservePlaceStateOnNextActivation;
    private bool _isEmbeddedGalleryActivated;
    private bool _isEmbeddedGalleryVisible;
    private long _pendingEmbeddedGalleryInvalidationGeneration;
    private long _lastAppliedEmbeddedGalleryInvalidationGeneration;

    public SettingsViewModel ViewModel { get; }

    public bool IsPlaceWorkspaceRoundTripPending => _preservePlaceStateOnNextActivation;

    public bool IsEmbeddedGalleryVisible => _isEmbeddedGalleryVisible;

    public event EventHandler? BackRequested;

    public event EventHandler? OpenMapRequested;

    public void RequestPlaceSelection(Guid placeId)
    {
        _placeView.ViewModel.RequestSelection(placeId);
        ShowPlaceWorkspace();
    }

    public void PreparePlaceWorkspaceRoundTrip()
    {
        _preservePlaceStateOnNextActivation = true;
        _placeView.PreserveForGalleryRoundTrip();
    }

    public SettingsPage(
        SettingsViewModel viewModel,
        PhotoManagementView photoManagementView,
        PendingMemoryView pendingView,
        PlaceManagementView placeView,
        GalleryPage embeddedGalleryPage,
        TagManagementView tagView,
        INavigationService navigation,
        ICatalogInvalidation catalogInvalidation)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        _photoManagementView = photoManagementView;
        _pendingView = pendingView;
        _placeView = placeView;
        _embeddedGalleryPage = embeddedGalleryPage;
        _tagView = tagView;
        _navigation = navigation;
        _catalogInvalidation = catalogInvalidation;
        InitializeComponent();

        PhotoManagementHost.Content = _photoManagementView;
        PendingHost.Content = _pendingView;
        PlaceHost.Content = _placeView;
        _embeddedGalleryPage.ConfigureEmbeddedInSettings();
        EmbeddedGalleryHost.Content = _embeddedGalleryPage;
        TagHost.Content = _tagView;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        _placeView.ViewModel.OpenGalleryForPlaceRequested += OnOpenGalleryForPlaceRequested;
        _embeddedGalleryPage.OpenImportRequested += OnEmbeddedGalleryOpenImportRequested;
        _embeddedGalleryPage.OpenPendingRequested += OnEmbeddedGalleryOpenPendingRequested;
        _embeddedGalleryPage.OpenPlaceManagementRequested += OnEmbeddedGalleryOpenPlaceManagementRequested;
        _embeddedGalleryPage.OpenMapRequested += OnEmbeddedGalleryOpenMapRequested;
        _catalogInvalidation.Invalidated += OnCatalogInvalidated;
    }

    private void OnCatalogInvalidated(object? sender, CatalogInvalidatedEventArgs e)
    {
        if ((e.Surfaces & CatalogSurface.Gallery) == 0)
        {
            return;
        }

        // Keep the shared dirty bit for the top-level Gallery. The latest generation
        // collapses repeated successful mutations into one embedded refresh.
        Interlocked.Exchange(ref _pendingEmbeddedGalleryInvalidationGeneration, e.Generation);
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ViewModel.SelectedSettingsSection))
        {
            DispatcherQueue.TryEnqueue(SyncNavigationSelection);
            if (_isLoaded)
            {
                DispatcherQueue.TryEnqueue(async () => await ActivateSelectedSectionAsync());
            }
        }

        if (e.PropertyName is nameof(ViewModel.HasHomeSuggestions) && ViewModel.HasHomeSuggestions)
        {
            DispatcherQueue.TryEnqueue(() =>
                HomeSuggestionsPanel.StartBringIntoView(new BringIntoViewOptions
                {
                    AnimationDesired = true,
                    VerticalOffset = 32
                }));
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        ViewModel.HostXamlRoot = XamlRoot;
        ApplyResponsiveNavigation(ActualWidth);
        SyncNavigationSelection();
        RefreshBackNavigation();
        _ = ActivateSelectedSectionAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => _isLoaded = false;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.HostXamlRoot = XamlRoot;
        // MainWindow calls LoadCommand with the target section; avoid overwriting with null.
    }

    private void RefreshBackNavigation()
    {
        var current = _navigation.Current;
        var isVisible = current is { Kind: not NavigationKind.TopLevel }
                        && _navigation.CanGoBack;
        var label = _navigation.BackEntry?.DisplayLabel;
        label = string.IsNullOrWhiteSpace(label) ? "뒤로" : label.Trim();

        BackNavigationLabel.Text = label;
        BackNavigationButton.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(BackNavigationButton, $"{label}(으)로 돌아가기");
        AutomationProperties.SetName(BackNavigationButton, $"이전 화면: {label}");
    }

    private void BackNavigationButton_OnClick(object sender, RoutedEventArgs e) =>
        BackRequested?.Invoke(this, EventArgs.Empty);

    private async Task ActivateSelectedSectionAsync()
    {
        var section = ViewModel.SelectedSettingsSection;
        if (_activatedSections.Contains(section))
        {
            if (section == SettingsSection.Places)
            {
                await _placeView.ActivateAsync();
                if (_preservePlaceStateOnNextActivation)
                {
                    _preservePlaceStateOnNextActivation = false;
                }
                else if (!_isEmbeddedGalleryVisible)
                {
                    await _placeView.ViewModel.LoadCommand.ExecuteAsync(null);
                }
            }
            else if (section == SettingsSection.PendingMemories)
            {
                await _pendingView.ViewModel.LoadCommand.ExecuteAsync(null);
            }

            return;
        }

        if (!_activatingSections.Add(section))
        {
            return;
        }

        try
        {
            switch (section)
            {
                case SettingsSection.PhotoManagement:
                    await _photoManagementView.ActivateAsync();
                    break;
                case SettingsSection.PendingMemories:
                    await _pendingView.ViewModel.LoadCommand.ExecuteAsync(null);
                    break;
                case SettingsSection.Places:
                    // Map initialization is independent from the cached place catalog.
                    // Starting both together removes the first-entry serial wait.
                    await Task.WhenAll(
                        _placeView.ActivateAsync(),
                        _placeView.ViewModel.LoadCommand.ExecuteAsync(null));
                    break;
                case SettingsSection.Tags:
                    await _tagView.ViewModel.LoadCommand.ExecuteAsync(null);
                    break;
            }

            _activatedSections.Add(section);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Settings detail activation failed ({section}): {ex}");
            ViewModel.StatusMessage = "선택한 설정을 불러오지 못했습니다. 잠시 후 다시 시도해 주세요.";
        }
        finally
        {
            _activatingSections.Remove(section);
        }
    }

    private void HomeSuggestion_OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PlaceSuggestionDto suggestion)
        {
            _ = ViewModel.SelectHomeSuggestionCommand.ExecuteAsync(suggestion);
        }
    }

    private void SettingsNavigation_OnSelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (_syncingNavigation || args.SelectedItemContainer?.Tag is not string tag)
        {
            return;
        }

        if (tag == "photo-group")
        {
            return;
        }

        tag = tag switch
        {
            "photo" => "photo-management",
            "data" => "photo-export",
            "advanced" => "reset",
            _ => tag,
        };
        _ = ViewModel.SelectSettingsSectionCommand.ExecuteAsync(tag);
    }

    private async void GalleryWorkspaceTab_OnClick(object sender, RoutedEventArgs e) =>
        await ShowEmbeddedGalleryAsync();

    private void PlaceWorkspaceTab_OnClick(object sender, RoutedEventArgs e) =>
        ShowPlaceWorkspace();

    private async void OnOpenGalleryForPlaceRequested(object? sender, Guid placeId) =>
        await ShowEmbeddedGalleryAsync(placeId);

    private async void OnEmbeddedGalleryOpenPlaceManagementRequested(
        object? sender,
        GalleryPlaceManagementRequestedEventArgs e)
    {
        var isAlreadyLoaded = _placeView.ViewModel.Places.Any(place => place.Id == e.PlaceId);
        _placeView.ViewModel.RequestSelection(e.PlaceId);
        ShowPlaceWorkspace();
        if (!isAlreadyLoaded)
        {
            await _placeView.ViewModel.LoadCommand.ExecuteAsync(null);
        }
    }

    private void OnEmbeddedGalleryOpenImportRequested(object? sender, EventArgs e) =>
        _ = ViewModel.SelectSettingsSectionCommand.ExecuteAsync("photo-management");

    private void OnEmbeddedGalleryOpenPendingRequested(object? sender, EventArgs e) =>
        _ = ViewModel.SelectSettingsSectionCommand.ExecuteAsync("pending-memories");

    private void OnEmbeddedGalleryOpenMapRequested(object? sender, EventArgs e)
    {
        PreparePlaceWorkspaceRoundTrip();
        OpenMapRequested?.Invoke(this, EventArgs.Empty);
    }

    private async Task ShowEmbeddedGalleryAsync(Guid? placeId = null)
    {
        var targetPlaceId = placeId.GetValueOrDefault();
        var hasTargetPlace = placeId.HasValue && targetPlaceId != Guid.Empty;
        if (hasTargetPlace)
        {
            _embeddedGalleryPage.ViewModel.RequestPlaceFilter(targetPlaceId);
        }

        _isEmbeddedGalleryVisible = true;
        PlaceHost.Visibility = Visibility.Collapsed;
        EmbeddedGalleryHost.Visibility = Visibility.Visible;
        PlaceWorkspaceTabIndicator.BorderThickness = new Thickness(0);
        GalleryWorkspaceTabIndicator.BorderThickness = new Thickness(0, 0, 0, 2);
        PlaceWorkspaceTabButton.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
        GalleryWorkspaceTabButton.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        AutomationProperties.SetName(PlaceWorkspaceTabButton, "장소관리 열기");
        AutomationProperties.SetName(GalleryWorkspaceTabButton, "현재 탭: 사진첩");

        var invalidationGeneration = Volatile.Read(ref _pendingEmbeddedGalleryInvalidationGeneration);
        var requiresInvalidationRefresh = _isEmbeddedGalleryActivated
                                          && invalidationGeneration > _lastAppliedEmbeddedGalleryInvalidationGeneration;
        if (!_isEmbeddedGalleryActivated || hasTargetPlace || requiresInvalidationRefresh)
        {
            if (requiresInvalidationRefresh && !hasTargetPlace)
            {
                await _embeddedGalleryPage.ReloadPreservingFocusAsync();
            }
            else
            {
                await _embeddedGalleryPage.ViewModel.LoadCommand.ExecuteAsync(null);
            }

            _isEmbeddedGalleryActivated = true;
            _lastAppliedEmbeddedGalleryInvalidationGeneration = Math.Max(
                _lastAppliedEmbeddedGalleryInvalidationGeneration,
                invalidationGeneration);
        }
    }

    private void ShowPlaceWorkspace()
    {
        _isEmbeddedGalleryVisible = false;
        EmbeddedGalleryHost.Visibility = Visibility.Collapsed;
        PlaceHost.Visibility = Visibility.Visible;
        GalleryWorkspaceTabIndicator.BorderThickness = new Thickness(0);
        PlaceWorkspaceTabIndicator.BorderThickness = new Thickness(0, 0, 0, 2);
        GalleryWorkspaceTabButton.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
        PlaceWorkspaceTabButton.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        AutomationProperties.SetName(GalleryWorkspaceTabButton, "설정 안의 사진첩 열기");
        AutomationProperties.SetName(PlaceWorkspaceTabButton, "현재 탭: 장소관리");
    }

    private void SyncNavigationSelection()
    {
        var item = ViewModel.SelectedSettingsSection switch
        {
            SettingsSection.PhotoManagement => PhotoRegistrationMenuItem,
            SettingsSection.PendingMemories => PendingMemoriesMenuItem,
            SettingsSection.Places => PlacesMenuItem,
            SettingsSection.Tags => TagsMenuItem,
            SettingsSection.HomeLocation => HomeLocationMenuItem,
            SettingsSection.AutoTags => AutoTagsMenuItem,
            SettingsSection.PhotoExport => PhotoExportMenuItem,
            SettingsSection.PreviewCache => PreviewCacheMenuItem,
            SettingsSection.Reset => ResetMenuItem,
            SettingsSection.AppInfo => AppInfoMenuItem,
            _ => PhotoRegistrationMenuItem,
        };

        _syncingNavigation = true;
        try
        {
            SettingsNavigation.SelectedItem = item;
        }
        finally
        {
            _syncingNavigation = false;
        }
    }

    private void SettingsPage_OnSizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyResponsiveNavigation(e.NewSize.Width);

    private void ApplyResponsiveNavigation(double width)
    {
        SettingsNavigation.PaneDisplayMode = width < 800
            ? NavigationViewPaneDisplayMode.Top
            : NavigationViewPaneDisplayMode.Left;
    }
}
