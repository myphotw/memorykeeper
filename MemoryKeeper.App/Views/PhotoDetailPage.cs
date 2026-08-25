using MemoryKeeper.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace MemoryKeeper.App.Views;

public sealed class PhotoDetailPage : Page
{
    public PhotoDetailViewModel ViewModel { get; }

    public PhotoDetailPage(PhotoDetailView view)
    {
        ViewModel = view.ViewModel;
        Content = view;
    }
}
