using MemoryKeeper.App.Models;
using MemoryKeeper.App.Services;
using MemoryKeeper.App.ViewModels;
using MemoryKeeper.Application.Layout;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using Windows.System;
using WinRT.Interop;

namespace MemoryKeeper.App.Views;

public sealed partial class PhotoViewerPage : Page
{
    private const double ClickZoneRatio = 0.20;
    private const float MinZoom = 1.0f;
    private const float MaxZoom = 8.0f;
    private const float ZoomedThreshold = 1.02f;
    private const int ChromeHideDelayMs = 3000;
    private const int FadeDurationMs = 180;
    private const int NavFadeDurationMs = 150;
    private const int SlideDurationMs = 180;
    private const double FilmStripItemWidth = 56; // 48 + margin/spacing
    private const double FilmStripScrollStep = FilmStripItemWidth * 3;
    private const double FilmStripBottomMargin = 10;
    private const double MetaAboveStripGap = 10;

    private readonly IResponsiveLayoutService _responsiveLayout;
    private readonly DispatcherTimer _chromeHideTimer;
    private bool _chromeVisible = true;
    private bool _navButtonsVisible;
    private bool _filmStripPinned;
    private bool _filmStripVisible;
    private bool _pointerInsideViewer;
    private bool _isFullscreen;
    private bool _isPanning;
    private Point _panStartPoint;
    private double _panStartHorizontal;
    private double _panStartVertical;
    private uint? _panPointerId;
    private AppWindow? _appWindow;

    public PhotoViewerViewModel ViewModel { get; }

