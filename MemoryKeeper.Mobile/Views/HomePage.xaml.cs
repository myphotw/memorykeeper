using MemoryKeeper.Mobile.Diagnostics;
using MemoryKeeper.Mobile.Models;
using MemoryKeeper.Mobile.ViewModels;

namespace MemoryKeeper.Mobile.Views;

public partial class HomePage : ContentPage
{
    private const double TileSpacing = 2;

    public HomePage(HomeViewModel viewModel)
    {
        MobileStartupCheckpoint.Record("HOME-PAGE");
        InitializeComponent();
        ViewModel = viewModel;
        BindingContext = viewModel;
        ViewModel.AddPhotosRequested += OnAddPhotosRequested;
        ViewModel.PlaceTreeFocusRequested += OnPlaceTreeFocusRequested;
        MobileStartupCheckpoint.Record("HOME-PAGE-DONE");
    }

    public HomeViewModel ViewModel { get; }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = InitializeSafelyAsync();
    }

    protected override void OnDisappearing()
    {
        ViewModel.CancelPendingRequests();
        base.OnDisappearing();
    }

    protected override bool OnBackButtonPressed() =>
        ViewModel.HandleBackRequested() || base.OnBackButtonPressed();

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width <= 0)
        {
            return;
        }

        var columns = width >= 1000 ? 7 : width >= 700 ? 5 : 3;
        GalleryItemsLayout.Span = columns;
        var tileSize = Math.Max(1, (width - (columns - 1) * TileSpacing) / columns);
        ViewModel.SetTileSize(tileSize);
    }

    private async void OnRemainingItemsThresholdReached(object? sender, EventArgs e)
    {
        try
        {
            await ViewModel.LoadMoreAsync();
        }
        catch (Exception exception)
        {
            ViewModel.HandleUnexpectedLoadFailure(exception);
        }
    }

    private async void OnAddPhotosRequested(object? sender, EventArgs e)
    {
        await DisplayAlert("사진 추가", "사진 추가 기능은 준비 중입니다.", "확인");
    }

    private void OnPlaceTreeFocusRequested(MobileGalleryTreeNode node) =>
        Dispatcher.Dispatch(() => PlaceTreeCollection.ScrollTo(
            node,
            position: ScrollToPosition.Center,
            animate: false));

    private async Task InitializeSafelyAsync()
    {
        try
        {
            await ViewModel.InitializeAsync();
        }
        catch (Exception exception)
        {
            ViewModel.HandleUnexpectedLoadFailure(exception);
        }
    }
}
