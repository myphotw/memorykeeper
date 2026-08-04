using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using MemoryKeeper.App.ViewModels;

namespace MemoryKeeper.App.Views;

public sealed partial class StorageManagementPage : Page
{
    public StorageManagementViewModel ViewModel { get; }

    public StorageManagementPage(StorageManagementViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadCommand.ExecuteAsync(null);
    }
}