    public PhotoViewerPage(
        PhotoViewerViewModel viewModel,
        IResponsiveLayoutService responsiveLayout)
    {
        ViewModel = viewModel;
        _responsiveLayout = responsiveLayout;
        DataContext = viewModel;
        InitializeComponent();

        _chromeHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ChromeHideDelayMs) };
        _chromeHideTimer.Tick += (_, _) => HideChrome();

        ViewModel.NavigateSlideRequested += OnNavigateSlideRequested;
        ViewModel.FilmStripUpdated += OnFilmStripUpdated;
        _responsiveLayout.BreakpointChanged += OnBreakpointChanged;
        _responsiveLayout.LayoutChanged += OnLayoutChanged;
        // Capture keys even when a child control (chrome button) has focus.
        AddHandler(KeyDownEvent, new KeyEventHandler(PhotoViewerPage_OnKeyDown), handledEventsToo: true);
        // Wheel over any overlay (side zones, chrome) still zooms.
        AddHandler(PointerWheelChangedEvent, new PointerEventHandler(OnViewerPointerWheelChanged), handledEventsToo: true);

        Loaded += (_, _) =>
        {
            UpdateClickZoneWidths();
            ApplyResponsiveLayout(_responsiveLayout.CurrentBreakpoint);
            UpdateMetaOverlayPosition();
            UpdateInteractionMode();
        };
    }

    private async void PhotoViewerPage_OnLoaded(object sender, RoutedEventArgs e)
    {
        _appWindow = GetAppWindow();
        ResetChromeTimer();
        await ViewModel.LoadCommand.ExecuteAsync(null);
        ResetZoom();
        EnsureViewerKeyboardFocus();
    }

    private void PhotoViewerPage_OnUnloaded(object sender, RoutedEventArgs e)
    {
        _chromeHideTimer.Stop();
        EndPan();
        ViewModel.NavigateSlideRequested -= OnNavigateSlideRequested;
        ViewModel.FilmStripUpdated -= OnFilmStripUpdated;
        _responsiveLayout.BreakpointChanged -= OnBreakpointChanged;
        _responsiveLayout.LayoutChanged -= OnLayoutChanged;
        ViewModel.DisposeImages();
    }

    private void OnBreakpointChanged(object? sender, LayoutBreakpointChangedEventArgs e) =>
        DispatcherQueue.TryEnqueue(() => ApplyResponsiveLayout(e.Current));

    private void OnLayoutChanged(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(UpdateMetaOverlayPosition);

    private void ApplyResponsiveLayout(LayoutBreakpoint breakpoint)
    {
        FilmStripOverlay.MaxWidth = ResponsiveLayoutRules.FilmStripMaxWidth(breakpoint);
        ViewModel.ApplyBreakpoint(breakpoint);
        UpdateMetaOverlayPosition();
    }

    private void PhotoViewerPage_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateClickZoneWidths();
        UpdateMetaOverlayPosition();
    }

    private void FilmStripOverlay_OnSizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateMetaOverlayPosition();

    private void UpdateClickZoneWidths()
    {
        var zoneWidth = RootGrid.ActualWidth * ClickZoneRatio;
        LeftClickZone.Width = zoneWidth;
        RightClickZone.Width = zoneWidth;
    }

    private void UpdateMetaOverlayPosition()
    {
        var stripHeight = FilmStripOverlay.ActualHeight > 0
            ? FilmStripOverlay.ActualHeight
            : 72;
        var bottom = _filmStripVisible || _filmStripPinned
            ? FilmStripBottomMargin + stripHeight + MetaAboveStripGap
            : FilmStripBottomMargin + 24;
        MetaOverlay.Margin = new Thickness(16, 0, 16, bottom);
        MetaOverlay.MaxWidth = Math.Max(180, RootGrid.ActualWidth * 0.42);
    }

    private void PhotoViewerPage_OnPointerEntered(object sender, PointerRoutedEventArgs e) =>
        _pointerInsideViewer = true;

    private void PhotoViewerPage_OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _pointerInsideViewer = false;
        _filmStripPinned = false;
        HideFilmStrip();
    }

    private void PhotoViewerPage_OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        _pointerInsideViewer = true;
        ShowChrome();
        ResetChromeTimer();

        var position = e.GetCurrentPoint(RootGrid).Position;
        var width = RootGrid.ActualWidth;
        var height = RootGrid.ActualHeight;

        if (position.Y > height * 0.72)
        {
            ShowFilmStrip();
        }
        else if (!_filmStripPinned)
        {
            HideFilmStrip();
        }

        if (position.X < width * 0.25 && position.Y < height * 0.72)
        {
            ShowNavButton(PreviousButton);
        }
        else if (position.X > width * 0.75 && position.Y < height * 0.72)
        {
            ShowNavButton(NextButton);
        }
        else
        {
            HideNavButtons();
        }
    }

    private void ResetChromeTimer()
    {
        _chromeHideTimer.Stop();
        _chromeHideTimer.Start();
    }

    private void ShowChrome()
    {
        if (_chromeVisible)
        {
            return;
        }

        _chromeVisible = true;
        AnimateOpacity(TopChrome, 1, FadeDurationMs);
        AnimateOpacity(MetaOverlay, 1, FadeDurationMs);
    }

    private void HideChrome()
    {
        _chromeHideTimer.Stop();
        if (!_chromeVisible)
        {
            return;
        }

        _chromeVisible = false;
        AnimateOpacity(TopChrome, 0, FadeDurationMs);
        AnimateOpacity(MetaOverlay, 0, FadeDurationMs);
        HideNavButtons();
        if (!_filmStripPinned && !_pointerInsideViewer)
        {
            HideFilmStrip();
        }
    }

    private void ShowFilmStrip()
    {
        FilmStripOverlay.IsHitTestVisible = true;
        _filmStripVisible = true;
        UpdateMetaOverlayPosition();
        if (FilmStripOverlay.Opacity > 0.9)
        {
            return;
        }

        AnimateOpacity(FilmStripOverlay, 1, FadeDurationMs);
    }

    private void HideFilmStrip()
    {
        _filmStripVisible = false;
        UpdateMetaOverlayPosition();
        if (FilmStripOverlay.Opacity < 0.05)
        {
            FilmStripOverlay.IsHitTestVisible = false;
            return;
        }

        AnimateOpacity(FilmStripOverlay, 0, FadeDurationMs);
        FilmStripOverlay.IsHitTestVisible = false;
    }

    private void FilmStripOverlay_OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _filmStripPinned = true;
        ShowFilmStrip();
        ResetChromeTimer();
    }

    private void FilmStripOverlay_OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _filmStripPinned = false;
        // Fade out only when pointer leaves the strip AND is not still over the bottom hover band.
        // Full leave of the viewer is handled by RootGrid PointerExited.
    }

    public float GetZoomFactor() => PhotoScrollViewer.ZoomFactor;

    private void ShowNavButton(Button button)
    {
        if (button.Opacity > 0.9)
        {
            return;
        }

        _navButtonsVisible = true;
        AnimateOpacity(button, 0.75, NavFadeDurationMs);
    }

    private void HideNavButtons()
    {
        if (!_navButtonsVisible)
        {
            return;
        }

        _navButtonsVisible = false;
        AnimateOpacity(PreviousButton, 0, NavFadeDurationMs);
        AnimateOpacity(NextButton, 0, NavFadeDurationMs);
    }

    private void NavButton_OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button button)
        {
            AnimateOpacity(button, 0.95, NavFadeDurationMs);
        }
    }

    private void NavButton_OnPointerExited(object sender, PointerRoutedEventArgs e) =>
        HideNavButtons();

    private static void AnimateOpacity(UIElement element, double target, int durationMs)
    {
        var animation = new DoubleAnimation
        {
            To = target,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, "Opacity");
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    private void OnFilmStripUpdated(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(async () =>
        {
            await Task.Delay(16);
            CenterFilmStripOnCurrent();
            // Do not auto-show strip on photo change — hover only.
            if (_pointerInsideViewer && (_filmStripPinned || _filmStripVisible))
            {
                ShowFilmStrip();
            }
        });

    private void CenterFilmStripOnCurrent()
    {
        var current = ViewModel.CurrentFilmStripItem;
        if (current is null)
        {
            return;
        }

        var index = ViewModel.FilmStripItems.ToList().IndexOf(current);
        if (index < 0)
        {
            return;
        }

        var viewport = FilmStripScroller.ActualWidth;
        var contentWidth = ViewModel.FilmStripItems.Count * FilmStripItemWidth;
        var target = (index * FilmStripItemWidth) + (FilmStripItemWidth / 2.0) - (viewport / 2.0);
        target = Math.Clamp(target, 0, Math.Max(0, contentWidth - viewport));
        FilmStripScroller.ChangeView(target, null, null, disableAnimation: true);
    }

    private void FilmStripScrollPrev_OnClick(object sender, RoutedEventArgs e)
    {
        var target = Math.Max(0, FilmStripScroller.HorizontalOffset - FilmStripScrollStep);
        FilmStripScroller.ChangeView(target, null, null);
        ResetChromeTimer();
    }

    private void FilmStripScrollNext_OnClick(object sender, RoutedEventArgs e)
    {
        var max = Math.Max(0, FilmStripScroller.ExtentWidth - FilmStripScroller.ViewportWidth);
        var target = Math.Min(max, FilmStripScroller.HorizontalOffset + FilmStripScrollStep);
        FilmStripScroller.ChangeView(target, null, null);
        ResetChromeTimer();
    }

    private async void FilmStripItem_OnTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: FilmStripItem item })
        {
            ResetZoom();
            await ViewModel.SelectFilmStripItemCommand.ExecuteAsync(item);
            e.Handled = true;
        }
    }

    private void OnNavigateSlideRequested(object? sender, int direction)
    {
        ResetZoom();
        var from = direction < 0 ? -40 : 40;
        PhotoSlideTransform.TranslateX = from;
        var animation = new DoubleAnimation
        {
            From = from,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(SlideDurationMs),
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(animation, PhotoSlideTransform);
        Storyboard.SetTargetProperty(animation, "TranslateX");
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    private async void LeftClickZone_OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (IsZoomed)
        {
            return;
        }

        if (ViewModel.CanGoPrevious)
        {
            await ViewModel.GoPreviousCommand.ExecuteAsync(null);
        }

        EnsureViewerKeyboardFocus();
    }

    private async void RightClickZone_OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (IsZoomed)
        {
            return;
        }

        if (ViewModel.CanGoNext)
        {
            await ViewModel.GoNextCommand.ExecuteAsync(null);
        }

        EnsureViewerKeyboardFocus();
    }

    private async void PreviousButton_OnClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.GoPreviousCommand.ExecuteAsync(null);
        EnsureViewerKeyboardFocus();
    }

    private async void NextButton_OnClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.GoNextCommand.ExecuteAsync(null);
        EnsureViewerKeyboardFocus();
    }

    private void PhotoScrollViewer_OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (IsZoomed)
        {
            ResetZoom();
        }
        else
        {
            ToggleFullscreen();
        }
    }

    private void PhotoScrollViewer_OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs e) =>
        UpdateInteractionMode();

    private void OnViewerPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(PhotoScrollViewer).Properties.MouseWheelDelta;
        if (delta == 0)
        {
            return;
        }

        var step = e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control) ? 0.25f : 0.15f;
        var next = Math.Clamp(PhotoScrollViewer.ZoomFactor + (delta > 0 ? step : -step), MinZoom, MaxZoom);
        // Zoom toward pointer position.
        var point = e.GetCurrentPoint(PhotoScrollViewer).Position;
        var viewportWidth = PhotoScrollViewer.ViewportWidth;
        var viewportHeight = PhotoScrollViewer.ViewportHeight;
        if (viewportWidth <= 0 || viewportHeight <= 0)
        {
            PhotoScrollViewer.ChangeView(null, null, next);
        }
        else
        {
            var zoom = PhotoScrollViewer.ZoomFactor;
            var offsetX = PhotoScrollViewer.HorizontalOffset + point.X;
            var offsetY = PhotoScrollViewer.VerticalOffset + point.Y;
            var ratio = next / Math.Max(zoom, 0.0001f);
            var newOffsetX = (offsetX * ratio) - point.X;
            var newOffsetY = (offsetY * ratio) - point.Y;
            PhotoScrollViewer.ChangeView(newOffsetX, newOffsetY, next);
        }

        e.Handled = true;
        UpdateInteractionMode();
        ResetChromeTimer();
    }

    private void PhotoScrollViewer_OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!IsZoomed || !e.GetCurrentPoint(PhotoScrollViewer).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _isPanning = true;
        _panPointerId = e.Pointer.PointerId;
        _panStartPoint = e.GetCurrentPoint(PhotoScrollViewer).Position;
        _panStartHorizontal = PhotoScrollViewer.HorizontalOffset;
        _panStartVertical = PhotoScrollViewer.VerticalOffset;
        PhotoScrollViewer.CapturePointer(e.Pointer);
        PhotoScrollViewer.ManipulationMode = ManipulationModes.None;
        e.Handled = true;
    }

    private void PhotoScrollViewer_OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isPanning || _panPointerId != e.Pointer.PointerId)
        {
            return;
        }

        var current = e.GetCurrentPoint(PhotoScrollViewer).Position;
        var dx = current.X - _panStartPoint.X;
        var dy = current.Y - _panStartPoint.Y;
        // Drag content with the pointer (grab-and-move).
        PhotoScrollViewer.ChangeView(
            _panStartHorizontal - dx,
            _panStartVertical - dy,
            null,
            disableAnimation: true);
        e.Handled = true;
        ResetChromeTimer();
    }

    private void PhotoScrollViewer_OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_panPointerId is uint id && e.Pointer.PointerId == id)
        {
            EndPan();
            e.Handled = true;
        }
    }

    private void EndPan()
    {
        if (!_isPanning)
        {
            return;
        }

        _isPanning = false;
        _panPointerId = null;
        PhotoScrollViewer.ReleasePointerCaptures();
    }

    private bool IsZoomed => PhotoScrollViewer.ZoomFactor > ZoomedThreshold;

    private void ResetZoom()
    {
        EndPan();
        PhotoScrollViewer.ChangeView(0, 0, MinZoom, disableAnimation: true);
        UpdateInteractionMode();
    }

    private void UpdateInteractionMode()
    {
        var zoomed = IsZoomed;
        // When zoomed, side zones must not steal drag; use buttons/keys for prev/next.
        LeftClickZone.IsHitTestVisible = !zoomed;
        RightClickZone.IsHitTestVisible = !zoomed;
        PhotoScrollViewer.ManipulationMode = zoomed
            ? ManipulationModes.TranslateX | ManipulationModes.TranslateY
            : ManipulationModes.System;
    }

    private void PhotoViewerPage_OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (HandleNavigationKey(e.Key))
        {
            e.Handled = true;
        }
    }

    private bool HandleNavigationKey(VirtualKey key)
    {
        switch (key)
        {
            case VirtualKey.Left:
                if (ViewModel.CanGoPrevious)
                {
                    _ = ViewModel.GoPreviousCommand.ExecuteAsync(null);
                }

                EnsureViewerKeyboardFocus();
                return true;
            case VirtualKey.Right:
                if (ViewModel.CanGoNext)
                {
                    _ = ViewModel.GoNextCommand.ExecuteAsync(null);
                }

                EnsureViewerKeyboardFocus();
                return true;
            case VirtualKey.Escape:
                if (IsZoomed)
                {
                    ResetZoom();
                }
                else if (_isFullscreen)
                {
                    ToggleFullscreen();
                }
                else
                {
                    ViewModel.GoBackCommand.Execute(null);
                }

                return true;
            case VirtualKey.F11:
                ToggleFullscreen();
                return true;
            default:
                return false;
        }
    }

    /// <summary>Used by MainWindow when the viewer page does not hold keyboard focus.</summary>
    public bool TryHandleKey(VirtualKey key) => HandleNavigationKey(key);

    private void EnsureViewerKeyboardFocus()
    {
        if (!Focus(FocusState.Programmatic))
        {
            PhotoScrollViewer.Focus(FocusState.Programmatic);
        }
    }

    private void ToggleFullscreen()
    {
        if (_appWindow is null)
        {
            return;
        }

        _isFullscreen = !_isFullscreen;
        if (_isFullscreen)
        {
            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.SetBorderAndTitleBar(false, false);
                presenter.Maximize();
            }
        }
        else if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(true, true);
            presenter.Restore();
        }
    }

    private AppWindow? GetAppWindow()
    {
        var window = (Microsoft.UI.Xaml.Application.Current as App)?.MainWindow;
        if (window is null)
        {
            return null;
        }

        var hwnd = WindowNative.GetWindowHandle(window);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        return AppWindow.GetFromWindowId(windowId);
    }
}
