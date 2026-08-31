using MemoryKeeper.App.Models;
using MemoryKeeper.App.ViewModels;
using MemoryKeeper.Application.Navigation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace MemoryKeeper.App.Views;

public sealed partial class TravelRecordsDetailPage : Page
{
    private readonly INavigationService _navigation;

    public TravelRecordsDetailViewModel ViewModel { get; }

    public TravelRecordsDetailPage(
        TravelRecordsDetailViewModel viewModel,
        INavigationService navigation)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        _navigation = navigation;
        InitializeComponent();
    }

    private async void TravelRecordsDetailPage_OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshBackNavigation();
        await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void RefreshBackNavigation()
    {
        var current = _navigation.Current;
        var hasTravelFallback = current is { } entry
                                && entry.Tag == "travel-detail"
                                && entry.Kind != NavigationKind.TopLevel;
        var isVisible = _navigation.CanGoBack || hasTravelFallback;
        var label = _navigation.BackEntry?.DisplayLabel;
        label = string.IsNullOrWhiteSpace(label) ? "여행기록" : label.Trim();

        BackNavigationLabel.Text = label;
        BackNavigationButton.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(BackNavigationButton, $"{label}(으)로 돌아가기");
        AutomationProperties.SetName(BackNavigationButton, $"이전 화면: {label}");
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
