using System.Diagnostics;
using MemoryKeeper.Mobile.Controls;
using MemoryKeeper.Mobile.Diagnostics;
using MemoryKeeper.Mobile.Models;
using MemoryKeeper.Mobile.ViewModels;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;

namespace MemoryKeeper.Mobile.Views;

public partial class HomePage : ContentPage
{
    private const double TileSpacing = 2;
    private static readonly TimeSpan MinimumStartupDisplayDuration = TimeSpan.FromMilliseconds(1500);
    private const uint StartupFadeDurationMilliseconds = 200;

    private long _startupDisplayStartedAt;
    private bool _startupOverlayDismissed;
    private bool _isInitializing;

    public HomePage(HomeViewModel viewModel)
    {
        MobileStartupCheckpoint.Record("HOME-PAGE");
        InitializeComponent();
        ViewModel = viewModel;
        BindingContext = viewModel;
        ViewModel.PlaceTreeFocusRequested += OnPlaceTreeFocusRequested;
        MobileStartupCheckpoint.Record("HOME-PAGE-DONE");
    }

    public HomeViewModel ViewModel { get; }

    private async void ShareDiagnostics_OnClicked(object? sender, EventArgs e)
    {
        try
        {
            var snapshot = MobileDiagnosticLog.Current?.CreateShareSnapshot(FileSystem.CacheDirectory);
            if (snapshot is null)
            {
                await DisplayAlert("진단 로그", "공유할 진단 로그가 없습니다.", "확인");
                return;
            }

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "MemoryKeeper 진단 로그 공유",
                File = new ShareFile(snapshot),
            });
        }
        catch
        {
            await DisplayAlert("진단 로그", "진단 로그를 공유할 수 없습니다. 다시 시도해 주세요.", "확인");
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (!_isInitializing)
        {
            _ = InitializeSafelyAsync();
        }
    }

    protected override void OnDisappearing()
    {
        ViewerVideoSurface.PausePlayback();
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

    private void OnPlaceTreeFocusRequested(MobileGalleryTreeNode node) =>
        Dispatcher.Dispatch(() => PlaceTreeCollection.ScrollTo(
            node,
            position: ScrollToPosition.Center,
            animate: false));

    private void OnViewerPreviewLoaded(object? sender, EventArgs e) =>
        ViewModel.MarkViewerPreviewReady();

    private void OnViewerPreviewFailed(object? sender, EventArgs e) =>
        ViewModel.MarkViewerPreviewFailed();

    private void OnViewerPreviousRequested(object? sender, EventArgs e) =>
        _ = ViewModel.ShowPreviousViewerItemAsync();

    private void OnViewerNextRequested(object? sender, EventArgs e) =>
        _ = ViewModel.ShowNextViewerItemAsync();

    private void OnViewerVideoPlaybackStateChanged(
        object? sender,
        MobileVideoPlaybackStateChangedEventArgs e) =>
        ViewModel.ApplyViewerVideoState(e);

    private async Task InitializeSafelyAsync()
    {
        _isInitializing = true;
        if (_startupDisplayStartedAt == 0)
        {
            _startupDisplayStartedAt = Stopwatch.GetTimestamp();
        }

        try
        {
            await ViewModel.InitializeAsync();
        }
        catch (Exception exception)
        {
            ViewModel.HandleUnexpectedLoadFailure(exception);
        }
        finally
        {
            _isInitializing = false;
            await DismissStartupOverlayAsync(skipMinimumDuration: ViewModel.IsErrorStateVisible);
        }
    }

    private async Task DismissStartupOverlayAsync(bool skipMinimumDuration)
    {
        if (_startupOverlayDismissed)
        {
            return;
        }

        _startupOverlayDismissed = true;
        try
        {
            if (!skipMinimumDuration)
            {
                var elapsed = Stopwatch.GetElapsedTime(_startupDisplayStartedAt);
                var remaining = MinimumStartupDisplayDuration - elapsed;
                if (remaining > TimeSpan.Zero)
                {
                    await Task.Delay(remaining);
                }

                await StartupLoadingOverlay.FadeTo(
                    0,
                    StartupFadeDurationMilliseconds,
                    Easing.CubicOut);
            }
        }
        finally
        {
            StartupLoadingOverlay.IsVisible = false;
        }
    }
}
