using MemoryKeeper.App.Maps.Google;
using MemoryKeeper.App.ViewModels;
using MemoryKeeper.Application.DTOs;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MemoryKeeper.App.Views;

public sealed partial class PlaceManagementView : UserControl
{
    private readonly ILoggerFactory _loggerFactory;
    private GoogleMapController? _mapController;

    public PlaceManagementViewModel ViewModel { get; }

    public bool AutoActivate { get; set; }

    public PlaceManagementView(PlaceManagementViewModel viewModel, ILoggerFactory loggerFactory)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        _loggerFactory = loggerFactory;
        InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ViewModel.HasPlaceSuggestions) && ViewModel.HasPlaceSuggestions)
        {
            DispatcherQueue.TryEnqueue(() =>
                PlaceSuggestionsPanel.StartBringIntoView(new BringIntoViewOptions
                {
                    AnimationDesired = true,
                    VerticalOffset = 24
                }));
        }
    }

    private async void PlaceManagementView_OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.HostXamlRoot = XamlRoot;

        if (AutoActivate)
        {
            await ActivateAsync();
        }
    }

    public async Task ActivateAsync()
    {
        ViewModel.HostXamlRoot = XamlRoot;
        if (_mapController is not null)
        {
            return;
        }

        _mapController = new GoogleMapController(
            MapWebView,
            _loggerFactory.CreateLogger<GoogleMapController>());
        ViewModel.AttachMap(_mapController);
        await ViewModel.InitializeMapCommand.ExecuteAsync(null);
    }

    private async void PlaceManagementView_OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.DetachMap();
        if (_mapController is not null)
        {
            await _mapController.DisposeAsync();
            _mapController = null;
        }
    }

    private async void PlaceSuggestion_OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PlaceSuggestionDto suggestion)
        {
            await ViewModel.SelectSuggestionCommand.ExecuteAsync(suggestion);
        }
    }

    private void FavoritePlace_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: PlaceDto place })
        {
            ViewModel.SelectFavoriteCommand.Execute(place);
        }
    }

    private void RecentPlace_OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PlaceDto place)
        {
            ViewModel.SelectRecentCommand.Execute(place);
        }
    }
}
