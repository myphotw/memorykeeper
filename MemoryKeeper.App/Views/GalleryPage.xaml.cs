using MemoryKeeper.App.Diagnostics;
using MemoryKeeper.App.Models;
using MemoryKeeper.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System.Collections.ObjectModel;
using System.Numerics;

namespace MemoryKeeper.App.Views;

public sealed partial class GalleryPage : Page
{
    private GalleryItem? _pageSelectedItem;
    private ObservableCollection<GalleryItem>? _subscribedItems;
    private readonly PhotoDetailView _photoDetailView;
    private bool _detailViewHosted;

    public event EventHandler? OpenImportRequested;

    public event EventHandler? OpenPendingRequested;

    public event EventHandler? OpenMapRequested;

    public GalleryViewModel ViewModel { get; }

    public GalleryPage(GalleryViewModel viewModel, PhotoDetailView photoDetailView)
    {
        GalleryDiagnostics.WriteStep("GalleryPage constructor start");
        ViewModel = viewModel;
        _photoDetailView = photoDetailView;
        _photoDetailView.ConfigurePanelMode();
        DataContext = viewModel;
        try
        {
            GalleryDiagnostics.WriteStep("GalleryPage InitializeComponent start");
            InitializeComponent();
            GalleryDiagnostics.WriteStep("GalleryPage InitializeComponent complete");
        }
        catch (Exception ex)
        {
            GalleryDiagnostics.WriteException("GalleryPage InitializeComponent", ex);
            throw;
        }

        ViewModel.ScrollToMediaRequested += OnScrollToMediaRequested;
        ViewModel.ScrollOffsetRequested += OnScrollOffsetRequested;
        ViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        _photoDetailView.ViewModel.Closed += OnDetailClosed;
        _photoDetailView.ViewModel.PhotoDeleted += OnPhotoDeleted;
        _photoDetailView.ViewModel.OpenMapRequested += OnDetailOpenMapRequested;
        Loaded += GalleryPage_OnLoaded;
        SizeChanged += GalleryPage_OnSizeChanged;
        ResubscribeItems();
        GalleryDiagnostics.WriteStep("GalleryPage constructor complete");
    }

    private void GalleryPage_OnLoaded(object sender, RoutedEventArgs e)
    {
        GalleryDiagnostics.WriteStep("GalleryPage Loaded");
        UpdateEmptyState();
        RefreshDetail();
    }

