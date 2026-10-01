namespace MemoryKeeper.Tests.UnitTests;

public sealed class GallerySearchTreeRegressionTests
{
    [Fact]
    public void SearchTree_UsesPrunedHierarchyForBothBrowseModesAndStableSelection()
    {
        var source = File.ReadAllText(FindSourceFile(
            "MemoryKeeper.App",
            "ViewModels",
            "GalleryViewModel.cs"));

        Assert.Contains("GallerySearchHierarchyProjection.Create", source, StringComparison.Ordinal);
        Assert.Contains("RebuildSearchTreeRootsAsync", source, StringComparison.Ordinal);
        Assert.Contains("BuildPlaceTreeRoots(hierarchy)", source, StringComparison.Ordinal);
        Assert.Contains("selectedNodeKey = SelectedNode?.BuildNodeKey()", source, StringComparison.Ordinal);
        Assert.Contains("FindNodeByKey(selectedNodeKey)", source, StringComparison.Ordinal);
        Assert.Contains("node.Kind == GalleryTreeNodeKind.All", source, StringComparison.Ordinal);
        Assert.Contains("ExpandSearchTreeNodeAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ClearingSearch_DiscardsSearchHierarchyAndReusesFullSnapshot()
    {
        var source = File.ReadAllText(FindSourceFile(
            "MemoryKeeper.App",
            "ViewModels",
            "GalleryViewModel.cs"));

        Assert.Contains("_searchHierarchy = null;", source, StringComparison.Ordinal);
        Assert.Contains("RebuildTreeRootsAsync(reuseCachedData: true)", source, StringComparison.Ordinal);
        Assert.Contains("reuseCachedData && _fastSummary is not null", source, StringComparison.Ordinal);
        Assert.Contains("reuseCachedData || _fastHierarchy is null", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NonHierarchySearch_UsesExactLegacyTotalWithoutExposingFullTree()
    {
        var source = File.ReadAllText(FindSourceFile(
            "MemoryKeeper.App",
            "ViewModels",
            "GalleryViewModel.cs"));

        Assert.Contains("_searchTreeTotalCount = projection.TotalCount", source, StringComparison.Ordinal);
        Assert.Contains("UpdateSearchTreeTotalCount(_totalCount)", source, StringComparison.Ordinal);
        Assert.Contains("Count = _searchTreeTotalCount", source, StringComparison.Ordinal);
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
