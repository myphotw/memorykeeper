namespace MemoryKeeper.Tests.UnitTests;

public sealed class TravelCountryListRegressionTests
{
    [Fact]
    public void ForeignCountrySummary_UsesDashboardCountryStatisticsAndExistingGalleryNavigation()
    {
        var service = LoadSource("MemoryKeeper.Application", "Services", "TravelRecordsService.cs");
        var viewModel = LoadSource("MemoryKeeper.App", "ViewModels", "TravelCountryListViewModel.cs");
        var page = LoadSource("MemoryKeeper.App", "Views", "TravelCountryListPage.xaml");
        var travelPage = LoadSource("MemoryKeeper.App", "Views", "TravelRecordsPage.xaml");
        var mainWindow = LoadSource("MemoryKeeper.App", "Views", "MainWindow.xaml.cs");
        var gallery = LoadSource("MemoryKeeper.App", "ViewModels", "GalleryViewModel.cs");

        Assert.Contains("BuildForeignCountries(countryAggregates)", service, StringComparison.Ordinal);
        Assert.Contains("VisitCount = statistic.VisitCount", service, StringComparison.Ordinal);
        Assert.Contains("PhotoCount = item.Item.PhotoCount", service, StringComparison.Ordinal);
        Assert.Contains("GetCountryAggregatesAsync", service, StringComparison.Ordinal);
        Assert.Contains("_navigationState.ForeignCountries", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("TravelRecordsService", viewModel, StringComparison.Ordinal);
        Assert.Contains("Text=\"해외 방문 국가\"", page, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding GoBackCommand}\"", page, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Countries}\"", page, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding VisitCountText}\"", page, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding PhotoCountText}\"", page, StringComparison.Ordinal);
        Assert.Contains("Source=\"{Binding ThumbnailImage}\"", page, StringComparison.Ordinal);
        Assert.Contains("OpenForeignCountriesCommand", travelPage, StringComparison.Ordinal);
        Assert.Contains("galleryFocus.RequestPlaceBrowse(e.Scope, e.Level)", mainWindow, StringComparison.Ordinal);
        Assert.Contains("RequestCountryFilter(country)", mainWindow, StringComparison.Ordinal);
        Assert.Contains("NavigateDrillDown(\"gallery\", $\"country:{country}\", country)", mainWindow, StringComparison.Ordinal);
        Assert.Contains("\"travel-detail\" or \"travel-countries\" or \"travel\" => \"travel\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("GalleryPlaceHierarchyProjection.Build", gallery, StringComparison.Ordinal);
        Assert.Contains("SelectCountryFilterAsync", gallery, StringComparison.Ordinal);
    }

    private static string LoadSource(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Source file was not found: {Path.Combine(parts)}");
    }
}
