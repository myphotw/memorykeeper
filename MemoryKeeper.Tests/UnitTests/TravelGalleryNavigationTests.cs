using MemoryKeeper.App.Services;
using MemoryKeeper.Application.Navigation;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class TravelGalleryNavigationTests
{
    [Theory]
    [InlineData(GalleryPlaceScope.International, GalleryPlaceNavigationLevel.Countries)]
    [InlineData(GalleryPlaceScope.International, GalleryPlaceNavigationLevel.Places)]
    [InlineData(GalleryPlaceScope.Domestic, GalleryPlaceNavigationLevel.Places)]
    [InlineData(GalleryPlaceScope.International, GalleryPlaceNavigationLevel.Photos)]
    [InlineData(GalleryPlaceScope.Domestic, GalleryPlaceNavigationLevel.Photos)]
    public void RequestPlaceBrowse_CarriesTheRequestedGalleryModeAndScope(
        GalleryPlaceScope scope,
        GalleryPlaceNavigationLevel level)
    {
        var state = new GalleryFocusState();

        state.RequestPlaceBrowse(scope, level);

        var request = Assert.IsType<GalleryFocusSnapshot>(state.ConsumeRestore());
        Assert.Equal(1, request.BrowseModeIndex);
        Assert.Equal(scope, request.PlaceScope);
        Assert.Equal(level, request.RequestedPlaceLevel);
    }

    [Fact]
    public void TravelSummaryBindsAllFiveGalleryCommandsAndUsesExistingDrillDownNavigation()
    {
        var page = LoadSource("MemoryKeeper.App", "Views", "TravelRecordsPage.xaml");
        var viewModel = LoadSource("MemoryKeeper.App", "ViewModels", "TravelRecordsViewModel.cs");
        var mainWindow = LoadSource("MemoryKeeper.App", "Views", "MainWindow.xaml.cs");

        foreach (var command in new[]
                 {
                     "OpenForeignCountriesCommand",
                     "OpenForeignPlacesCommand",
                     "OpenDomesticPlacesCommand",
                     "OpenForeignPhotosCommand",
                     "OpenDomesticPhotosCommand",
                 })
        {
            Assert.Contains($"Command=\"{{Binding {command}}}\"", page, StringComparison.Ordinal);
        }

        Assert.Contains("GalleryPlaceScope.International", viewModel, StringComparison.Ordinal);
        Assert.Contains("GalleryPlaceScope.Domestic", viewModel, StringComparison.Ordinal);
        Assert.Contains("galleryFocus.RequestPlaceBrowse(e.Scope, e.Level)", mainWindow, StringComparison.Ordinal);
        Assert.Contains("NavigateDrillDown(", mainWindow, StringComparison.Ordinal);
        Assert.Contains("$\"travel-gallery:{e.Scope}:{e.Level}\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("Kind: NavigationKind.DrillDown", mainWindow, StringComparison.Ordinal);
        Assert.Contains("RootTag: \"travel\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("contextKey.StartsWith(\"travel-gallery:\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<IGalleryFocusState>().Clear()", mainWindow, StringComparison.Ordinal);
    }

    [Fact]
    public void Clear_RemovesPendingTravelScopeBeforeLaterTopLevelGalleryEntry()
    {
        var state = new GalleryFocusState();
        state.RequestPlaceBrowse(GalleryPlaceScope.International, GalleryPlaceNavigationLevel.Photos);

        state.Clear();

        Assert.False(state.HasPendingRestore);
        Assert.Null(state.ConsumeRestore());
    }

    [Theory]
    [InlineData("International", "Countries")]
    [InlineData("International", "Places")]
    [InlineData("Domestic", "Places")]
    [InlineData("International", "Photos")]
    [InlineData("Domestic", "Photos")]
    public void TravelGalleryEntriesHaveTravelBackHistoryWhileTopLevelGalleryDoesNot(
        string scope,
        string level)
    {
        var navigation = new NavigationService();
        var travel = NavigationEntry.TopLevel("travel", "여행기록");
        navigation.NavigateRoot(travel);
        navigation.Navigate(NavigationEntry.DrillDown(
            "gallery",
            "travel",
            $"{scope} {level}",
            $"travel-gallery:{scope}:{level}"));

        Assert.Equal(NavigationKind.DrillDown, navigation.Current?.Kind);
        Assert.True(navigation.CanGoBack);
        Assert.Equal("여행기록", navigation.BackEntry?.DisplayLabel);
        Assert.True(navigation.TryGoBack(out var destination));
        Assert.True(destination.HasSameIdentity(travel));

        navigation.NavigateRoot(NavigationEntry.TopLevel("gallery", "사진첩"));
        Assert.Equal(NavigationKind.TopLevel, navigation.Current?.Kind);
        Assert.False(navigation.CanGoBack);
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
