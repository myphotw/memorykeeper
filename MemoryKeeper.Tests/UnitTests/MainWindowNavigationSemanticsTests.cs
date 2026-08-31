namespace MemoryKeeper.Tests.UnitTests;

public class MainWindowNavigationSemanticsTests
{
    [Fact]
    public void TopNavigation_UsesSingleItemInvokedEntryPoint()
    {
        var xaml = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Views", "MainWindow.xaml"));
        var source = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Views", "MainWindow.xaml.cs"));

        Assert.Contains("ItemInvoked=\"RootNavigation_OnItemInvoked\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectionChanged=\"RootNavigation_OnSelectionChanged\"", xaml, StringComparison.Ordinal);
        Assert.Contains("if (IsTopLevelTag(tag))", source, StringComparison.Ordinal);
        Assert.Contains("NavigateTopLevel(tag);", source, StringComparison.Ordinal);
        Assert.Contains("_navigation.NavigateRoot(entry);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RepresentativeFlows_DeclareDrillDownViewerAndTravelContext()
    {
        var source = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Views", "MainWindow.xaml.cs"));

        Assert.Contains("private void NavigateDrillDown(", source, StringComparison.Ordinal);
        Assert.Contains("private void NavigateViewer(", source, StringComparison.Ordinal);
        Assert.Contains("CreateTravelDetailEntry()", source, StringComparison.Ordinal);
        Assert.Contains("\"recent\"", source, StringComparison.Ordinal);
        Assert.Contains("SelectNavigationItem(CreateTravelDetailEntry());", source, StringComparison.Ordinal);
        Assert.Contains("NavigateViewer(tag);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RootHighlightAndTopLevelReset_UseNavigationSemantics()
    {
        var source = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Views", "MainWindow.xaml.cs"));

        Assert.Contains("entry.RootTag ?? MapToTopNavTag(entry.Tag)", source, StringComparison.Ordinal);
        Assert.Contains("ClearPendingDestinationState();", source, StringComparison.Ordinal);
        Assert.Contains("_placeFocusState.ClearFocus();", source, StringComparison.Ordinal);
        Assert.Contains("_placeFocusState.ClearFilters();", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_photoNavigationState.ReturnSourceTag)", source, StringComparison.Ordinal);
    }

    private static string FindSourceFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "MemoryKeeper.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(new[] { directory!.FullName }.Concat(segments).ToArray());
    }
}
