using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class PlaceManagementExperienceTests
{
    [Fact]
    public void HierarchyProjection_KeepsSameUuidCanonicalPathsAndDifferentUuidNamesDistinct()
    {
        var sharedId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var places = new[]
        {
            Place(sharedId, "헐후메일 퍼블릭 비치"),
            Place(otherId, "헐후메일 퍼블릭 비치"),
        };
        var paths = new[]
        {
            HierarchyPath(sharedId, "male-path", "몰디브", "말레", 11),
            HierarchyPath(sharedId, "lot-path", "몰디브", "Lot", 1),
            HierarchyPath(otherId, "busan-path", "대한민국", "부산", 2),
        };

        var result = PlacePickerHierarchyProjection.Build(places, paths, includeInactive: true);

        Assert.Equal(2, result.AllPathItems.Count(item => item.Id == sharedId));
        Assert.All(result.AllPathItems.Where(item => item.Id == sharedId), item => Assert.True(item.HasMultipleHierarchyPaths));
        Assert.Single(result.AllPathItems.Where(item => item.Id == otherId));
    }

    [Fact]
    public void PlaceManagement_UsesLinkedPhotosAndRadiusImpactAsDifferentCounts()
    {
        var xaml = ReadSource("MemoryKeeper.App", "Views", "PlaceManagementView.xaml");
        var viewModel = ReadSource("MemoryKeeper.App", "ViewModels", "PlaceManagementViewModel.cs");

        Assert.Contains("PlaceHierarchy", xaml, StringComparison.Ordinal);
        Assert.Contains("LinkedPhotoCountText", xaml, StringComparison.Ordinal);
        Assert.Contains("IncludedPhotoCountText", xaml, StringComparison.Ordinal);
        Assert.Contains("GetLinkedPhotosAsync", viewModel, StringComparison.Ordinal);
        Assert.Contains("GetRadiusImpactAsync", viewModel, StringComparison.Ordinal);
        Assert.Contains("PlaceLinkedPhotoCountPolicy.Project(hierarchy.Items)", viewModel, StringComparison.Ordinal);
        Assert.Contains("LinkedPhotoCountText", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Run Text=\"{Binding MediaCount}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("실제 연결 사진", viewModel, StringComparison.Ordinal);
        Assert.Contains("반경 내 사진", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaceManagement_CombinesHierarchySearchWithExactPhotoCountFilter()
    {
        var xaml = ReadSource("MemoryKeeper.App", "Views", "PlaceManagementView.xaml");
        var viewModel = ReadSource("MemoryKeeper.App", "ViewModels", "PlaceManagementViewModel.cs");

        Assert.Contains("Header=\"사진 필터\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PhotoCountFilterOptions", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedPhotoCountFilter", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding SelectedPlace, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectionChanged=\"PlaceList_OnSelectionChanged\"", xaml, StringComparison.Ordinal);
        Assert.Contains("\"전체\"", viewModel, StringComparison.Ordinal);
        Assert.Contains("\"사진 있음\"", viewModel, StringComparison.Ordinal);
        Assert.Contains("\"사진 없음\"", viewModel, StringComparison.Ordinal);
        Assert.Contains("PlaceLinkedPhotoCountPolicy.Matches", viewModel, StringComparison.Ordinal);
        Assert.Contains("ListSearchText", viewModel, StringComparison.Ordinal);
        Assert.Contains("ApplyHierarchyScope", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaceManagement_MergeDeleteAndReverseGeocodeKeepSafetyContracts()
    {
        var viewModel = ReadSource("MemoryKeeper.App", "ViewModels", "PlaceManagementViewModel.cs");
        var service = ReadSource("MemoryKeeper.Application", "Services", "MemoryKeeperPlaceService.cs");

        Assert.Contains("GetMergePreviewAsync", viewModel, StringComparison.Ordinal);
        Assert.Contains("var sourceRevision = preview.SourceRevision > 0", viewModel, StringComparison.Ordinal);
        Assert.Contains("var targetRevision = preview.TargetRevision > 0", viewModel, StringComparison.Ordinal);
        Assert.Contains("ExpectedSourceRevision = sourceRevision", viewModel, StringComparison.Ordinal);
        Assert.Contains("ExpectedTargetRevision = targetRevision", viewModel, StringComparison.Ordinal);
        Assert.Contains("실제 연결 사진", viewModel, StringComparison.Ordinal);
        Assert.Contains("삭제할 수 없습니다. 다른 장소와 병합하세요", viewModel, StringComparison.Ordinal);
        Assert.Contains("DisplayName = originalName", viewModel, StringComparison.Ordinal);
        Assert.Contains("LatitudeText = originalLatitude", viewModel, StringComparison.Ordinal);
        Assert.Contains("RadiusText = originalRadius", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("미등록 상태로 전환", service, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaceManagement_UsesThreeColumnExplorerLayoutWithoutDroppingExistingActions()
    {
        var xaml = ReadSource("MemoryKeeper.App", "Views", "PlaceManagementView.xaml");
        var codeBehind = ReadSource("MemoryKeeper.App", "Views", "PlaceManagementView.xaml.cs");
        var viewModel = ReadSource("MemoryKeeper.App", "ViewModels", "PlaceManagementViewModel.cs");
        var settingsXaml = ReadSource("MemoryKeeper.App", "Views", "SettingsPage.xaml");

        Assert.Contains("x:Name=\"TreeColumn\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ListColumn\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DetailColumn\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CompactPlaceLayout\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"WidePlaceLayout\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MinWidth=\"1060\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PlaceHierarchyTree\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding FilteredPlaces, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding SelectedPlace, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectionChanged=\"PlaceList_OnSelectionChanged\"", xaml, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxHeight=\"300\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DetailActionBar\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DetailScrollViewer\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsInlineStatusVisible", xaml, StringComparison.Ordinal);
        Assert.True(
            xaml.IndexOf("x:Name=\"DetailActionBar\"", StringComparison.Ordinal)
            < xaml.IndexOf("x:Name=\"DetailScrollViewer\"", StringComparison.Ordinal));
        Assert.Contains("<Expander IsExpanded=\"True\">", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Height=\"{Binding ActualHeight, ElementName=SettingsNavigation}\"", settingsXaml, StringComparison.Ordinal);

        Assert.Contains("SelectHierarchyCountry", codeBehind, StringComparison.Ordinal);
        Assert.Contains("SelectHierarchyRegion", codeBehind, StringComparison.Ordinal);
        Assert.Contains("HierarchyScopeText", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("StatusMessage = message;", viewModel, StringComparison.Ordinal);
        Assert.Contains("ReverseGeocodeCurrentCoordinatesCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("MergeIntoSelectedTargetCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("DeleteCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("LoadMoreLinkedPhotosCommand", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaceManagement_MapWheelScrollsDetailsAndKeepsExplicitMapZoom()
    {
        var view = ReadSource("MemoryKeeper.App", "Views", "PlaceManagementView.xaml.cs");
        var controller = ReadSource("MemoryKeeper.App", "Maps", "Google", "GoogleMapController.cs");
        var googleHtml = ReadSource("MemoryKeeper.App", "Maps", "Google", "GoogleMapHtmlBuilder.cs");
        var osmHtml = ReadSource("MemoryKeeper.App", "Maps", "Google", "OpenStreetMapHtmlBuilder.cs");

        Assert.Contains("HostScrollRequested += OnMapHostScrollRequested", view, StringComparison.Ordinal);
        Assert.Contains("DetailScrollViewer.ChangeView", view, StringComparison.Ordinal);
        Assert.Contains("type is \"hostScroll\"", controller, StringComparison.Ordinal);
        Assert.Contains("event.ctrlKey", googleHtml, StringComparison.Ordinal);
        Assert.Contains("scrollwheel: false", googleHtml, StringComparison.Ordinal);
        Assert.Contains("zoomControl: true", googleHtml, StringComparison.Ordinal);
        Assert.Contains("event.ctrlKey", osmHtml, StringComparison.Ordinal);
        Assert.Contains("scrollWheelZoom: false", osmHtml, StringComparison.Ordinal);
        Assert.Contains("zoomControl: true", osmHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaceManagement_GalleryTabsStayInsideSettingsAndPreserveEditState()
    {
        var settingsXaml = ReadSource("MemoryKeeper.App", "Views", "SettingsPage.xaml");
        var placeXaml = ReadSource("MemoryKeeper.App", "Views", "PlaceManagementView.xaml");
        var galleryXaml = ReadSource("MemoryKeeper.App", "Views", "GalleryPage.xaml");
        var galleryCode = ReadSource("MemoryKeeper.App", "Views", "GalleryPage.xaml.cs");
        var placeCode = ReadSource("MemoryKeeper.App", "Views", "PlaceManagementView.xaml.cs");
        var settingsCode = ReadSource("MemoryKeeper.App", "Views", "SettingsPage.xaml.cs");
        var mainWindow = ReadSource("MemoryKeeper.App", "Views", "MainWindow.xaml.cs");
        var viewModel = ReadSource("MemoryKeeper.App", "ViewModels", "PlaceManagementViewModel.cs");

        Assert.Contains("x:Name=\"SettingsNavigation\"", settingsXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GalleryWorkspaceTabButton\"", settingsXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PlaceWorkspaceTabButton\"", settingsXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PlaceHost\"", settingsXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"EmbeddedGalleryHost\"", settingsXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"사진첩\"", placeXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"PlaceManagementTabHeader\"", galleryXaml, StringComparison.Ordinal);
        Assert.Contains("ConfigureEmbeddedInSettings", galleryCode, StringComparison.Ordinal);
        Assert.Contains("BackNavigationButton.Visibility = Visibility.Collapsed", galleryCode, StringComparison.Ordinal);
        Assert.Contains("GalleryPage embeddedGalleryPage", settingsCode, StringComparison.Ordinal);
        Assert.Contains("EmbeddedGalleryHost.Content = _embeddedGalleryPage", settingsCode, StringComparison.Ordinal);
        Assert.Contains("PlaceHost.Visibility = Visibility.Collapsed", settingsCode, StringComparison.Ordinal);
        Assert.Contains("EmbeddedGalleryHost.Visibility = Visibility.Collapsed", settingsCode, StringComparison.Ordinal);
        Assert.Contains("PreserveForGalleryRoundTrip", placeCode, StringComparison.Ordinal);
        Assert.Contains("if (_preserveOnNextUnload)", placeCode, StringComparison.Ordinal);
        Assert.Contains("_preservePlaceStateOnNextActivation", settingsCode, StringComparison.Ordinal);
        Assert.Contains("IsPlaceWorkspaceRoundTripPending", settingsCode, StringComparison.Ordinal);
        Assert.Contains("PreparePlaceWorkspaceRoundTrip", settingsCode, StringComparison.Ordinal);
        Assert.Contains("GetOrCreatePage<GalleryPage>(\"gallery\")", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("PlaceManagementGalleryContextPrefix", mainWindow, StringComparison.Ordinal);
        Assert.Contains("if (!preservePlaceState)", mainWindow, StringComparison.Ordinal);
        Assert.Contains("ContentFrame.Content is SettingsPage settingsPage", mainWindow, StringComparison.Ordinal);
        Assert.Contains("OpenGalleryForPlaceRequested", viewModel, StringComparison.Ordinal);
        Assert.Contains("if (!loaded || SelectedPlace?.Id != selectedPlaceId)", viewModel, StringComparison.Ordinal);
        Assert.Contains("if (LinkedPhotoCount <= 0)", viewModel, StringComparison.Ordinal);
        Assert.Contains("ViewModel.UseIndependentFocusState()", galleryCode, StringComparison.Ordinal);
        Assert.Contains("_embeddedGalleryPage.ViewModel.RequestPlaceFilter(targetPlaceId)", settingsCode, StringComparison.Ordinal);
        Assert.DoesNotContain("NavigateDrillDown(\n            \"gallery\"", settingsCode, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaceManagement_SuccessfulCatalogInvalidationRefreshesEmbeddedGalleryOnceAndPreservesFocus()
    {
        var settings = ReadSource("MemoryKeeper.App", "Views", "SettingsPage.xaml.cs");
        var galleryPage = ReadSource("MemoryKeeper.App", "Views", "GalleryPage.xaml.cs");
        var galleryViewModel = ReadSource("MemoryKeeper.App", "ViewModels", "GalleryViewModel.cs");
        var placeService = ReadSource("MemoryKeeper.Application", "Services", "MemoryKeeperPlaceService.cs");

        Assert.Contains("_catalogInvalidation.Invalidated += OnCatalogInvalidated", settings, StringComparison.Ordinal);
        Assert.Contains("(e.Surfaces & CatalogSurface.Gallery) == 0", settings, StringComparison.Ordinal);
        Assert.Contains("Interlocked.Exchange(ref _pendingEmbeddedGalleryInvalidationGeneration", settings, StringComparison.Ordinal);
        Assert.Contains("invalidationGeneration > _lastAppliedEmbeddedGalleryInvalidationGeneration", settings, StringComparison.Ordinal);
        Assert.Contains("await _embeddedGalleryPage.ReloadPreservingFocusAsync()", settings, StringComparison.Ordinal);
        Assert.Contains("_lastAppliedEmbeddedGalleryInvalidationGeneration = Math.Max", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("_catalogInvalidation.Consume", settings, StringComparison.Ordinal);

        var reloadStart = galleryPage.IndexOf("public async Task ReloadPreservingFocusAsync()", StringComparison.Ordinal);
        var reloadEnd = galleryPage.IndexOf("private void GalleryPage_OnUnloaded", reloadStart, StringComparison.Ordinal);
        Assert.True(reloadStart >= 0 && reloadEnd > reloadStart);
        var reload = galleryPage[reloadStart..reloadEnd];
        Assert.Contains("ViewModel.CaptureFocusState(GetGridScrollOffset()", reload, StringComparison.Ordinal);
        Assert.Contains("await ViewModel.LoadCommand.ExecuteAsync(null)", reload, StringComparison.Ordinal);
        Assert.Contains("_fastHierarchy = null;", galleryViewModel, StringComparison.Ordinal);
        Assert.Contains("_registeredPlacePaths = null;", galleryViewModel, StringComparison.Ordinal);

        Assert.Contains("_invalidation.Invalidate(CatalogSurface.AllMemoryKeeper)", placeService, StringComparison.Ordinal);
        Assert.Contains("_invalidation.Invalidate();", placeService, StringComparison.Ordinal);
    }

    [Fact]
    public void RegisteredPlacesMissingFromFastHierarchyRemainDiscoverableByPlaceId()
    {
        var gallery = ReadSource("MemoryKeeper.App", "ViewModels", "GalleryViewModel.cs");
        var projection = ReadSource("MemoryKeeper.Application", "GalleryPlaceHierarchyProjection.cs");
        var placeService = ReadSource("MemoryKeeper.Application", "Services", "MemoryKeeperPlaceService.cs");
        var requestBuilder = ReadSource("MemoryKeeper.Application", "FastGalleryRequestPathBuilder.cs");

        Assert.Contains("GetRegisteredPlacePathsAsync", gallery, StringComparison.Ordinal);
        Assert.Contains("_placeService.GetPlaceHierarchyAsync", gallery, StringComparison.Ordinal);
        Assert.Contains("GalleryPlaceHierarchyProjection.Build(hierarchy, registeredPaths)", gallery, StringComparison.Ordinal);
        Assert.Contains("PlaceLinkedPhotoCountPolicy.Project(registeredPaths)", projection, StringComparison.Ordinal);
        Assert.Contains("photoCount <= 0", projection, StringComparison.Ordinal);
        Assert.Contains("group.Key", projection, StringComparison.Ordinal);
        Assert.Contains("LocationKey: null", projection, StringComparison.Ordinal);
        Assert.Contains("IsRegisteredPlace: true", projection, StringComparison.Ordinal);
        Assert.Contains("MatchesRegisteredPlaceSearch", gallery, StringComparison.Ordinal);
        Assert.Contains("Concat(_registeredPlaceSearchScopes)", gallery, StringComparison.Ordinal);
        Assert.Contains("ExpandAncestorsTo(node)", gallery, StringComparison.Ordinal);
        Assert.Contains("GetPlaceHierarchyAsync", placeService, StringComparison.Ordinal);
        Assert.Contains("[\"place_id\"]", requestBuilder, StringComparison.Ordinal);
    }

    private static PlaceDto Place(Guid id, string name) => new()
    {
        Id = id,
        DisplayName = name,
        IsActive = true,
    };

    private static MemoryKeeperPlaceHierarchyPathApiDto HierarchyPath(
        Guid id,
        string key,
        string country,
        string region,
        int count) => new()
    {
        MemorykeeperPlaceId = id,
        LocationKey = key,
        Country = country,
        Region = region,
        DisplayName = "헐후메일 퍼블릭 비치",
        FullPath = $"{country} > {region} > 헐후메일 퍼블릭 비치",
        PhotoCount = count,
        LocationHierarchyComplete = true,
    };

    private static string ReadSource(params string[] parts)
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