    private void ViewModel_OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GalleryViewModel.Items) or nameof(GalleryViewModel.IsBusy))
        {
            if (e.PropertyName is nameof(GalleryViewModel.Items))
            {
                ResubscribeItems();
            }

            UpdateEmptyState();
            RefreshDetail();
        }
        else if (e.PropertyName is nameof(GalleryViewModel.BreadcrumbText)
                 or nameof(GalleryViewModel.SelectedNode)
                 or nameof(GalleryViewModel.SelectedItem))
        {
            RefreshDetail();
        }
    }

    private void ResubscribeItems()
    {
        if (_subscribedItems is not null)
        {
            _subscribedItems.CollectionChanged -= Items_OnCollectionChanged;
            foreach (var item in _subscribedItems)
            {
                item.PropertyChanged -= GalleryItem_OnPropertyChanged;
            }
        }

        _subscribedItems = ViewModel.Items;
        _subscribedItems.CollectionChanged += Items_OnCollectionChanged;
        foreach (var item in _subscribedItems)
        {
            item.PropertyChanged += GalleryItem_OnPropertyChanged;
        }
    }

    private void Items_OnCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (GalleryItem item in e.OldItems)
            {
                item.PropertyChanged -= GalleryItem_OnPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (GalleryItem item in e.NewItems)
            {
                item.PropertyChanged += GalleryItem_OnPropertyChanged;
            }
        }

        UpdateEmptyState();
        RefreshDetail();
    }

    private void GalleryItem_OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GalleryItem.ThumbnailImage) or nameof(GalleryItem.IsSelected))
        {
            DispatcherQueue.TryEnqueue(RefreshDetail);
        }
    }

    private void UpdateEmptyState()
    {
        var empty = !ViewModel.IsBusy && ViewModel.Items.Count == 0;
        GalleryEmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        PhotoGrid.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        if (empty)
        {
            DetailCard.Visibility = Visibility.Collapsed;
        }
    }

    private void Gallery_OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not GalleryItem item)
        {
            return;
        }

        SelectItemForDisplay(item);
        ViewModel.CaptureFocusState(GetGridScrollOffset(), item.MediaId);
        if (ViewModel.IsDetailPanelOpen)
        {
            _ = ShowDetailPanelAsync(item, toggle: false);
        }
    }

    private void SelectItemForDisplay(GalleryItem item)
    {
        foreach (var galleryItem in ViewModel.Items)
        {
            galleryItem.IsSelected = ReferenceEquals(galleryItem, item);
        }

        _pageSelectedItem = item;
        ViewModel.SelectedItem = item;
        RefreshDetail();
        ApplySelectionVisuals();
    }

    private void RefreshDetail()
    {
        var focus = ViewModel.SelectedItem
            ?? _pageSelectedItem
            ?? ViewModel.Items.FirstOrDefault();

        if (focus is null || ViewModel.Items.Count == 0)
        {
            DetailCard.Visibility = Visibility.Collapsed;
            return;
        }

        DetailCard.Visibility = Visibility.Visible;
        DetailThumbImage.Source = focus.ThumbnailImage;
        DetailFileName.Text = focus.FileName;
        DetailCapturedAt.Text = focus.CapturedAtText;
        DetailPlace.Text = ResolvePlaceLabel();
    }

    private string ResolvePlaceLabel()
    {
        var node = ViewModel.SelectedNode;
        if (node is not null
            && node.Kind is GalleryTreeNodeKind.Place
                or GalleryTreeNodeKind.PlaceBrowse
                or GalleryTreeNodeKind.City
                or GalleryTreeNodeKind.Country)
        {
            return string.IsNullOrWhiteSpace(node.Title) ? "장소 미상" : node.Title;
        }

        var crumb = ViewModel.BreadcrumbText;
        if (!string.IsNullOrWhiteSpace(crumb) && !string.Equals(crumb, "사진첩", StringComparison.Ordinal))
        {
            return crumb;
        }

        return "장소 미상";
    }

    private void ApplySelectionVisuals()
    {
        // Re-apply when containers exist; Loaded handlers also refresh per card.
        PhotoGrid.UpdateLayout();
        foreach (var item in ViewModel.Items)
        {
            if (PhotoGrid.ContainerFromItem(item) is not GridViewItem container)
            {
                continue;
            }

            if (FindDescendantByTag<Border>(container, item) is { } card)
            {
                ApplyCardElevation(card, item.IsSelected, hovered: false);
            }
        }
    }

    private void PhotoCard_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Border { Tag: GalleryItem item } card)
        {
            card.Shadow ??= new ThemeShadow();
            ApplyCardElevation(card, item.IsSelected, hovered: false);
        }
    }

    private void PhotoCard_OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border { Tag: GalleryItem item } card)
        {
            card.Shadow ??= new ThemeShadow();
            ApplyCardElevation(card, item.IsSelected, hovered: true);
            card.Opacity = 0.94;
        }
    }

    private void PhotoCard_OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border { Tag: GalleryItem item } card)
        {
            ApplyCardElevation(card, item.IsSelected, hovered: false);
            card.Opacity = 1;
        }
    }

    private static void ApplyCardElevation(Border card, bool selected, bool hovered)
    {
        var z = selected
            ? (hovered ? 16f : 12f)
            : (hovered ? 8f : 4f);
        card.Translation = new Vector3(0, 0, z);

        if (selected)
        {
            card.BorderThickness = new Thickness(0);
            card.Opacity = hovered ? 0.96 : 1;
            // Soft primary wash via background tint without a hard ring.
            if (global::Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue("MkBrushPrimary", out var brush)
                && brush is SolidColorBrush primary)
            {
                card.BorderBrush = primary;
                card.BorderThickness = new Thickness(0);
            }
        }
    }

    private void ImportPhotos_OnClick(object sender, RoutedEventArgs e) =>
        OpenImportRequested?.Invoke(this, EventArgs.Empty);

    private void ToggleDetailPanel_OnClick(object sender, RoutedEventArgs e)
    {
        var selected = ViewModel.SelectedItem ?? _pageSelectedItem ?? ViewModel.Items.FirstOrDefault();
        if (selected is not null)
        {
            _ = ShowDetailPanelAsync(selected, toggle: true);
        }
    }

    private void PhotoCard_OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is Border { Tag: GalleryItem item })
        {
            SelectItemForDisplay(item);
            ViewModel.CaptureFocusState(GetGridScrollOffset(), item.MediaId);
            ViewModel.OpenPhotoViewerCommand.Execute(item);
            e.Handled = true;
        }
    }

    private void OpenPhotoViewer_OnClick(object sender, RoutedEventArgs e)
    {
        var selected = ViewModel.SelectedItem ?? _pageSelectedItem ?? ViewModel.Items.FirstOrDefault();
        if (selected is null)
        {
            return;
        }

        ViewModel.CaptureFocusState(GetGridScrollOffset(), selected.MediaId);
        ViewModel.OpenPhotoViewerCommand.Execute(selected);
    }

    private async Task ShowDetailPanelAsync(GalleryItem item, bool toggle)
    {
        if (toggle && ViewModel.IsDetailPanelOpen)
        {
            await CloseDetailPanelAsync();
            return;
        }

        SelectItemForDisplay(item);
        ViewModel.PreparePhotoDetail(item);
        ApplyDetailPanelLayout(ActualWidth);

        Task detailLoad;
        if (!_detailViewHosted)
        {
            PhotoDetailHost.Content = _photoDetailView;
            _detailViewHosted = true;
            detailLoad = Task.CompletedTask;
        }
        else
        {
            detailLoad = _photoDetailView.LoadMediaAsync(item.MediaId);
        }

        DetailPanel.Visibility = Visibility.Visible;
        ViewModel.IsDetailPanelOpen = true;
        await Task.WhenAll(AnimateDetailPanelAsync(0), detailLoad);
    }

    public bool TryCloseDetailPanel()
    {
        if (!ViewModel.IsDetailPanelOpen)
        {
            return false;
        }

        _ = CloseDetailPanelAsync();
        return true;
    }

    private async Task CloseDetailPanelAsync()
    {
        if (!ViewModel.IsDetailPanelOpen)
        {
            return;
        }

        await AnimateDetailPanelAsync(440);
        DetailPanel.Visibility = Visibility.Collapsed;
        ViewModel.IsDetailPanelOpen = false;
        ApplyDetailPanelLayout(ActualWidth);
    }

    private Task AnimateDetailPanelAsync(double destination)
    {
        var completion = new TaskCompletionSource();
        var animation = new DoubleAnimation
        {
            To = destination,
            Duration = new Duration(TimeSpan.FromMilliseconds(180)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            EnableDependentAnimation = true,
        };
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        Storyboard.SetTarget(animation, DetailPanelTransform);
        Storyboard.SetTargetProperty(animation, "X");
        storyboard.Completed += (_, _) => completion.TrySetResult();
        storyboard.Begin();
        return completion.Task;
    }

    private void GalleryPage_OnSizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyDetailPanelLayout(e.NewSize.Width);

    private void ApplyDetailPanelLayout(double width)
    {
        var useOverlay = width < 1450;
        DetailColumn.Width = useOverlay ? new GridLength(0) : GridLength.Auto;
        Grid.SetColumn(DetailPanel, useOverlay ? 1 : 2);
        Grid.SetColumnSpan(DetailPanel, useOverlay ? 2 : 1);
        Canvas.SetZIndex(DetailPanel, useOverlay ? 10 : 0);
    }

    private void OnDetailClosed(object? sender, EventArgs e) => _ = CloseDetailPanelAsync();

    private void OnDetailOpenMapRequested(object? sender, EventArgs e) =>
        OpenMapRequested?.Invoke(this, EventArgs.Empty);

    private void OnPhotoDeleted(object? sender, Guid mediaId)
    {
        var deleted = ViewModel.Items.FirstOrDefault(item => item.MediaId == mediaId);
        if (deleted is not null)
        {
            ViewModel.Items.Remove(deleted);
        }

        if (ViewModel.SelectedItem?.MediaId == mediaId)
        {
            ViewModel.SelectedItem = ViewModel.Items.FirstOrDefault();
            _pageSelectedItem = ViewModel.SelectedItem;
        }

        UpdateEmptyState();
        RefreshDetail();
        _ = ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void OnScrollToMediaRequested(object? sender, Guid mediaId)
    {
        var item = ViewModel.Items.FirstOrDefault(galleryItem => galleryItem.MediaId == mediaId);
        if (item is null)
        {
            return;
        }

        SelectItemForDisplay(item);
        PhotoGrid.UpdateLayout();
        var container = PhotoGrid.ContainerFromItem(item) as FrameworkElement;
        container?.StartBringIntoView();
    }

    private void OnScrollOffsetRequested(object? sender, double offset)
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            await Task.Delay(32);
            PhotoGrid.UpdateLayout();
            var scrollViewer = FindDescendant<ScrollViewer>(PhotoGrid);
            scrollViewer?.ChangeView(null, offset, null, disableAnimation: true);
        });
    }

    private double GetGridScrollOffset()
    {
        var scrollViewer = FindDescendant<ScrollViewer>(PhotoGrid);
        return scrollViewer?.VerticalOffset ?? 0;
    }

    public double GetGridScrollOffsetPublic() => GetGridScrollOffset();

    private static T? FindDescendant<T>(DependencyObject parent) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                return match;
            }

            var found = FindDescendant<T>(child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static T? FindDescendantByTag<T>(DependencyObject parent, object tag) where T : FrameworkElement
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match && Equals(match.Tag, tag))
            {
                return match;
            }

            var found = FindDescendantByTag<T>(child, tag);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private void Expand_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: GalleryTreeNode node })
        {
            ViewModel.ToggleNodeCommand.Execute(node);
        }
    }

    private void Node_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: GalleryTreeNode node } && !node.IsSeparator)
        {
            if (node.Kind == GalleryTreeNodeKind.Pending)
            {
                OpenPendingRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            ViewModel.SelectTreeNodeCommand.Execute(node);
        }
    }
}
