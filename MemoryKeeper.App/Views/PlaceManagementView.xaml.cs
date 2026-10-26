using MemoryKeeper.App.Maps.Google;
using MemoryKeeper.App.ViewModels;
using MemoryKeeper.Application.DTOs;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MemoryKeeper.App.Views;

public sealed partial class PlaceManagementView : UserControl
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly Dictionary<TreeViewNode, PlacePickerCountryNode> _countryNodes = [];
    private readonly Dictionary<TreeViewNode, PlacePickerRegionNode> _regionNodes = [];
    private readonly Dictionary<TreeViewNode, PlacePickerItemDto> _placeNodes = [];
    private GoogleMapController? _mapController;
    private bool _preserveOnNextUnload;

    public PlaceManagementViewModel ViewModel { get; }

    public bool AutoActivate { get; set; }

    public PlaceManagementView(PlaceManagementViewModel viewModel, ILoggerFactory loggerFactory)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        _loggerFactory = loggerFactory;
        InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ViewModel.PlaceHierarchy))
        {
            DispatcherQueue.TryEnqueue(RebuildHierarchyTree);
        }
        else if (e.PropertyName is nameof(ViewModel.HasPlaceSuggestions) && ViewModel.HasPlaceSuggestions)
        {
            DispatcherQueue.TryEnqueue(() =>
                PlaceSuggestionsPanel.StartBringIntoView(new BringIntoViewOptions
                {
                    AnimationDesired = true,
                    VerticalOffset = 24
                }));
        }
        else if (e.PropertyName is nameof(ViewModel.SelectedPlace)
                 && ViewModel.SelectedPlace is { } selectedPlace)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                PlaceList.ScrollIntoView(selectedPlace);
                SelectCurrentHierarchyNode();
            });
        }
    }

    private async void PlaceManagementView_OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.HostXamlRoot = XamlRoot;
        if (PlaceHierarchyTree.RootNodes.Count == 0)
        {
            RebuildHierarchyTree();
        }

        if (AutoActivate)
        {
            await ActivateAsync();
        }
    }

    public async Task ActivateAsync()
    {
        ViewModel.HostXamlRoot = XamlRoot;
        if (_mapController is not null)
        {
            return;
        }

        _mapController = new GoogleMapController(
            MapWebView,
            _loggerFactory.CreateLogger<GoogleMapController>());
        _mapController.HostScrollRequested += OnMapHostScrollRequested;
        ViewModel.AttachMap(_mapController);
        await ViewModel.InitializeMapCommand.ExecuteAsync(null);
    }

    private async void PlaceManagementView_OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_preserveOnNextUnload)
        {
            _preserveOnNextUnload = false;
            return;
        }

        ViewModel.DetachMap();
        if (_mapController is not null)
        {
            _mapController.HostScrollRequested -= OnMapHostScrollRequested;
            await _mapController.DisposeAsync();
            _mapController = null;
        }
    }

    public void PreserveForGalleryRoundTrip() => _preserveOnNextUnload = true;

    private void OnMapHostScrollRequested(double deltaY)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var targetOffset = Math.Clamp(
                DetailScrollViewer.VerticalOffset + deltaY,
                0,
                DetailScrollViewer.ScrollableHeight);
            DetailScrollViewer.ChangeView(null, targetOffset, null, disableAnimation: true);
        });
    }

    private async void PlaceSuggestion_OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PlaceSuggestionDto suggestion)
        {
            await ViewModel.SelectSuggestionCommand.ExecuteAsync(suggestion);
        }
    }

    private void FavoritePlace_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: PlaceDto place })
        {
            PlaceHierarchyTree.SelectedNodes.Clear();
            ViewModel.SelectFavoriteCommand.Execute(place);
        }
    }

    private void RecentPlace_OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PlaceDto place)
        {
            PlaceHierarchyTree.SelectedNodes.Clear();
            ViewModel.SelectRecentCommand.Execute(place);
        }
    }

    private void AllPlaces_OnClick(object sender, RoutedEventArgs e)
    {
        PlaceHierarchyTree.SelectedNodes.Clear();
        ViewModel.ClearHierarchyScope();
    }

    private void PlaceHierarchyTree_OnItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs e)
    {
        if (e.InvokedItem is not TreeViewNode node)
        {
            return;
        }

        if (_placeNodes.TryGetValue(node, out var place))
        {
            ViewModel.SelectHierarchyPlaceCommand.Execute(place);
        }
        else if (_regionNodes.TryGetValue(node, out var region))
        {
            ViewModel.SelectHierarchyRegion(region);
        }
        else if (_countryNodes.TryGetValue(node, out var country))
        {
            ViewModel.SelectHierarchyCountry(country);
        }
    }

    private void RebuildHierarchyTree()
    {
        _countryNodes.Clear();
        _regionNodes.Clear();
        _placeNodes.Clear();
        PlaceHierarchyTree.RootNodes.Clear();

        foreach (var country in ViewModel.PlaceHierarchy)
        {
            var countryPlaceCount = country.Regions
                .SelectMany(region => region.Places)
                .Select(place => place.Id)
                .Distinct()
                .Count();
            var countryNode = new TreeViewNode
            {
                Content = $"{country.Title} ({countryPlaceCount:N0})",
                IsExpanded = true,
            };
            _countryNodes[countryNode] = country;

            foreach (var region in country.Regions)
            {
                var regionPlaceCount = region.Places.Select(place => place.Id).Distinct().Count();
                var regionNode = new TreeViewNode
                {
                    Content = $"{region.Title} ({regionPlaceCount:N0})",
                    IsExpanded = true,
                };
                _regionNodes[regionNode] = region;

                foreach (var place in region.Places)
                {
                    var placeNode = new TreeViewNode { Content = place.TreeDisplayText };
                    _placeNodes[placeNode] = place;
                    regionNode.Children.Add(placeNode);
                }

                countryNode.Children.Add(regionNode);
            }

            PlaceHierarchyTree.RootNodes.Add(countryNode);
        }

        SelectCurrentHierarchyNode();
    }

    private void SelectCurrentHierarchyNode()
    {
        TreeViewNode? selectedNode = null;
        if (ViewModel.HierarchyScopeRegion is not null)
        {
            selectedNode = _regionNodes
                .Where(entry => string.Equals(entry.Value.Title, ViewModel.HierarchyScopeRegion, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(
                                    entry.Value.Places.FirstOrDefault()?.Country,
                                    ViewModel.HierarchyScopeCountry,
                                    StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.Key)
                .FirstOrDefault();
        }
        else if (ViewModel.HierarchyScopeCountry is not null)
        {
            selectedNode = _countryNodes
                .Where(entry => string.Equals(entry.Value.Title, ViewModel.HierarchyScopeCountry, StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.Key)
                .FirstOrDefault();
        }
        else if (ViewModel.SelectedPlace is { } selectedPlace)
        {
            selectedNode = _placeNodes
                .Where(entry => entry.Value.Id == selectedPlace.Id)
                .Select(entry => entry.Key)
                .FirstOrDefault();
        }

        PlaceHierarchyTree.SelectedNodes.Clear();
        if (selectedNode is not null)
        {
            PlaceHierarchyTree.SelectedNodes.Add(selectedNode);
        }
    }

    private void LinkedPhoto_OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PlaceLinkedPhotoItem photo)
        {
            ViewModel.OpenLinkedPhotoCommand.Execute(photo);
        }
    }

    private void PlaceList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListView { SelectedItem: PlaceDto place })
        {
            ViewModel.SelectedPlace = place;
        }
    }
}
