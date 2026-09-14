using MemoryKeeper.App.ViewModels;
using MemoryKeeper.Application.DTOs;
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
    private readonly TagManagementView _tagView;
    private readonly INavigationService _navigation;
    private readonly HashSet<SettingsSection> _activatedSections = [];
    private readonly HashSet<SettingsSection> _activatingSections = [];
    private bool _syncingNavigation;
    private bool _isLoaded;

    public SettingsViewModel ViewModel { get; }

    public event EventHandler? BackRequested;

    public void RequestPlaceSelection(Guid placeId) =>
        _placeView.ViewModel.RequestSelection(placeId);

    public SettingsPage(
        SettingsViewModel viewModel,
        PhotoManagementView photoManagementView,
        PendingMemoryView pendingView,
        PlaceManagementView placeView,
        TagManagementView tagView,
        INavigationService navigation)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        _photoManagementView = photoManagementView;
        _pendingView = pendingView;
        _placeView = placeView;
        _tagView = tagView;
        _navigation = navigation;
        InitializeComponent();

        PhotoManagementHost.Content = _photoManagementView;
        PendingHost.Content = _pendingView;
        PlaceHost.Content = _placeView;
        TagHost.Content = _tagView;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
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
                await _placeView.ViewModel.LoadCommand.ExecuteAsync(null);
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
                    await _placeView.ActivateAsync();
                    await _placeView.ViewModel.LoadCommand.ExecuteAsync(null);
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
