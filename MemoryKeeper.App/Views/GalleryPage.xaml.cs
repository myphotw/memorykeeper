using MemoryKeeper.App.Diagnostics;
using MemoryKeeper.App.Dialogs;
using MemoryKeeper.App.Models;
using MemoryKeeper.App.Services;
using MemoryKeeper.App.ViewModels;
using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Navigation;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Infrastructure.Services.Api;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System.Collections.ObjectModel;
using System.Numerics;

namespace MemoryKeeper.App.Views;

public sealed class GalleryPlaceManagementRequestedEventArgs(Guid placeId, string displayName) : EventArgs
{
    public Guid PlaceId { get; } = placeId;

    public string DisplayName { get; } = displayName;
}

public sealed partial class GalleryPage : Page
{
    private const int MaximumCaptureDateBatchSize = 500;
    private const int CaptureDateRevisionRefreshConcurrency = 8;

    private GalleryItem? _pageSelectedItem;
    private ObservableCollection<GalleryItem>? _subscribedItems;
    private readonly PhotoDetailView _photoDetailView;
    private readonly INavigationService _navigation;
    private readonly ICatalogInvalidation _catalogInvalidation;
    private readonly MemoryKeeperPlaceService _placeService;
    private readonly GalleryPlaceAssignmentWorkflow _placeAssignmentWorkflow;
    private readonly IGalleryApiRepository _galleryApiRepository;
    private readonly MemoryKeeperWriteService _writeService;
    private readonly ILocationResolver _locationResolver;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ISettingRepository _settingRepository;
    private bool _detailViewHosted;
    private ScrollViewer? _photoScrollViewer;

    public event EventHandler? OpenImportRequested;

    public event EventHandler? OpenPendingRequested;

    public event EventHandler? OpenMapRequested;

    public event EventHandler<GalleryPlaceManagementRequestedEventArgs>? OpenPlaceManagementRequested;

    public GalleryViewModel ViewModel { get; }

    public GalleryPage(
        GalleryViewModel viewModel,
        PhotoDetailView photoDetailView,
        INavigationService navigation,
        ICatalogInvalidation catalogInvalidation,
        MemoryKeeperPlaceService placeService,
        GalleryPlaceAssignmentWorkflow placeAssignmentWorkflow,
        IGalleryApiRepository galleryApiRepository,
        MemoryKeeperWriteService writeService,
        ILocationResolver locationResolver,
        ILoggerFactory loggerFactory,
        ISettingRepository settingRepository)
    {
        GalleryDiagnostics.WriteStep("GalleryPage constructor start");
        ViewModel = viewModel;
        _photoDetailView = photoDetailView;
        _navigation = navigation;
        _catalogInvalidation = catalogInvalidation;
        _placeService = placeService;
        _placeAssignmentWorkflow = placeAssignmentWorkflow;
        _galleryApiRepository = galleryApiRepository;
        _writeService = writeService;
        _locationResolver = locationResolver;
        _loggerFactory = loggerFactory;
        _settingRepository = settingRepository;
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
        _photoDetailView.ViewModel.PlaceRegistered += OnDetailPlaceRegistered;
        _photoDetailView.ViewModel.CaptureDateChanged += OnDetailCaptureDateChanged;
        _photoDetailView.ViewModel.OpenMapRequested += OnDetailOpenMapRequested;
        Loaded += GalleryPage_OnLoaded;
        SizeChanged += GalleryPage_OnSizeChanged;
        ResubscribeItems();
        GalleryDiagnostics.WriteStep("GalleryPage constructor complete");
    }

    private void GalleryPage_OnLoaded(object sender, RoutedEventArgs e)
    {
        GalleryDiagnostics.WriteStep("GalleryPage Loaded");
        RefreshBackNavigation();
        UpdateEmptyState();
        _photoScrollViewer ??= FindDescendant<ScrollViewer>(PhotoGrid);
        if (_photoScrollViewer is not null)
        {
            _photoScrollViewer.ViewChanged -= PhotoScrollViewer_OnViewChanged;
            _photoScrollViewer.ViewChanged += PhotoScrollViewer_OnViewChanged;
        }
    }

