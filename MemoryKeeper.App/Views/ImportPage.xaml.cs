using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using MemoryKeeper.App.ViewModels;

namespace MemoryKeeper.App.Views;

public sealed partial class ImportPage : Page
{
    public ImportViewModel ViewModel { get; }

    public ImportPage(ImportViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += (_, _) => ViewModel.HostXamlRoot = XamlRoot;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.HostXamlRoot = XamlRoot;
    }
}