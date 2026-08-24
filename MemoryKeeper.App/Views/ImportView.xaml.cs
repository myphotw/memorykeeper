using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MemoryKeeper.App.ViewModels;

namespace MemoryKeeper.App.Views;

public sealed partial class ImportView : UserControl
{
    public ImportViewModel ViewModel { get; }

    public ImportView(ImportViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += (_, _) => ViewModel.HostXamlRoot = XamlRoot;
    }

    public void ConfigureEmbedded(bool isEmbedded)
    {
        StoragePathPanel.Visibility = isEmbedded ? Visibility.Collapsed : Visibility.Visible;
        SourceFolderPanel.Visibility = isEmbedded ? Visibility.Collapsed : Visibility.Visible;
    }
}
