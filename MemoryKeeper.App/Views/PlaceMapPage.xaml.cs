using MemoryKeeper.App.Maps.Google;
using MemoryKeeper.App.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MemoryKeeper.App.Views;

public sealed partial class PlaceMapPage : Page
{
    private readonly ILoggerFactory _loggerFactory;
    private GoogleMapController? _mapController;

    public PlaceMapViewModel ViewModel { get; }

    public PlaceMapPage(PlaceMapViewModel viewModel, ILoggerFactory loggerFactory)
    {
        ViewModel = viewModel;
        _loggerFactory = loggerFactory;
        InitializeComponent();
    }

    private async void PlaceMapPage_OnLoaded(object sender, RoutedEventArgs e)
    {
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

    private async void PlaceMapPage_OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.DetachMap();
        if (_mapController is not null)
        {
            await _mapController.DisposeAsync();
            _mapController = null;
        }
    }
}
