using MemoryKeeper.App.Models;
using MemoryKeeper.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace MemoryKeeper.App.Views;

public sealed partial class FavoritesPage : Page
{
    public FavoritesViewModel ViewModel { get; }

    public FavoritesPage(FavoritesViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private void Favorites_OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is GalleryItem item)
        {
            ViewModel.OpenPhotoCommand.Execute(item);
        }
    }
}
