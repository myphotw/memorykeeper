namespace MemoryKeeper.Tests.UnitTests;

public sealed class TravelRecordsDetailPageRegressionTests
{
    [Fact]
    public void DetailPage_UsesSharedCardStyles_AndUsesRankOnlyForRankedListTemplates()
    {
        var xaml = File.ReadAllText(FindSourceFile(
            "MemoryKeeper.App", "Views", "TravelRecordsDetailPage.xaml"));

        Assert.Contains("MkStandardPageContainerStyle", xaml, StringComparison.Ordinal);
        Assert.Contains("MkPageTitleStyle", xaml, StringComparison.Ordinal);
        Assert.Contains("MkCompactCardStyle", xaml, StringComparison.Ordinal);
        Assert.Contains("MkThumbnailBorderStyle", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("TagsText", xaml, StringComparison.Ordinal);
        Assert.Contains("IsRecentDetail", xaml, StringComparison.Ordinal);
        Assert.Contains("IsLongUnvisitedDetail", xaml, StringComparison.Ordinal);
        Assert.Contains("ShowFarthestHomeHint", xaml, StringComparison.Ordinal);

        var placesStart = xaml.IndexOf("ItemsSource=\"{Binding Places, Mode=OneWay}\"", StringComparison.Ordinal);
        var countriesStart = xaml.IndexOf("ItemsSource=\"{Binding Countries, Mode=OneWay}\"", StringComparison.Ordinal);
        var farthestStart = xaml.IndexOf("ItemsSource=\"{Binding FarthestPlaces, Mode=OneWay}\"", StringComparison.Ordinal);

        Assert.True(placesStart >= 0 && countriesStart > placesStart && farthestStart > countriesStart);
        Assert.DoesNotContain("Text=\"{Binding Rank}\"", xaml[placesStart..countriesStart], StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Rank}\"", xaml[farthestStart..], StringComparison.Ordinal);
    }

    [Fact]
    public void DetailViewModel_PreservesTheActiveMode_WhenNoNewModeIsPending()
    {
        var source = File.ReadAllText(FindSourceFile(
            "MemoryKeeper.App", "ViewModels", "TravelRecordsDetailViewModel.cs"));

        Assert.Contains(
            "requestedKind ?? _activeDetailKind ?? TravelRecordsDetailKind.MostVisited",
            source,
            StringComparison.Ordinal);
        Assert.Contains("_activeDetailKind = kind;", source, StringComparison.Ordinal);
        Assert.Contains("OpenPlace", source, StringComparison.Ordinal);
        Assert.Contains("OpenFarthest", source, StringComparison.Ordinal);
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
