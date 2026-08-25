using MemoryKeeper.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MemoryKeeper.App.Views;

public sealed partial class PhotoManagementView : UserControl
{
    private readonly ImportView _importView;
    private bool _activated;
    private bool _isLoaded;

    public StorageManagementViewModel Storage { get; }

    public ImportViewModel Import => _importView.ViewModel;

    public PhotoManagementView(
        StorageManagementViewModel storage,
        ImportView importView)
    {
        Storage = storage;
        _importView = importView;
        DataContext = this;
        InitializeComponent();

        _importView.ConfigureEmbedded(isEmbedded: true);
        ImportHost.Content = _importView;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public async Task ActivateAsync()
    {
        if (_activated)
        {
            SyncImportSource();
            return;
        }

        _activated = true;
        await Storage.LoadCommand.ExecuteAsync(null);
        SyncImportSource();
        await Import.LoadCommand.ExecuteAsync(null);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;
        Import.HostXamlRoot = XamlRoot;
        Storage.PropertyChanged += OnStoragePropertyChanged;
        SyncImportSource();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded)
        {
            return;
        }

        _isLoaded = false;
        Storage.PropertyChanged -= OnStoragePropertyChanged;
    }

    private void OnStoragePropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(StorageManagementViewModel.PhotoRootPath))
        {
            SyncImportSource();
        }
    }

    private void SyncImportSource() => Import.SourceFolderPath = Storage.PhotoRootPath;
}
