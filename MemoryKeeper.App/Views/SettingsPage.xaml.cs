using MemoryKeeper.App.ViewModels;
using MemoryKeeper.Application.DTOs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MemoryKeeper.App.Views;

public sealed partial class SettingsPage : Page
{
    private readonly TagManagementPage _tagPage;

    public SettingsViewModel ViewModel { get; }

    public SettingsPage(SettingsViewModel viewModel, TagManagementPage tagPage)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        _tagPage = tagPage;
        InitializeComponent();
        TagHost.Content = _tagPage;
        ViewModel.TagsSectionOpened += OnTagsSectionOpened;
        Loaded += OnLoaded;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
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
        ViewModel.HostXamlRoot = XamlRoot;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.HostXamlRoot = XamlRoot;
        // MainWindow calls LoadCommand with the target section; avoid overwriting with null.
    }

    private async void OnTagsSectionOpened(object? sender, EventArgs e)
    {
        await _tagPage.ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void OnTagBackRequested(object? sender, EventArgs e) =>
        ViewModel.GoBackCommand.Execute(null);

    private void HomeSuggestion_OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PlaceSuggestionDto suggestion)
        {
            _ = ViewModel.SelectHomeSuggestionCommand.ExecuteAsync(suggestion);
        }
    }
}
