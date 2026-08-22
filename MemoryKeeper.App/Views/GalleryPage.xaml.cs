using MemoryKeeper.App.Diagnostics;
using MemoryKeeper.App.Models;
using MemoryKeeper.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Collections.ObjectModel;
using System.Numerics;

namespace MemoryKeeper.App.Views;

public sealed partial class GalleryPage : Page
{
    private GalleryItem? _pageSelectedItem;
    private ObservableCollection<GalleryItem>? _subscribedItems;

    public event EventHandler? OpenImportRequested;

    public event EventHandler? OpenPendingRequested;

    public GalleryViewModel ViewModel { get; }

    public GalleryPage(GalleryViewModel viewModel)
    {
        GalleryDiagnostics.WriteStep("GalleryPage constructor start");
        ViewModel = viewModel;
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
        Loaded += GalleryPage_OnLoaded;
        ResubscribeItems();
        GalleryDiagnostics.WriteStep("GalleryPage constructor complete");
    }

    private void GalleryPage_OnLoaded(object sender, RoutedEventArgs e)
    {
        GalleryDiagnostics.WriteStep("GalleryPage Loaded");
        UpdateEmptyState();
        RefreshHeroAndDetail();
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
            RefreshHeroAndDetail();
        }
        else if (e.PropertyName is nameof(GalleryViewModel.BreadcrumbText)
                 or nameof(GalleryViewModel.SelectedNode)
                 or nameof(GalleryViewModel.SelectedItem))
        {
            RefreshHeroAndDetail();
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
        RefreshHeroAndDetail();
    }

    private void GalleryItem_OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GalleryItem.ThumbnailImage) or nameof(GalleryItem.IsSelected))
        {
            DispatcherQueue.TryEnqueue(RefreshHeroAndDetail);
        }
    }

    private void UpdateEmptyState()
    {
        var empty = !ViewModel.IsBusy && ViewModel.Items.Count == 0;
        GalleryEmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        PhotoGrid.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        if (empty)
        {
            HeroThumbHost.Visibility = Visibility.Collapsed;
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
        ViewModel.OpenPhotoViewerCommand.Execute(item);
    }

    private void SelectItemForDisplay(GalleryItem item)
    {
        foreach (var galleryItem in ViewModel.Items)
        {
            galleryItem.IsSelected = ReferenceEquals(galleryItem, item);
        }

        _pageSelectedItem = item;
        ViewModel.SelectedItem = item;
        RefreshHeroAndDetail();
        ApplySelectionVisuals();
    }

    private void RefreshHeroAndDetail()
    {
        var focus = ViewModel.SelectedItem
            ?? _pageSelectedItem
            ?? ViewModel.Items.FirstOrDefault();

        if (focus is null || ViewModel.Items.Count == 0)
        {
            HeroThumbHost.Visibility = Visibility.Collapsed;
            DetailCard.Visibility = Visibility.Collapsed;
            return;
        }

        HeroThumbHost.Visibility = Visibility.Visible;
        HeroThumbImage.Source = focus.ThumbnailImage;

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
