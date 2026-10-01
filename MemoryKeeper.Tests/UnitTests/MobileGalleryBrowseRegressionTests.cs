namespace MemoryKeeper.Tests.UnitTests;

public sealed class MobileGalleryBrowseRegressionTests
{
    [Fact]
    public void HomePage_UsesExclusiveGalleryYearPlaceAndSearchModesAndKeepsExistingGrid()
    {
        var xaml = File.ReadAllText(FindSourceFile("MemoryKeeper.Mobile", "Views", "HomePage.xaml"));
        var codeBehind = File.ReadAllText(FindSourceFile("MemoryKeeper.Mobile", "Views", "HomePage.xaml.cs"));

        Assert.Contains("IsVisible=\"{Binding IsGalleryMode}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding IsYearSelectionMode}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding IsPlaceSelectionMode}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding IsPlaceYearSelectionMode}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding IsSearchMode}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding CurrentContext.Title}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding CurrentContext.HierarchyBreadcrumbText}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding CurrentContext.YearText", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding CurrentContext.YearSelectorText}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding CurrentContext.PhotoCountText}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding OpenPlaceYearSelectionCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding OpenHierarchyNavigatorCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding SiblingYearOptions}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding SelectedSiblingYearOption, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding CurrentText}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ColumnDefinitions=\"88,*,88\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding AddPhotosCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"+ 추가\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding IsRecentContext}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding BrowseOptions}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding SelectedBrowseOption, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding VisiblePlaceTreeNodes}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Path=TogglePlaceTreeNodeCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("Path=SelectPlaceTreeNodeCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("ReturnCommand=\"{Binding SearchCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding SearchResults}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding CancelSearchCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding NavigateBackCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.HandleBackRequested()", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ViewModel.AddPhotosRequested += OnAddPhotosRequested", codeBehind, StringComparison.Ordinal);
        Assert.Contains("RemainingItemsThreshold=\"8\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Items}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Picker", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("TreeView", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void GalleryHeader_SeparatesTitleBreadcrumbAndNonInteractivePhotoCountTargets()
    {
        var xaml = File.ReadAllText(FindSourceFile("MemoryKeeper.Mobile", "Views", "HomePage.xaml"));
        var document = System.Xml.Linq.XDocument.Parse(xaml);
        var elements = document.Descendants().ToArray();
        var title = Assert.Single(elements, element =>
            string.Equals((string?)element.Attribute("Text"), "{Binding CurrentContext.Title}", StringComparison.Ordinal));
        Assert.DoesNotContain(title.Parent!.Descendants(), element => element.Attribute("Command") is not null);

        var yearSelector = Assert.Single(elements, element =>
            string.Equals(
                (string?)element.Attribute("Command"),
                "{Binding OpenPlaceYearSelectionCommand}",
                StringComparison.Ordinal));
        Assert.Equal("{Binding CanOpenSiblingYearSelection}", (string?)yearSelector.Attribute("IsVisible"));
        Assert.Equal("44", (string?)yearSelector.Attribute("MinimumHeightRequest"));

        var breadcrumb = Assert.Single(elements, element =>
            string.Equals((string?)element.Attribute("Text"), "{Binding CurrentContext.HierarchyBreadcrumbText}", StringComparison.Ordinal));
        Assert.Contains(
            breadcrumb.Descendants(),
            element => string.Equals(
                (string?)element.Attribute("Command"),
                "{Binding OpenHierarchyNavigatorCommand}",
                StringComparison.Ordinal));

        var photoCount = Assert.Single(elements, element =>
            string.Equals((string?)element.Attribute("Text"), "{Binding CurrentContext.PhotoCountText}", StringComparison.Ordinal));
        Assert.DoesNotContain(photoCount.Descendants(), element => element.Attribute("Command") is not null);
        Assert.DoesNotContain("장소 선택 &#x25BE;", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void HomeViewModel_GuardsScopeChangesAndLoadsHierarchyWithoutBlockingDefaultFeed()
    {
        var source = File.ReadAllText(FindSourceFile("MemoryKeeper.Mobile", "ViewModels", "HomeViewModel.cs"));

        Assert.Contains("_ = LoadBrowseOptionsSafelyAsync();", source, StringComparison.Ordinal);
        Assert.Contains("_repository.GetHierarchyAsync()", source, StringComparison.Ordinal);
        Assert.Contains("FastGalleryYearCatalog.FromHierarchy", source, StringComparison.Ordinal);
        Assert.Contains("Interlocked.Increment(ref _scopeGeneration)", source, StringComparison.Ordinal);
        Assert.Contains("request.Generation == Volatile.Read(ref _scopeGeneration)", source, StringComparison.Ordinal);
        Assert.Contains("!request.Cancellation.IsCancellationRequested", source, StringComparison.Ordinal);
        Assert.Contains("GalleryBrowseScopeQueryMapper.ToQueries", source, StringComparison.Ordinal);
        Assert.Contains("GalleryBrowseTree.ForYear", source, StringComparison.Ordinal);
        Assert.Contains("GalleryBrowseTree.CreateFocus", source, StringComparison.Ordinal);
        Assert.Contains("GalleryPlaceYearCatalog.FindSiblings", source, StringComparison.Ordinal);
        Assert.Contains("_placeTreesByYear.TryGetValue", source, StringComparison.Ordinal);
        Assert.Contains("private void PreparePlaceTree(", source, StringComparison.Ordinal);
        Assert.Contains("GalleryBrowseCatalog.Search", source, StringComparison.Ordinal);
        Assert.Contains("_contextHistory.Remember(CurrentContext, returnMode, context)", source, StringComparison.Ordinal);
        Assert.Contains("_contextHistory.TryPop(out var previous)", source, StringComparison.Ordinal);
        Assert.Contains("previous.ReturnMode == MobileGalleryViewMode.PlaceSelection", source, StringComparison.Ordinal);
        Assert.Contains("_placeTreeYear != year.Value", source, StringComparison.Ordinal);
        Assert.Contains("node.IsExpanded = !node.IsExpanded", source, StringComparison.Ordinal);
        Assert.Contains("node.IsCurrent = string.Equals(node.Key, currentKey", source, StringComparison.Ordinal);
        Assert.Contains("MobileGalleryContext.FromOption(option)", source, StringComparison.Ordinal);
        Assert.Contains("CurrentContext.Scope", source, StringComparison.Ordinal);
        Assert.Contains("var needsReload = !Equals(_loadedScope, context.Scope)", source, StringComparison.Ordinal);
        Assert.Contains("ViewMode = MobileGalleryViewMode.Gallery", source, StringComparison.Ordinal);
        Assert.Contains("ClearSearchPresentation();", source, StringComparison.Ordinal);
        Assert.Contains("IsEmptyStateVisible => !IsInitialLoading", source, StringComparison.Ordinal);

        var cancelStart = source.IndexOf("private Task CancelSearchAsync()", StringComparison.Ordinal);
        var cancelEnd = source.IndexOf("public async Task NavigateBackAsync()", cancelStart, StringComparison.Ordinal);
        Assert.True(cancelStart >= 0 && cancelEnd > cancelStart);
        var cancelBody = source[cancelStart..cancelEnd];
        Assert.Contains("ViewMode = MobileGalleryViewMode.Gallery", cancelBody, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentContext =", cancelBody, StringComparison.Ordinal);

        var toggleStart = source.IndexOf("private void TogglePlaceTreeNode", StringComparison.Ordinal);
        var toggleEnd = source.IndexOf("private Task SelectPlaceTreeNodeAsync", toggleStart, StringComparison.Ordinal);
        var selectEnd = source.IndexOf("private void RebuildVisiblePlaceTree", toggleEnd, StringComparison.Ordinal);
        Assert.True(toggleStart >= 0 && toggleEnd > toggleStart && selectEnd > toggleEnd);
        var toggleBody = source[toggleStart..toggleEnd];
        var selectBody = source[toggleEnd..selectEnd];
        Assert.Contains("node.IsExpanded = !node.IsExpanded", toggleBody, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyOptionSafelyAsync", toggleBody, StringComparison.Ordinal);
        Assert.Contains("ApplyOptionSafelyAsync", selectBody, StringComparison.Ordinal);
        Assert.DoesNotContain("IsExpanded =", selectBody, StringComparison.Ordinal);
    }

    [Fact]
    public void GalleryNavigation_ReusesCurrentContextForSearchTreeAndSiblingYears()
    {
        var source = File.ReadAllText(FindSourceFile("MemoryKeeper.Mobile", "ViewModels", "HomeViewModel.cs"));

        Assert.Contains("MobileGalleryContext.FromOption(option)", source, StringComparison.Ordinal);
        Assert.Contains("PreparePlaceTree(CurrentContext.Scope, focusCurrentPath: true)", source, StringComparison.Ordinal);
        Assert.Contains("CollapsePlaceTree(root)", source, StringComparison.Ordinal);
        Assert.Contains("PlaceTreeFocusRequested?.Invoke(currentNode)", source, StringComparison.Ordinal);
        Assert.Contains("PrepareSiblingYearOptions(CurrentContext)", source, StringComparison.Ordinal);
        Assert.Contains("GalleryTitle: sibling.DisplayName", source, StringComparison.Ordinal);
        Assert.Contains("returnMode: MobileGalleryViewMode.PlaceSelection", source, StringComparison.Ordinal);
        Assert.Contains("returnMode: MobileGalleryViewMode.Gallery", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetPhotos", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Search_DoesNotAutoNavigateWhenOnlyOneHierarchyResultExists()
    {
        var source = File.ReadAllText(FindSourceFile("MemoryKeeper.Mobile", "ViewModels", "HomeViewModel.cs"));

        Assert.DoesNotContain("SearchResults.Count == 1", source, StringComparison.Ordinal);
        Assert.Contains("SelectedSearchOptionChanged", source, StringComparison.Ordinal);
    }

    private static string FindSourceFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Source file was not found: {Path.Combine(parts)}");
    }
}
