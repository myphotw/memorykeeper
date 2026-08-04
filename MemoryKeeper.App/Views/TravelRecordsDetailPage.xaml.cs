using MemoryKeeper.App.Models;
using MemoryKeeper.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MemoryKeeper.App.Views;

public sealed partial class TravelRecordsDetailPage : Page
{
    public TravelRecordsDetailViewModel ViewModel { get; }

    public TravelRecordsDetailPage(TravelRecordsDetailViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private async void TravelRecordsDetailPage_OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void Places_OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TravelPlaceCardItem item)
        {
            ViewModel.OpenPlaceCommand.Execute(item);
        }
    }

    private void Countries_OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TravelCountryCardItem item)
        {
            ViewModel.OpenCountryCommand.Execute(item);
        }
    }

    private void Farthest_OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TravelFarthestCardItem item)
        {
            ViewModel.OpenFarthestCommand.Execute(item);
        }
    }
}
