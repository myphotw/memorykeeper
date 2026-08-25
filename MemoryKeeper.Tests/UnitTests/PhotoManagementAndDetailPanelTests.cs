namespace MemoryKeeper.Tests.UnitTests;

public sealed class PhotoManagementAndDetailPanelTests
{
    [Fact]
    public void Import_Route_Uses_Dedicated_PhotoManagement_Page_With_Shared_State()
    {
        var mainWindow = LoadSource("MemoryKeeper.App", "Views", "MainWindow.xaml.cs");
        var settings = LoadSource("MemoryKeeper.App", "Views", "SettingsPage.xaml.cs");
        var app = LoadSource("MemoryKeeper.App", "App.xaml.cs");

        Assert.Contains("GetRequiredService<PhotoManagementPage>", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("GetRequiredService<ImportPage>", mainWindow, StringComparison.Ordinal);
        Assert.Contains("PhotoManagementView photoManagementView", settings, StringComparison.Ordinal);
        Assert.Contains("PhotoManagementHost.Content = _photoManagementView", settings, StringComparison.Ordinal);
        Assert.Contains("AddSingleton<ImportViewModel>()", app, StringComparison.Ordinal);
        Assert.Contains("AddSingleton<StorageManagementViewModel>()", app, StringComparison.Ordinal);
        Assert.DoesNotContain("ImportCompletedNavigateHome +=", mainWindow, StringComparison.Ordinal);
    }

    [Fact]
    public void PhotoManagement_View_Preserves_Folder_Connection_And_Import_Workflow()
    {
        var management = LoadSource("MemoryKeeper.App", "Views", "PhotoManagementView.xaml");
        var import = LoadSource("MemoryKeeper.App", "Views", "ImportView.xaml");

        Assert.Contains("Storage.ChangeFolderCommand", management, StringComparison.Ordinal);
        Assert.Contains("Storage.CheckConnectionCommand", management, StringComparison.Ordinal);
        Assert.Contains("ImportHost", management, StringComparison.Ordinal);
        foreach (var command in new[] { "ImportCommand", "CancelImportCommand", "RetryImportCommand" })
        {
            Assert.Contains(command, import, StringComparison.Ordinal);
        }

        foreach (var state in new[] { "ProgressValue", "DuplicateCountText", "FailedCountText", "CurrentFileName" })
        {
            Assert.Contains(state, import, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Gallery_Detail_Uses_Local_Reusable_Slide_Panel()
    {
        var xaml = LoadSource("MemoryKeeper.App", "Views", "GalleryPage.xaml");
        var gallery = LoadSource("MemoryKeeper.App", "Views", "GalleryPage.xaml.cs");
        var mainWindow = LoadSource("MemoryKeeper.App", "Views", "MainWindow.xaml.cs");

        Assert.Contains("x:Name=\"DetailPanel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Width=\"420\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"380\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MaxWidth=\"440\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PaneThemeTransition Edge=\"Right\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PhotoDetailHost", xaml, StringComparison.Ordinal);
        Assert.Contains("ShowDetailPanelAsync", gallery, StringComparison.Ordinal);
        Assert.Contains("ViewModel.IsDetailPanelOpen", gallery, StringComparison.Ordinal);
        Assert.Contains("TryCloseDetailPanel", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectNavigationItem(\"photo\")", gallery, StringComparison.Ordinal);
    }

    [Fact]
    public void Standalone_And_Gallery_Detail_Reuse_One_UserControl()
    {
        var page = LoadSource("MemoryKeeper.App", "Views", "PhotoDetailPage.cs");
        var view = LoadSource("MemoryKeeper.App", "Views", "PhotoDetailView.xaml");
        var gallery = LoadSource("MemoryKeeper.App", "Views", "GalleryPage.xaml.cs");

        Assert.Contains("PhotoDetailPage(PhotoDetailView view)", page, StringComparison.Ordinal);
        Assert.Contains("x:Class=\"MemoryKeeper.App.Views.PhotoDetailView\"", view, StringComparison.Ordinal);
        Assert.Contains("PhotoDetailView photoDetailView", gallery, StringComparison.Ordinal);
        Assert.Contains("ConfigurePanelMode", gallery, StringComparison.Ordinal);
        Assert.Contains("LoadMediaAsync(item.MediaId)", gallery, StringComparison.Ordinal);
    }

    [Fact]
    public void Detail_Panel_Updates_Selection_And_Refreshes_After_Delete()
    {
        var gallery = LoadSource("MemoryKeeper.App", "Views", "GalleryPage.xaml.cs");
        var detail = LoadSource("MemoryKeeper.App", "ViewModels", "PhotoDetailViewModel.cs");

        Assert.Contains("if (ViewModel.IsDetailPanelOpen)", gallery, StringComparison.Ordinal);
        Assert.Contains("ShowDetailPanelAsync(item, toggle: false)", gallery, StringComparison.Ordinal);
        Assert.Contains("PhotoDeleted += OnPhotoDeleted", gallery, StringComparison.Ordinal);
        Assert.Contains("ViewModel.Items.Remove(deleted)", gallery, StringComparison.Ordinal);
        Assert.Contains("PhotoDeleted?.Invoke(this, MediaId)", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("IsSelected = false", gallery, StringComparison.Ordinal);
    }

    [Fact]
    public void Full_Detail_Header_Has_RightCloseAndNoLeftBackButton()
    {
        var view = LoadSource("MemoryKeeper.App", "Views", "PhotoDetailView.xaml");

        Assert.DoesNotContain("Content=\"뒤로\"", view, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ZoomFitButton\" Grid.Column=\"2\"", view, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ZoomInButton\" Grid.Column=\"3\"", view, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ZoomOutButton\" Grid.Column=\"4\"", view, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CloseButton\"", view, StringComparison.Ordinal);
        Assert.Contains("Grid.Column=\"5\"", view, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"닫기\"", view, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding CloseCommand}\"", view, StringComparison.Ordinal);
    }

    [Fact]
    public void PhotoMap_UsesExistingBackStackWithContextualCloseAndEscape()
    {
        var mainWindow = LoadSource("MemoryKeeper.App", "Views", "MainWindow.xaml.cs");
        var visitView = LoadSource("MemoryKeeper.App", "Views", "VisitRecordPage.xaml");
        var visitViewModel = LoadSource("MemoryKeeper.App", "ViewModels", "VisitRecordViewModel.cs");
        var focusState = LoadSource("MemoryKeeper.App", "Services", "IPlaceFocusState.cs");

        Assert.Contains("VisitMapNavigationSource.PhotoDetail", mainWindow, StringComparison.Ordinal);
        Assert.Contains("SelectNavigationItem(\"visits\")", mainWindow, StringComparison.Ordinal);
        Assert.Contains("ContentFrame.Content is VisitRecordPage visit", mainWindow, StringComparison.Ordinal);
        Assert.Contains("visit.ViewModel.GoBackCommand.Execute(null)", mainWindow, StringComparison.Ordinal);
        Assert.Contains("IsXButton1Pressed", mainWindow, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding GoBackCommand}\"", visitView, StringComparison.Ordinal);
        Assert.Contains("IsContextualCloseVisible", visitView, StringComparison.Ordinal);
        Assert.Contains("source == VisitMapNavigationSource.PhotoDetail", visitViewModel, StringComparison.Ordinal);
        Assert.Contains("PhotoDetail = 4", focusState, StringComparison.Ordinal);
    }

    [Fact]
    public void GalleryMapper_UsesExifPriorityAndRegisteredPlaceProjection()
    {
        var mapper = LoadSource("MemoryKeeper.App", "Services", "GalleryBackendMapper.cs");
        var detailViewModel = LoadSource("MemoryKeeper.App", "ViewModels", "PhotoDetailViewModel.cs");

        var original = mapper.IndexOf("\"datetime_original\"", StringComparison.Ordinal);
        var digitized = mapper.IndexOf("\"datetime_digitized\"", StringComparison.Ordinal);
        var dateTime = mapper.IndexOf("\"datetime\"", StringComparison.Ordinal);
        Assert.True(original >= 0 && original < digitized && digitized < dateTime);
        Assert.Contains("\"camera_make\"", mapper, StringComparison.Ordinal);
        Assert.Contains("\"camera_model\"", mapper, StringComparison.Ordinal);
        Assert.Contains("\"lens_model\"", mapper, StringComparison.Ordinal);
        Assert.Contains("PhotoDetailGeographyProjection.Resolve(detail, SelectedPlace)", detailViewModel, StringComparison.Ordinal);
        Assert.Contains("Text=\"상세지역: \"", LoadSource("MemoryKeeper.App", "Views", "PhotoDetailView.xaml"), StringComparison.Ordinal);
    }

    private static string LoadSource(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MemoryKeeper.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var path = Path.Combine(new[] { directory!.FullName }.Concat(segments).ToArray());
        Assert.True(File.Exists(path), $"Source file was not found: {path}");
        return File.ReadAllText(path);
    }
}