    private void RefreshBackNavigation()
    {
        var current = _navigation.Current;
        var isVisible = current is { } entry
                        && entry.Kind != NavigationKind.TopLevel
                        && _navigation.CanGoBack;
        var label = _navigation.BackEntry?.DisplayLabel;
        label = string.IsNullOrWhiteSpace(label) ? "뒤로" : label.Trim();

        BackNavigationLabel.Text = label;
        BackNavigationButton.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(BackNavigationButton, $"{label}(으)로 돌아가기");
        AutomationProperties.SetName(BackNavigationButton, $"이전 화면: {label}");
    }

    private void PhotoScrollViewer_OnViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_photoScrollViewer is null || e.IsIntermediate || !ViewModel.CanLoadMore || ViewModel.IsBusy || ViewModel.IsMutating)
        {
            return;
        }

        if (_photoScrollViewer.ScrollableHeight - _photoScrollViewer.VerticalOffset <= 600)
        {
            ViewModel.LoadMoreCommand.Execute(null);
        }
    }

    private void ViewModel_OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GalleryViewModel.Items) or nameof(GalleryViewModel.IsBusy))
        {
            if (e.PropertyName is nameof(GalleryViewModel.Items))
            {
                ResubscribeItems();
                // ItemsSource replacement owns native selection teardown. Mutating
                // SelectedItems synchronously here collides with WinUI's vector reset.
                ViewModel.ResetSelectionForItemsReplacement();
                UpdateSelectAllButtonContent();
            }

            UpdateEmptyState();
        }
        else if (e.PropertyName is nameof(GalleryViewModel.SelectedNode))
        {
            UpdateEmptyState();
        }
        else if (e.PropertyName is nameof(GalleryViewModel.SelectedCount))
        {
            UpdateSelectAllButtonContent();
        }
    }

    private void ResubscribeItems()
    {
        if (_subscribedItems is not null)
        {
            _subscribedItems.CollectionChanged -= Items_OnCollectionChanged;
        }

        _subscribedItems = ViewModel.Items;
        _subscribedItems.CollectionChanged += Items_OnCollectionChanged;
    }

    private void Items_OnCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        UpdateEmptyState();
        UpdateSelectAllButtonContent();
    }

    private void UpdateEmptyState()
    {
        var empty = !ViewModel.IsBusy
                    && ViewModel.SelectedNode is not null
                    && ViewModel.Items.Count == 0;
        GalleryEmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        PhotoGrid.Visibility = ViewModel.Items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Gallery_OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (ViewModel.IsEditing || ViewModel.IsMutating)
        {
            return;
        }
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
        ApplySelectionVisuals();
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

    private void PhotoDetail_OnClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsEditing || ViewModel.IsMutating)
        {
            return;
        }
        if (sender is FrameworkElement { Tag: GalleryItem item })
        {
            _ = ShowDetailPanelAsync(item, toggle: true);
        }
    }

    private void PhotoCard_OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (ViewModel.IsEditing || ViewModel.IsMutating)
        {
            e.Handled = true;
            return;
        }
        if (sender is Border { Tag: GalleryItem item })
        {
            SelectItemForDisplay(item);
            ViewModel.CaptureFocusState(GetGridScrollOffset(), item.MediaId);
            ViewModel.OpenPhotoViewerCommand.Execute(item);
            e.Handled = true;
        }
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
        _ = ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void OnDetailPlaceRegistered(object? sender, EventArgs e) =>
        _ = ReloadAfterPlaceChangeAsync();

    private void OnDetailCaptureDateChanged(object? sender, EventArgs e) =>
        _ = ReloadAfterPlaceChangeAsync();

    private async Task ReloadAfterPlaceChangeAsync()
    {
        ViewModel.CaptureFocusState(GetGridScrollOffset(), ViewModel.SelectedItem?.MediaId);
        _catalogInvalidation.Consume(CatalogSurface.Gallery);
        try
        {
            await ViewModel.LoadCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            _catalogInvalidation.Invalidate(CatalogSurface.Gallery);
            GalleryDiagnostics.WriteException("GalleryPage.ReloadAfterPlaceChange", ex);
            ViewModel.StatusMessage = "장소 변경 내용을 새로 고치는 중 오류가 발생했습니다.";
        }
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
        if (ViewModel.IsMutating) return;
        if (sender is FrameworkElement { Tag: GalleryTreeNode node })
        {
            ViewModel.ToggleNodeCommand.Execute(node);
        }
    }

    private void Node_OnClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsMutating) return;
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

    private async void EnterEditMode_OnClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsMutating) return;
        await CloseDetailPanelAsync();
        ViewModel.ClearDisplaySelection();
        _pageSelectedItem = null;
        // Start a fresh native selection session by changing modes; do not mutate
        // the WinUI SelectedItems vector while SelectionMode is None.
        ViewModel.EnterEditMode();
        PhotoGrid.SelectionMode = ListViewSelectionMode.Multiple;
        UpdateSelectAllButtonContent();
        ApplySelectionVisuals();
    }

    private void ExitEditMode_OnClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsMutating) return;
        // Returning to None lets WinUI tear down its own native selection state.
        PhotoGrid.SelectionMode = ListViewSelectionMode.None;
        ViewModel.ExitEditMode();
        UpdateSelectAllButtonContent();
    }

    private void SelectAll_OnClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsEditing || ViewModel.IsMutating) return;
        var allLoadedItemsSelected = GallerySelectionPolicy.AreAllLoadedItemsSelected(
            ViewModel.Items.Count,
            ViewModel.SelectedCount);
        if (allLoadedItemsSelected)
        {
            foreach (var item in PhotoGrid.SelectedItems.Cast<object>().ToList())
            {
                PhotoGrid.SelectedItems.Remove(item);
            }
        }
        else
        {
            foreach (var item in ViewModel.Items)
            {
                if (!PhotoGrid.SelectedItems.Contains(item)) PhotoGrid.SelectedItems.Add(item);
            }
        }

        ViewModel.SelectedCount = PhotoGrid.SelectedItems.Count;
        UpdateSelectAllButtonContent();
    }

    private void PhotoGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel.IsEditing && !ViewModel.IsMutating)
        {
            ViewModel.SelectedCount = PhotoGrid.SelectedItems.Count;
        }
        UpdateSelectAllButtonContent();
    }

    private void UpdateSelectAllButtonContent()
    {
        SelectAllButton.Content = GallerySelectionPolicy.GetToggleLabel(
            ViewModel.Items.Count,
            ViewModel.SelectedCount);
    }

    private void ManageCurrentPlace_OnClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.TryGetCurrentRegisteredPlace(out var placeId, out var displayName))
        {
            return;
        }

        ViewModel.CaptureFocusState(GetGridScrollOffset(), ViewModel.SelectedItem?.MediaId);
        OpenPlaceManagementRequested?.Invoke(
            this,
            new GalleryPlaceManagementRequestedEventArgs(placeId, displayName));
    }

    private async void ChangePlace_OnClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanChangePlace) return;
        var selected = PhotoGrid.SelectedItems.OfType<GalleryItem>().ToList();
        if (selected.Count > GalleryPlaceAssignmentWorkflow.MaximumBatchSize)
        {
            ViewModel.CompleteMutation(false, $"한 번에 최대 {GalleryPlaceAssignmentWorkflow.MaximumBatchSize}장까지 변경할 수 있습니다.");
            return;
        }

        var selectedIds = selected.Select(item => item.BackendFileId).ToHashSet(StringComparer.Ordinal);
        var sourceWasUnclassified = ViewModel.SelectedNode?.BuildQuery().UnclassifiedOnly == true;
        ViewModel.CaptureFocusState(GetGridScrollOffset(), ViewModel.SelectedItem?.MediaId);
        var session = new GalleryPlaceEditSessionViewModel(
            _placeService,
            _placeAssignmentWorkflow,
            _locationResolver,
            _loggerFactory.CreateLogger<GalleryPlaceEditSessionViewModel>(),
            selected)
        {
            HostXamlRoot = XamlRoot,
            RadiusExpansionPreviewHandler = ShowRadiusExpansionPreviewAsync,
            MutationStarted = ViewModel.BeginMutation,
            MutationProgress = UpdateMutationProgressOnUiThread,
        };

        try
        {
            await PlaceRegistrationDialog.ShowAsync(
                XamlRoot,
                session,
                new PlaceRegistrationDialog.Options
                {
                    Title = $"사진 {selected.Count}장 장소 변경",
                    PrimaryButtonText = "장소 변경",
                    SupportsMapPick = true,
                    MapPickHandler = host => ShowMapPickInPlaceDialogAsync(host, session),
                });

            if (!session.MutationAttempted)
            {
                return;
            }

            if (session.Succeeded && session.TargetPlaceId is Guid targetId)
            {
                ViewModel.UpdateMutationStatus("사진첩을 갱신하고 있습니다", "잠시 기다려 주세요");
                if (sourceWasUnclassified)
                {
                    _catalogInvalidation.Consume(CatalogSurface.Gallery);
                    await ViewModel.LoadCommand.ExecuteAsync(null);
                    ViewModel.CompleteMutation(true, $"{selected.Count}장의 장소 변경이 완료되었습니다.");
                    return;
                }

                _catalogInvalidation.Consume(CatalogSurface.Gallery);
                await ViewModel.ReloadAndSelectRegisteredPlaceAsync(targetId);
                PhotoGrid.SelectionMode = ListViewSelectionMode.None;
                ViewModel.CompleteMutation(true, $"{selected.Count}장의 장소 변경이 완료되었습니다.");
                ViewModel.ExitEditMode();
                return;
            }

            _catalogInvalidation.Consume(CatalogSurface.Gallery);
            await ViewModel.LoadCommand.ExecuteAsync(null);
            await RestoreNativeSelectionAfterItemsReplacementAsync(selectedIds);
            ViewModel.CompleteMutation(false, session.PlaceDialogStatus);
            ViewModel.SelectedCount = PhotoGrid.SelectedItems.Count;
        }
        catch (Exception ex)
        {
            GalleryDiagnostics.WriteException("GalleryPage.ChangePlace", ex);
            if (ViewModel.IsMutating)
            {
                ViewModel.CompleteMutation(false, "사진의 장소를 저장하지 못했습니다. 다시 시도해 주세요.");
            }
            else
            {
                ViewModel.MutationInfoSeverity = InfoBarSeverity.Error;
                ViewModel.MutationInfoMessage = "장소 정보를 불러오지 못했습니다. 다시 시도해 주세요.";
                ViewModel.IsMutationInfoOpen = true;
            }
        }
    }

    private async void DirectAssignPlace_OnClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanDirectAssignPlace)
        {
            return;
        }

        var selected = GetSelectedMutationItems();
        if (selected.Count == 0)
        {
            ViewModel.CompleteMutation(false, "장소를 지정할 사진을 선택하세요.");
            return;
        }
        if (selected.Count > GalleryPlaceAssignmentWorkflow.MaximumBatchSize)
        {
            ViewModel.CompleteMutation(false, $"한 번에 최대 {GalleryPlaceAssignmentWorkflow.MaximumBatchSize:N0}장까지 변경할 수 있습니다.");
            return;
        }

        PlaceDto? targetPlace;
        try
        {
            var places = await _placeService.GetPlaceListAsync();
            targetPlace = await DirectPlaceAssignmentDialog.ShowAsync(XamlRoot, places, selected.Count);
        }
        catch (Exception ex)
        {
            GalleryDiagnostics.WriteException("GalleryPage.DirectAssignPlace.LoadPlaces", ex);
            ViewModel.MutationInfoSeverity = InfoBarSeverity.Error;
            ViewModel.MutationInfoMessage = "등록된 장소를 불러오지 못했습니다. 다시 시도해 주세요.";
            ViewModel.IsMutationInfoOpen = true;
            return;
        }

        if (targetPlace is null)
        {
            return;
        }

        ViewModel.CaptureFocusState(GetGridScrollOffset(), ViewModel.SelectedItem?.MediaId);
        ViewModel.BeginMutation(
            $"{selected.Count:N0}장의 장소를 직접 지정하고 있습니다.",
            "장소 범위는 변경하지 않습니다.");
        try
        {
            await _placeAssignmentWorkflow.AssignPlaceDirectlyAsync(
                selected.Select(item => item.BackendFileId).ToList(),
                targetPlace.Id,
                reportStage: stage => UpdateMutationProgressOnUiThread(
                    $"{selected.Count:N0}장의 장소를 직접 지정하고 있습니다.",
                    GalleryPlaceAssignmentWorkflow.GetDiagnosticStageName(stage)));

            ViewModel.UpdateMutationStatus("사진첩을 갱신하고 있습니다.", "잠시 기다려 주세요.");
            await ReloadCurrentGalleryAfterCaptureDateMutationAsync();
            ViewModel.CompleteMutation(true, $"{selected.Count:N0}장을 {targetPlace.DisplayName}(으)로 분류했습니다.");
        }
        catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            GalleryDiagnostics.WriteException("GalleryPage.DirectAssignPlace.Conflict", ex);
            try
            {
                ViewModel.UpdateMutationStatus("최신 사진 상태를 불러오고 있습니다.", "잠시 기다려 주세요.");
                await ReloadCurrentGalleryAfterCaptureDateMutationAsync();
                ViewModel.CompleteMutation(false, "사진의 장소 상태가 변경되었습니다. 최신 상태를 불러왔으니 다시 시도해 주세요.");
            }
            catch (Exception reloadException)
            {
                GalleryDiagnostics.WriteException("GalleryPage.DirectAssignPlace.Reload", reloadException);
                ViewModel.CompleteMutation(false, "장소 상태가 변경되었지만 최신 목록을 불러오지 못했습니다. 다시 시도해 주세요.");
            }
        }
        catch (Exception ex)
        {
            GalleryDiagnostics.WriteException("GalleryPage.DirectAssignPlace", ex);
            ViewModel.CompleteMutation(false, "사진의 장소를 직접 지정하지 못했습니다. 다시 시도해 주세요.");
        }
    }

    private async void ChangePhotoCategory_OnClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanChangePhotoCategory)
        {
            return;
        }

        var selected = GetSelectedMutationItems();
        if (selected.Count == 0)
        {
            ViewModel.CompleteMutation(false, "분류를 변경할 사진을 선택하세요.");
            return;
        }
        if (selected.Count > MaximumCaptureDateBatchSize)
        {
            ViewModel.CompleteMutation(false, $"한 번에 최대 {MaximumCaptureDateBatchSize:N0}장까지 변경할 수 있습니다.");
            return;
        }

        var sourceWasDaily = ViewModel.IsDailySource;
        var targetCategory = sourceWasDaily
            ? MemoryKeeperPhotoCategories.Normal
            : MemoryKeeperPhotoCategories.Daily;
        var actionText = sourceWasDaily ? "일상 해제" : "일상 분류";
        ViewModel.CaptureFocusState(GetGridScrollOffset(), ViewModel.SelectedItem?.MediaId);
        ViewModel.BeginMutation(
            $"{selected.Count:N0}장의 {actionText}을 준비하고 있습니다.",
            "최신 사진 상태를 확인하고 있습니다.");

        try
        {
            var revisions = await LoadLatestPhotoCategoryRevisionsAsync(selected);
            if (revisions is null)
            {
                ViewModel.CompleteMutation(false, "선택한 사진의 최신 분류 revision을 확인할 수 없습니다. 사진첩을 새로 고친 뒤 다시 시도해 주세요.");
                return;
            }

            ViewModel.UpdateMutationStatus(
                $"{selected.Count:N0}장의 {actionText}을 적용하고 있습니다.",
                "잠시 기다려 주세요.");
            await _writeService.SetPhotoCategoryAsync(revisions, targetCategory);

            ViewModel.UpdateMutationStatus("사진첩을 갱신하고 있습니다.", "잠시 기다려 주세요.");
            await ReloadCurrentGalleryAfterCaptureDateMutationAsync();
            ViewModel.CompleteMutation(true, $"{selected.Count:N0}장의 {actionText}을 완료했습니다.");
        }
        catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            GalleryDiagnostics.WriteException("GalleryPage.ChangePhotoCategory.Conflict", ex);
            try
            {
                ViewModel.UpdateMutationStatus("최신 사진 상태를 불러오고 있습니다.", "잠시 기다려 주세요.");
                await ReloadCurrentGalleryAfterCaptureDateMutationAsync();
                ViewModel.CompleteMutation(false, "사진의 분류 상태가 변경되었습니다. 최신 상태를 불러왔으니 다시 시도해 주세요.");
            }
            catch (Exception reloadException)
            {
                GalleryDiagnostics.WriteException("GalleryPage.ChangePhotoCategory.Reload", reloadException);
                ViewModel.CompleteMutation(false, "분류 상태가 변경되었지만 최신 목록을 불러오지 못했습니다. 다시 시도해 주세요.");
            }
        }
        catch (Exception ex)
        {
            GalleryDiagnostics.WriteException("GalleryPage.ChangePhotoCategory", ex);
            ViewModel.CompleteMutation(false, $"사진의 {actionText}을 완료하지 못했습니다. 다시 시도해 주세요.");
        }
    }

    private List<GalleryItem> GetSelectedMutationItems() =>
        PhotoGrid.SelectedItems
            .OfType<GalleryItem>()
            .Where(item => item.MediaId != Guid.Empty && !string.IsNullOrWhiteSpace(item.BackendFileId))
            .DistinctBy(item => item.BackendFileId, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private async Task<IReadOnlyDictionary<Guid, int>?> LoadLatestPhotoCategoryRevisionsAsync(
        IReadOnlyList<GalleryItem> selected)
    {
        var revisions = selected
            .Where(item => item.Media.HasPhotoCategoryRevision && item.Media.PhotoCategoryRevision >= 0)
            .ToDictionary(item => item.MediaId, item => item.Media.PhotoCategoryRevision);
        var missing = selected.Where(item => !revisions.ContainsKey(item.MediaId)).ToList();
        var refreshedCount = selected.Count - missing.Count;
        foreach (var batch in missing.Chunk(CaptureDateRevisionRefreshConcurrency))
        {
            var refreshed = await Task.WhenAll(batch.Select(async item =>
                (Item: item, Detail: await _galleryApiRepository.GetPhotoAsync(item.MediaId))));
            foreach (var (item, detail) in refreshed)
            {
                if (!detail.HasPhotoCategoryRevision
                    || detail.PhotoCategoryRevision < 0
                    || !string.Equals(detail.FileId, item.BackendFileId, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                revisions[item.MediaId] = detail.PhotoCategoryRevision;
            }

            refreshedCount += refreshed.Length;
            ViewModel.UpdateMutationStatus(
                $"{selected.Count:N0}장의 분류 변경을 준비하고 있습니다.",
                $"최신 사진 상태 확인 {refreshedCount:N0}/{selected.Count:N0}");
        }

        return revisions.Count == selected.Count ? revisions : null;
    }

    private async void ChangeCaptureDate_OnClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsEditing || ViewModel.IsMutating)
        {
            return;
        }

        var selected = PhotoGrid.SelectedItems
            .OfType<GalleryItem>()
            .Where(item => item.MediaId != Guid.Empty && !string.IsNullOrWhiteSpace(item.BackendFileId))
            .DistinctBy(item => item.BackendFileId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (selected.Count == 0)
        {
            ViewModel.CompleteMutation(false, "촬영일을 변경할 사진을 선택하세요.");
            return;
        }
        if (selected.Count > MaximumCaptureDateBatchSize)
        {
            ViewModel.CompleteMutation(false, $"한 번에 최대 {MaximumCaptureDateBatchSize:N0}장까지 변경할 수 있습니다.");
            return;
        }

        var firstCapturedAt = selected[0].Media.CapturedAt;
        var selectedDate = await CaptureDateDialog.ShowChangeAsync(
            XamlRoot,
            selected.Count,
            selected[0].ThumbnailImage,
            "선택한 사진에 같은 촬영일을 적용합니다.",
            firstCapturedAt is DateTimeOffset capturedAt
                ? DateOnly.FromDateTime(capturedAt.LocalDateTime)
                : null);
        if (selectedDate is not DateOnly captureDate)
        {
            return;
        }

        ViewModel.CaptureFocusState(GetGridScrollOffset(), ViewModel.SelectedItem?.MediaId);
        ViewModel.BeginMutation(
            $"{selected.Count:N0}장의 촬영일 변경을 준비하고 있습니다.",
            "최신 사진 상태를 확인하고 있습니다.");
        var stage = "prepare";

        try
        {
            var revisions = await LoadLatestCaptureDateRevisionsAsync(selected);
            if (revisions is null)
            {
                GalleryDiagnostics.WriteOperationFailure(
                    operation: "GalleryCaptureDateAssignment",
                    stage: stage,
                    selectedCount: selected.Count,
                    targetPlaceId: null,
                    exceptionType: "CaptureDateRevisionUnavailable",
                    safeMessage: "A selected photo did not provide a valid capture-date revision.");
                ViewModel.CompleteMutation(
                    false,
                    "선택한 사진의 최신 촬영일 revision을 확인할 수 없습니다. 사진첩을 새로 고친 뒤 다시 시도해 주세요.");
                return;
            }

            stage = "mutation";
            ViewModel.UpdateMutationStatus(
                $"{selected.Count:N0}장의 촬영일을 변경하고 있습니다.",
                "잠시 기다려 주세요.");
            var response = await _writeService.SetCaptureDateAsync(revisions, captureDate);

            stage = "reload";
            ViewModel.UpdateMutationStatus("사진첩을 갱신하고 있습니다.", "잠시 기다려 주세요.");
            await ReloadCurrentGalleryAfterCaptureDateMutationAsync();
            ViewModel.CompleteMutation(true, $"{response.UpdatedCount:N0}장의 촬영일을 변경했습니다.");
        }
        catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            WriteCaptureDateFailure(stage, selected.Count, ex, "The capture-date revision was stale.");
            try
            {
                ViewModel.UpdateMutationStatus("최신 사진 상태를 불러오고 있습니다.", "잠시 기다려 주세요.");
                await ReloadCurrentGalleryAfterCaptureDateMutationAsync();
                ViewModel.CompleteMutation(false, "사진 상태가 변경되었습니다. 최신 상태를 불러왔으니 다시 시도해 주세요.");
            }
            catch (Exception reloadException)
            {
                WriteCaptureDateFailure("reload", selected.Count, reloadException, "Gallery reload after a revision conflict failed.");
                ViewModel.CompleteMutation(false, "사진 상태가 변경되었지만 최신 목록을 불러오지 못했습니다. 다시 시도해 주세요.");
            }
        }
        catch (Exception ex)
        {
            WriteCaptureDateFailure(stage, selected.Count, ex, "Gallery capture-date assignment failed.");
            ViewModel.CompleteMutation(
                false,
                stage == "reload"
                    ? "촬영일은 변경했지만 사진첩을 새로 고치지 못했습니다. 다시 시도해 주세요."
                    : "사진의 촬영일을 변경하지 못했습니다. 다시 시도해 주세요.");
        }
    }

    private async Task<IReadOnlyDictionary<Guid, int>?> LoadLatestCaptureDateRevisionsAsync(
        IReadOnlyList<GalleryItem> selected)
    {
        var revisions = new Dictionary<Guid, int>(selected.Count);
        var refreshedCount = 0;
        foreach (var batch in selected.Chunk(CaptureDateRevisionRefreshConcurrency))
        {
            var refreshed = await Task.WhenAll(batch.Select(async item =>
                (Item: item, Detail: await _galleryApiRepository.GetPhotoAsync(item.MediaId))));
            foreach (var (item, detail) in refreshed)
            {
                if (!detail.HasDateRevision
                    || detail.DateRevision < 0
                    || !string.Equals(detail.FileId, item.BackendFileId, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                revisions[item.MediaId] = detail.DateRevision;
            }

            refreshedCount += refreshed.Length;
            ViewModel.UpdateMutationStatus(
                $"{selected.Count:N0}장의 촬영일 변경을 준비하고 있습니다.",
                $"최신 사진 상태 확인 {refreshedCount:N0}/{selected.Count:N0}");
        }

        return revisions.Count == selected.Count ? revisions : null;
    }

    private async Task ReloadCurrentGalleryAfterCaptureDateMutationAsync()
    {
        _catalogInvalidation.Consume(CatalogSurface.Gallery);
        try
        {
            await ViewModel.LoadCommand.ExecuteAsync(null);
        }
        catch
        {
            _catalogInvalidation.Invalidate(CatalogSurface.Gallery);
            throw;
        }
    }

    private static void WriteCaptureDateFailure(
        string stage,
        int selectedCount,
        Exception exception,
        string safeMessage)
    {
        var apiException = exception as ApiException;
        GalleryDiagnostics.WriteOperationFailure(
            operation: "GalleryCaptureDateAssignment",
            stage: stage,
            selectedCount: selectedCount,
            targetPlaceId: null,
            exceptionType: exception.GetType().Name,
            safeMessage: safeMessage,
            httpStatus: apiException is null ? null : (int)apiException.StatusCode,
            detailCode: apiException?.DetailCode);
    }

    private Task<bool> ShowRadiusExpansionPreviewAsync(string placeName, PlaceRadiusExpansionPlan plan) =>
        PlaceRadiusExpansionDialog.ShowAsync(XamlRoot, _loggerFactory, _settingRepository, placeName, plan);

    private void UpdateMutationProgressOnUiThread(string status, string hint)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            ViewModel.UpdateMutationStatus(status, hint);
            return;
        }

        if (!DispatcherQueue.TryEnqueue(() => ViewModel.UpdateMutationStatus(status, hint)))
        {
            throw new InvalidOperationException("Gallery mutation progress could not be dispatched to the UI thread.");
        }
    }

    private Task ShowMapPickInPlaceDialogAsync(ContentDialog host, GalleryPlaceEditSessionViewModel session) =>
        MapPickSession.RunInDialogAsync(
            host,
            _loggerFactory,
            _settingRepository,
            session.MapPickLatitude,
            session.MapPickLongitude,
            session.MapPickRadiusMeters,
            async (latitude, longitude, radius) =>
            {
                await session.ApplyMapPickAsync(latitude, longitude, radius);
                return session.PlaceDialogStatus;
            },
            session.DiscardMapPickSelection,
            new MapPickSession.SearchHooks
            {
                SearchAsync = async query =>
                {
                    session.PlaceSearchText = query;
                    await session.SearchPlaceSuggestionsAsync();
                    return session.PlaceSearchResults;
                },
                ResolveCoordinatesAsync = session.ResolveSuggestionCoordinatesAsync,
            });

    private Task RestoreNativeSelectionAfterItemsReplacementAsync(IReadOnlySet<string> selectedFileIds)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    foreach (var item in ViewModel.Items.Where(item => selectedFileIds.Contains(item.BackendFileId)))
                    {
                        PhotoGrid.SelectedItems.Add(item);
                    }
                    completion.TrySetResult();
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            }))
        {
            completion.TrySetResult();
        }
        return completion.Task;
    }
}
