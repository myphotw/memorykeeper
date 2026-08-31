using MemoryKeeper.App.Models;
using MemoryKeeper.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MemoryKeeper.App.Views;

public sealed partial class TravelCountryListPage : Page
{
    public TravelCountryListViewModel ViewModel { get; }

    public TravelCountryListPage(TravelCountryListViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private void TravelCountryListPage_OnLoaded(object sender, RoutedEventArgs e) =>
        ViewModel.LoadCommand.Execute(null);

    private void Countries_OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TravelForeignCountryItem item)
        {
            ViewModel.OpenCountryGalleryCommand.Execute(item);
        }
    }
}
