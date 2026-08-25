using MemoryKeeper.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MemoryKeeper.App.Views;

public sealed class PhotoManagementPage : Page
{
    private readonly PhotoManagementView _view;

    public ImportViewModel ViewModel => _view.Import;

    public PhotoManagementPage(PhotoManagementView view)
    {
        _view = view;
        Content = new Grid
        {
            Style = (Style)global::Microsoft.UI.Xaml.Application.Current.Resources["MkStandardPageContainerStyle"],
            Children =
            {
                new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = view,
                },
            },
        };

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await _view.ActivateAsync();
}
