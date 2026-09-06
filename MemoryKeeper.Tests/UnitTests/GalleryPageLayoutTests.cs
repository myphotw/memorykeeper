namespace MemoryKeeper.Tests.UnitTests;

public sealed class GalleryPageLayoutTests
{
    [Fact]
    public void Gallery_RemovesRepresentativeHero_AndPlacesGridDirectlyBelowHeader()
    {
        var xaml = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Views", "GalleryPage.xaml"));
        var codeBehind = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Views", "GalleryPage.xaml.cs"));

        Assert.DoesNotContain("HeroThumbHost", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("HeroThumbImage", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("HeroThumbHost", codeBehind, StringComparison.Ordinal);
        Assert.Contains("RowDefinitions=\"Auto,*\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<!-- Photo grid -->", xaml, StringComparison.Ordinal);
        Assert.Contains("<Grid Grid.Row=\"1\">", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"DetailCard\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenMediaViewerButton", xaml, StringComparison.Ordinal);
        Assert.Contains("DoubleTapped=\"PhotoCard_OnDoubleTapped\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.OpenPhotoViewerCommand.Execute(item)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Text=\"상세\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"PhotoDetail_OnClick\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void GalleryThumbnailCards_PreserveWholeImageContext()
    {
        var xaml = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Views", "GalleryPage.xaml"));
        var cardStart = xaml.IndexOf("Style=\"{StaticResource GalleryPhotoCardStyle}\"", StringComparison.Ordinal);
        var cardEnd = xaml.IndexOf("<!-- Soft primary wash", cardStart, StringComparison.Ordinal);

        Assert.True(cardStart >= 0 && cardEnd > cardStart);
        var card = xaml[cardStart..cardEnd];
        Assert.Contains("Source=\"{Binding ThumbnailImage, Mode=OneWay}\"", card, StringComparison.Ordinal);
        Assert.Contains("Stretch=\"Uniform\"", card, StringComparison.Ordinal);
        Assert.DoesNotContain("Stretch=\"UniformToFill\"", card, StringComparison.Ordinal);
    }

    [Fact]
    public void GalleryShortcuts_UseAuthoritativeCountsAndPlaceCleanupRoute()
    {
        var gallery = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "ViewModels", "GalleryViewModel.cs"));
        var page = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Views", "GalleryPage.xaml.cs"));
        var pending = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "ViewModels", "PendingMemoryViewModel.cs"));
        var pendingView = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Views", "PendingMemoryView.xaml"));

        Assert.Contains("Count = summary.FavoriteCount", gallery, StringComparison.Ordinal);
        Assert.Contains("Count = summary.RecentCount", gallery, StringComparison.Ordinal);
        Assert.Contains("Title = \"장소 정리 필요\"", gallery, StringComparison.Ordinal);
        Assert.Contains("Count = summary.PlaceCleanupCount", gallery, StringComparison.Ordinal);
        Assert.Contains("GalleryTreeNodeKind.Pending", gallery, StringComparison.Ordinal);
        Assert.Contains("OpenPendingRequested?.Invoke", page, StringComparison.Ordinal);
        Assert.Contains("GetPlaceCleanupMemoriesAsync", pending, StringComparison.Ordinal);
        Assert.DoesNotContain("GetPendingMemoriesAsync", pending, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding LoadMoreCleanupCommand}\"", pendingView, StringComparison.Ordinal);
        Assert.Contains("CleanupProgressText", pendingView, StringComparison.Ordinal);
        Assert.Contains("LibraryConstants.UnclassifiedTitle", gallery, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaceCleanup_SelectionAndRefreshGuardsArePreserved()
    {
        var model = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Models", "PendingMemoryGroupItem.cs"));
        var view = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Views", "PendingMemoryView.xaml"));
        var codeBehind = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Views", "PendingMemoryView.xaml.cs"));
        var pending = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "ViewModels", "PendingMemoryViewModel.cs"));
        var cleanupDiagnostics = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Diagnostics", "PlaceCleanupDiagnostics.cs"));
        var gallery = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "ViewModels", "GalleryViewModel.cs"));

        Assert.Contains("IsIncluded = false", model, StringComparison.Ordinal);
        Assert.Contains("item.IsIncluded = true", pending, StringComparison.Ordinal);
        Assert.Contains("if (item is null || IsSelectionMode)", pending, StringComparison.Ordinal);
        Assert.Contains("ViewModel.ActivateMediaCommand.Execute(item)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ViewModel.OpenPhotoDetailCommand.Execute(item)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Tapped=\"IncludeCheckBox_OnTapped\"", view, StringComparison.Ordinal);
        Assert.Contains("ApplyOverview(overview, preserveSelection: false)", pending, StringComparison.Ordinal);
        Assert.Contains("ApplyOverview(overview, preserveSelection: true)", pending, StringComparison.Ordinal);
        Assert.Contains("_loadedMediaItems = overview.Items", pending, StringComparison.Ordinal);
        Assert.Contains("ActiveMediaItems = new ObservableCollection<PendingMemoryMediaItem>(_loadedMediaItems)", pending, StringComparison.Ordinal);
        Assert.Contains("media.WithEffectiveGeography(registeredPlace)", model, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding SelectAllCleanupCommand}\"", view, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding GeographyText}\"", view, StringComparison.Ordinal);
        Assert.Contains("PLACE_CLEANUP_ASSIGN_DIAG", pending, StringComparison.Ordinal);
        Assert.Contains("post_reload_cleanup_selected_count", pending, StringComparison.Ordinal);
        Assert.Contains("post_reload_with_place_id_count", pending, StringComparison.Ordinal);
        Assert.Contains("PlaceCleanupDiagnostics.WriteAssignment", pending, StringComparison.Ordinal);
        Assert.Contains("place-cleanup-diag.log", cleanupDiagnostics, StringComparison.Ordinal);
        Assert.Contains("StartupDiagnostics.LogDirectory", cleanupDiagnostics, StringComparison.Ordinal);
        Assert.Contains("catch", cleanupDiagnostics, StringComparison.Ordinal);
        Assert.Contains("finally", pending, StringComparison.Ordinal);
        Assert.Contains("await LoadCoreAsync();", pending, StringComparison.Ordinal);
        Assert.Contains("_fastHierarchy = null;", gallery, StringComparison.Ordinal);
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
