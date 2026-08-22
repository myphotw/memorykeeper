using MemoryKeeper.App.Maps.Google;
using MemoryKeeper.App.Models;
using MemoryKeeper.App.Services;
using MemoryKeeper.App.ViewModels;
using MemoryKeeper.Application.Layout;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace MemoryKeeper.App.Views;

public sealed partial class VisitRecordPage : Page
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly IResponsiveLayoutService _responsiveLayout;
    private GoogleMapController? _mapController;
    private VisitRecordPlaceItem? _selectedPlaceSubscription;
    private readonly List<VisitPreviewItem> _previewSubscriptions = [];
    private CancellationTokenSource? _layoutDebounceCts;
    private int _activateRequestId;

    public event EventHandler? OpenImportRequested;

    public VisitRecordViewModel ViewModel { get; }

    public VisitRecordPage(
        VisitRecordViewModel viewModel,
        ILoggerFactory loggerFactory,
        IResponsiveLayoutService responsiveLayout)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        _loggerFactory = loggerFactory;
        _responsiveLayout = responsiveLayout;
        InitializeComponent();
        _responsiveLayout.BreakpointChanged += OnBreakpointChanged;
        Loaded += (_, _) =>
        {
            ApplyResponsiveLayout(_responsiveLayout.CurrentBreakpoint);
            PositionSearchDropdowns();
        };
        SizeChanged += (_, e) =>
        {
            ApplyResponsiveLayout(_responsiveLayout.CurrentBreakpoint);
            PositionSearchDropdowns();
            if (_mapController is not null && e.NewSize.Width >= 32 && e.NewSize.Height >= 32)
            {
                _ = DebouncedNotifyLayoutAsync();
            }
        };
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        RefreshSelectedPlaceCard();
        RefreshPreviewStrip();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ViewModel.IsSuggestionOpen) or nameof(ViewModel.IsRecentOpen))
        {
            DispatcherQueue.TryEnqueue(PositionSearchDropdowns);
        }
        else if (e.PropertyName is nameof(ViewModel.SelectedPlace))
        {
            DispatcherQueue.TryEnqueue(RefreshSelectedPlaceCard);
        }
        else if (e.PropertyName is nameof(ViewModel.PreviewPhotos))
        {
            DispatcherQueue.TryEnqueue(RefreshPreviewStrip);
        }
    }

    private void PositionSearchDropdowns()
    {
        // Align dropdown under the fixed search row without affecting layout.
        SearchRow.UpdateLayout();
        var transform = SearchRow.TransformToVisual(this);
        var point = transform.TransformPoint(new Windows.Foundation.Point(0, SearchRow.ActualHeight + 6));
        SuggestionsPanel.Margin = new Thickness(point.X, point.Y, 0, 0);
        RecentPanel.Margin = new Thickness(point.X, point.Y, 0, 0);
    }

    private void RefreshSelectedPlaceCard()
    {
        if (_selectedPlaceSubscription is not null)
        {
            _selectedPlaceSubscription.PropertyChanged -= SelectedPlace_OnPropertyChanged;
            _selectedPlaceSubscription = null;
        }

        var place = ViewModel.SelectedPlace;
        SelectedPlaceCard.Visibility = place is null ? Visibility.Collapsed : Visibility.Visible;
        if (place is not null)
        {
            _selectedPlaceSubscription = place;
            place.PropertyChanged += SelectedPlace_OnPropertyChanged;
        }

        RefreshPreviewStrip();
        UpdateSelectedPlaceThumbnail();
    }

    private void SelectedPlace_OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(VisitRecordPlaceItem.ThumbnailImage))
        {
            DispatcherQueue.TryEnqueue(UpdateSelectedPlaceThumbnail);
        }
    }

    private void RefreshPreviewStrip()
    {
        foreach (var preview in _previewSubscriptions)
        {
            preview.PropertyChanged -= PreviewItem_OnPropertyChanged;
        }

        _previewSubscriptions.Clear();
        var previews = ViewModel.PreviewPhotos.Take(4).ToList();
        PreviewFourHost.ItemsSource = previews;
        foreach (var preview in previews)
        {
            preview.PropertyChanged += PreviewItem_OnPropertyChanged;
            _previewSubscriptions.Add(preview);
        }

        UpdateSelectedPlaceThumbnail();
    }

    private void PreviewItem_OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(VisitPreviewItem.ThumbnailImage))
        {
            DispatcherQueue.TryEnqueue(UpdateSelectedPlaceThumbnail);
        }
    }

    private void UpdateSelectedPlaceThumbnail()
    {
        // Year-scoped ForYear() copies never get LoadTimelineThumbnailsAsync —
        // fall back to the first preview thumb (loaded in LoadPreviewAsync).
        var source = ViewModel.SelectedPlace?.ThumbnailImage
            ?? ViewModel.PreviewPhotos.FirstOrDefault(photo => photo.ThumbnailImage is not null)?.ThumbnailImage;
        SelectedPlaceThumbImage.Source = source;
    }

    private void OnBreakpointChanged(object? sender, LayoutBreakpointChangedEventArgs e) =>
        DispatcherQueue.TryEnqueue(() => ApplyResponsiveLayout(e.Current));

    private void ApplyResponsiveLayout(LayoutBreakpoint breakpoint)
    {
        ToolbarLarge.Visibility = breakpoint == LayoutBreakpoint.Large
            ? Visibility.Visible
            : Visibility.Collapsed;
        ToolbarMedium.Visibility = breakpoint == LayoutBreakpoint.Medium
            ? Visibility.Visible
            : Visibility.Collapsed;
        ToolbarSmall.Visibility = breakpoint == LayoutBreakpoint.Small
            ? Visibility.Visible
            : Visibility.Collapsed;

        var (left, right) = breakpoint switch
        {
            LayoutBreakpoint.Small => (0.36, 0.64),
            LayoutBreakpoint.Medium => (0.30, 0.70),
            _ => (0.28, 0.72)
        };

        BodyGrid.ColumnDefinitions[0].Width = new GridLength(left, GridUnitType.Star);
        BodyGrid.ColumnDefinitions[1].Width = new GridLength(right, GridUnitType.Star);
        Grid.SetColumn(MapPane, 1);
    }

    private void VisitRecordPage_OnLoaded(object sender, RoutedEventArgs e)
    {
        _loggerFactory.CreateLogger<VisitRecordPage>().LogInformation(
            "VisitRecordPage Loaded. Size={Width:0}x{Height:0} WebView={WebW:0}x{WebH:0}",
            ActualWidth,
            ActualHeight,
            MapWebView.ActualWidth,
            MapWebView.ActualHeight);
        EnsureMapController();
        ApplyResponsiveLayout(_responsiveLayout.CurrentBreakpoint);
    }

    private void VisitRecordPage_OnUnloaded(object sender, RoutedEventArgs e)
    {
        _loggerFactory.CreateLogger<VisitRecordPage>().LogInformation("VisitRecordPage Unloaded.");
        // Keep map/controller alive — page instances are cached by NavigationService.
    }

    /// <summary>
    /// Called from MainWindow for every visit-map navigation (HOME and TRAVEL_RECORD).
    /// </summary>
    public async Task ActivateAsync(
        int navigationGeneration,
        VisitMapNavigationSource source,
        bool reloadData)
    {
        var requestId = Interlocked.Increment(ref _activateRequestId);
        var logger = _loggerFactory.CreateLogger<VisitRecordPage>();
        logger.LogInformation(
            "VisitRecordPage Activate start. Source={Source} Gen={Gen} Reload={Reload} RequestId={RequestId}",
            source,
            navigationGeneration,
            reloadData,
            requestId);

        EnsureMapController();
        await WaitUntilMapHostSizedAsync();

        if (requestId != _activateRequestId)
        {
            logger.LogInformation("stale async result ignored (page activate). RequestId={RequestId}", requestId);
            return;
        }

        logger.LogInformation(
            "WebView actual size ready. Size={Width:0}x{Height:0} Visible={Visible}",
            MapWebView.ActualWidth,
            MapWebView.ActualHeight,
            MapWebView.Visibility);

        await ViewModel.ActivateVisitSurfaceAsync(navigationGeneration, source, reloadData);

        logger.LogInformation("VisitRecordPage Activate done. Gen={Gen} RequestId={RequestId}", navigationGeneration, requestId);
    }

    private void EnsureMapController()
    {
        if (_mapController is not null)
        {
            return;
        }

        _mapController = new GoogleMapController(
            MapWebView,
            _loggerFactory.CreateLogger<GoogleMapController>());
        ViewModel.AttachMap(_mapController);
    }

    private async Task WaitUntilMapHostSizedAsync()
    {
        if (IsMapHostSized())
        {
            return;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (IsMapHostSized())
            {
                tcs.TrySetResult();
            }
        }

        MapWebView.SizeChanged += OnSizeChanged;
        SizeChanged += OnSizeChanged;
        try
        {
            UpdateLayout();
            MapWebView.UpdateLayout();
            if (IsMapHostSized())
            {
                return;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var reg = cts.Token.Register(() => tcs.TrySetResult());
            await tcs.Task;
        }
        finally
        {
            MapWebView.SizeChanged -= OnSizeChanged;
            SizeChanged -= OnSizeChanged;
        }
    }

    private bool IsMapHostSized() =>
        MapWebView.XamlRoot is not null
        && MapWebView.Visibility == Visibility.Visible
        && MapWebView.ActualWidth >= 32
        && MapWebView.ActualHeight >= 32;

    private async Task DebouncedNotifyLayoutAsync()
    {
        _layoutDebounceCts?.Cancel();
        _layoutDebounceCts?.Dispose();
        _layoutDebounceCts = new CancellationTokenSource();
        var token = _layoutDebounceCts.Token;
        try
        {
            await Task.Delay(120, token);
            var width = MapWebView.ActualWidth;
            var height = MapWebView.ActualHeight;
            _loggerFactory.CreateLogger<VisitRecordPage>().LogInformation(
                "layout debounce. WebView={Width:0}x{Height:0} CardVisible={Card}",
                width,
                height,
                SelectedPlaceCard.Visibility == Visibility.Visible);

            if (width < 32 || height < 32)
            {
                return;
            }

            if (_mapController is not null)
            {
                await _mapController.NotifyLayoutAsync(token);
            }

            await ViewModel.FlushPendingMapFocusAsync();
        }
        catch (OperationCanceledException)
        {
            // newer layout event won
        }
    }

    private async void SearchBox_OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        e.Handled = true;
        await ViewModel.SearchCommand.ExecuteAsync(null);
    }

    private async void SearchBox_OnGotFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ViewModel.SearchText))
        {
            await ViewModel.ShowRecentCommand.ExecuteAsync(null);
        }
    }

    private void SearchBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        // Delay so a click on Recent/Suggestion ListView can complete first.
        DispatcherQueue.TryEnqueue(async () =>
        {
            await Task.Delay(180);
            if (SearchBox.FocusState != FocusState.Unfocused)
            {
                return;
            }

            var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
            if (focused is not null
                && (IsUnder(focused, RecentPanel) || IsUnder(focused, SuggestionsPanel)))
            {
                return;
            }

            ViewModel.IsRecentOpen = false;
            ViewModel.IsSuggestionOpen = false;
        });
    }

    private static bool IsUnder(DependencyObject? node, DependencyObject ancestor)
    {
        while (node is not null)
        {
            if (ReferenceEquals(node, ancestor))
            {
                return true;
            }

            node = VisualTreeHelper.GetParent(node);
        }

        return false;
    }

    private async void Suggestion_OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SearchSuggestionItem item)
        {
            await ViewModel.ApplySuggestionCommand.ExecuteAsync(item);
        }
    }

    private async void Recent_OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is string query)
        {
            await ViewModel.ApplyRecentQueryCommand.ExecuteAsync(query);
        }
    }

    private async void HierarchyExpand_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: GalleryTreeNode node })
        {
            await ViewModel.ToggleHierarchyNodeCommand.ExecuteAsync(node);
        }
    }

    private async void HierarchyNode_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: GalleryTreeNode node })
        {
            await ViewModel.SelectHierarchyNodeCommand.ExecuteAsync(node);
        }
    }

    private void YearGroupArrow_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: VisitRecordYearGroup group })
        {
            ViewModel.ToggleYearGroupCommand.Execute(group);
        }
    }

    private async void YearGroupTitle_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: VisitRecordYearGroup group })
        {
            await ViewModel.SelectYearGroupCommand.ExecuteAsync(group);
        }
    }

    private void PlaceItem_OnTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: VisitRecordPlaceItem item })
        {
            ViewModel.SelectPlaceCommand.Execute(item);
            ScrollTimelineTo(item);
        }
    }

    private void PlaceItem_OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: VisitRecordPlaceItem item })
        {
            ViewModel.HoveredPlaceId = item.PlaceId;
        }
    }

    private void PlaceItem_OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        ViewModel.HoveredPlaceId = null;
    }

    private void PlaceItem_OnRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: VisitRecordPlaceItem item })
        {
            ViewModel.SelectPlaceCommand.Execute(item);
        }
    }

    private void Preview_OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is VisitPreviewItem item)
        {
            ViewModel.OpenPreviewPhotoCommand.Execute(item);
        }
    }

    private void PreviewThumb_OnTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: VisitPreviewItem item })
        {
            ViewModel.OpenPreviewPhotoCommand.Execute(item);
        }
    }

    private void OpenSelectedPhotos_OnClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedPlace is VisitRecordPlaceItem item)
        {
            ViewModel.OpenPhotoDetailCommand.Execute(item);
        }
    }

    private void ImportPhotos_OnClick(object sender, RoutedEventArgs e) =>
        OpenImportRequested?.Invoke(this, EventArgs.Empty);

    private void YearTitle_OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        if (global::Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue("MkBrushSurfaceMuted", out var brush)
            && brush is Brush muted)
        {
            button.Background = muted;
        }
    }

    private void YearTitle_OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button button)
        {
            button.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
    }

    private void ContextOpenPhoto_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: VisitRecordPlaceItem item })
        {
            ViewModel.OpenPhotoDetailCommand.Execute(item);
        }
    }

    private void ContextChangeRep_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: VisitRecordPlaceItem item })
        {
            ViewModel.ChangeRepresentativeCommand.Execute(item);
        }
    }

    private async void ContextFavorite_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: VisitRecordPlaceItem item })
        {
            await ViewModel.ToggleFavoriteCommand.ExecuteAsync(item);
        }
    }

    private void ContextEditTags_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: VisitRecordPlaceItem item })
        {
            ViewModel.EditTagsCommand.Execute(item);
        }
    }

    private void ContextEditPlace_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: VisitRecordPlaceItem item })
        {
            ViewModel.EditPlaceCommand.Execute(item);
        }
    }

    private async void ContextDelete_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: VisitRecordPlaceItem item })
        {
            await ViewModel.DeleteFromLibraryCommand.ExecuteAsync(item);
        }
    }

    private void ScrollTimelineTo(VisitRecordPlaceItem item)
    {
        try
        {
            TimelineList.ScrollIntoView(item);
        }
        catch
        {
            // Year group virtualization may not expose place item directly.
        }
    }
}
