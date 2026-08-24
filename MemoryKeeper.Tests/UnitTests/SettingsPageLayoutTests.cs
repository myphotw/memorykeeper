namespace MemoryKeeper.Tests.UnitTests;

public sealed class SettingsPageLayoutTests
{
    [Fact]
    public void Settings_Uses_Bounded_Left_Navigation_And_Right_Detail()
    {
        var xaml = LoadSource("MemoryKeeper.App", "Views", "SettingsPage.xaml");

        Assert.Contains("x:Name=\"SettingsNavigation\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PaneDisplayMode=\"Left\"", xaml, StringComparison.Ordinal);
        Assert.Contains("OpenPaneLength=\"{StaticResource MkSettingsNavigationWidth}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource MkWidePageContainerStyle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DetailHost\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"DetailHost\" MaxWidth=\"900\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer VerticalScrollBarVisibility=\"Auto\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_Distinguishes_Wide_Management_And_Simple_Details()
    {
        var xaml = LoadSource("MemoryKeeper.App", "Views", "SettingsPage.xaml");

        foreach (var detail in new[]
                 {
                     "PhotoManagementDetail", "PendingMemoriesDetail", "PlacesDetail", "TagsDetail", "PhotoExportDetail",
                 })
        {
            Assert.Contains($"x:Name=\"{detail}\"", xaml, StringComparison.Ordinal);
        }

        Assert.Equal(5, Count(xaml, "Style=\"{StaticResource MkWideSettingsCardStyle}\""));
        Assert.Equal(4, Count(xaml, "Style=\"{StaticResource MkSimpleSettingsCardStyle}\""));
        Assert.Equal(1, Count(xaml, "Style=\"{StaticResource MkSimpleSettingsOutlinedCardStyle}\""));
    }

    [Fact]
    public void Navigation_Has_Five_Top_Level_Items_And_Expected_Children()
    {
        var xaml = LoadSource("MemoryKeeper.App", "Views", "SettingsPage.xaml");

        foreach (var item in new[] { "사진 관리", "자동 태그", "데이터 관리", "고급 관리", "앱 정보" })
        {
            Assert.Contains($"Content=\"{item}\"", xaml, StringComparison.Ordinal);
        }

        foreach (var child in new[]
                 {
                     "미완성 추억", "장소 관리", "태그 관리", "집 위치",
                     "사진 내보내기", "미리보기 캐시", "처음부터 다시 구성",
                 })
        {
            Assert.Contains($"Content=\"{child}\"", xaml, StringComparison.Ordinal);
        }

        Assert.Contains("NavigationViewItem.MenuItems", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"원본 사진\" Tag=\"original-photos\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"사진 가져오기\" Tag=\"photo-import\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Photo_Management_Parent_Is_Default_And_Maps_To_One_Detail()
    {
        var xaml = LoadSource("MemoryKeeper.App", "Views", "SettingsPage.xaml");
        var viewModel = LoadSource("MemoryKeeper.App", "ViewModels", "SettingsViewModel.cs");

        Assert.Contains("x:Name=\"PhotoMenuItem\" Content=\"사진 관리\" Tag=\"photo-management\" IsExpanded=\"True\" IsSelected=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("private SettingsSection selectedSettingsSection = SettingsSection.PhotoManagement", viewModel, StringComparison.Ordinal);
        Assert.Contains("IsPhotoManagementDetailVisible => SelectedSettingsSection == SettingsSection.PhotoManagement", viewModel, StringComparison.Ordinal);
        Assert.Contains("IsResetDetailVisible => SelectedSettingsSection == SettingsSection.Reset", viewModel, StringComparison.Ordinal);
        Assert.Contains("IsAutoTagsDetailVisible => SelectedSettingsSection == SettingsSection.AutoTags", viewModel, StringComparison.Ordinal);
        Assert.Contains("\"photo-import\" or \"metadata\" or \"import\" => SettingsSection.PhotoManagement", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void Detail_Cards_Are_Selected_By_Visibility_And_Preserve_Function_Commands()
    {
        var xaml = LoadSource("MemoryKeeper.App", "Views", "SettingsPage.xaml");

        Assert.Contains("Visibility=\"{Binding IsPhotoManagementDetailVisible", xaml, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding IsAutoTagsDetailVisible", xaml, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding IsPhotoExportDetailVisible", xaml, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding IsResetDetailVisible", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ChangeStorageFolderCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding CheckStorageConnectionCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ImportHost\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PendingHost\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PlaceHost\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TagHost\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Command=\"{Binding OpenImportCommand}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Command=\"{Binding OpenPendingCommand}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Command=\"{Binding OpenPlaceCommand}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Command=\"{Binding OpenTagManagementCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding SaveHomeCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding RetryFailedAutoTagsCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ExportPhotosCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ClearThumbnailCacheCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding RestartMemoryKeeperCommand}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Embedded_Features_Reuse_UserControls_And_Existing_ViewModels()
    {
        var settingsCode = LoadSource("MemoryKeeper.App", "Views", "SettingsPage.xaml.cs");
        var appCode = LoadSource("MemoryKeeper.App", "App.xaml.cs");

        foreach (var view in new[] { "ImportView", "PendingMemoryView", "PlaceManagementView", "TagManagementView" })
        {
            Assert.Contains($"AddTransient<{view}>()", appCode, StringComparison.Ordinal);
            Assert.Contains($"{view} ", settingsCode, StringComparison.Ordinal);
        }

        Assert.Contains("_activatedSections.Contains(section)", settingsCode, StringComparison.Ordinal);
        Assert.Contains("SettingsSection.PendingMemories", settingsCode, StringComparison.Ordinal);
        Assert.Contains("SettingsSection.Places", settingsCode, StringComparison.Ordinal);
        Assert.Contains("SettingsSection.Tags", settingsCode, StringComparison.Ordinal);
    }

    [Fact]
    public void Photo_Registration_Commands_And_Card_Progress_Are_Embedded()
    {
        var settings = LoadSource("MemoryKeeper.App", "Views", "SettingsPage.xaml");
        var import = LoadSource("MemoryKeeper.App", "Views", "ImportView.xaml");

        Assert.Contains("Text=\"사진 관리\"", settings, StringComparison.Ordinal);
        Assert.Contains("Text=\"사진 등록\"", settings, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding BrowseFolderCommand}\"", import, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ImportCommand}\"", import, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding CancelImportCommand}\"", import, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding RetryImportCommand}\"", import, StringComparison.Ordinal);
        Assert.Contains("Value=\"{Binding ProgressValue, Mode=OneWay}\"", import, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding CurrentFileName, Mode=OneWay}\"", import, StringComparison.Ordinal);
    }

    [Fact]
    public void Pending_Place_And_Tag_Features_Remain_In_Shared_Views()
    {
        var pending = LoadSource("MemoryKeeper.App", "Views", "PendingMemoryView.xaml");
        var place = LoadSource("MemoryKeeper.App", "Views", "PlaceManagementView.xaml");
        var tag = LoadSource("MemoryKeeper.App", "Views", "TagManagementView.xaml");

        foreach (var term in new[] { "전체 포함", "전체 제외", "장소등록", "일괄 등록", "상세" })
        {
            Assert.Contains(term, pending, StringComparison.Ordinal);
        }

        foreach (var term in new[] { "장소 검색", "국가", "시/도", "시군구", "반경 (m)", "즐겨찾기", "저장", "삭제" })
        {
            Assert.Contains(term, place, StringComparison.Ordinal);
        }

        foreach (var term in new[] { "태그 검색", "사용 사진", "이름 변경", "삭제" })
        {
            Assert.Contains(term, tag, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Settings_Selection_Does_Not_Invoke_MainWindow_Child_Page_Navigation()
    {
        var settings = LoadSource("MemoryKeeper.App", "Views", "SettingsPage.xaml.cs");
        var mainWindow = LoadSource("MemoryKeeper.App", "Views", "MainWindow.xaml.cs");

        Assert.DoesNotContain("OpenImportRequested", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenPlaceRequested", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenPendingRequested", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("OnSettingsOpenImportRequested", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("OnSettingsOpenPlaceRequested", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("OnSettingsOpenPendingRequested", mainWindow, StringComparison.Ordinal);
        Assert.Contains("GetOrCreatePage<SettingsPage>(\"settings\")", mainWindow, StringComparison.Ordinal);

        Assert.Contains("GetOrCreatePage<PendingMemoryPage>", mainWindow, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<PlaceManagementPage>", mainWindow, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<ImportPage>", mainWindow, StringComparison.Ordinal);
    }

    [Fact]
    public void Reset_Danger_Action_Exists_Only_In_Reset_Detail()
    {
        var xaml = LoadSource("MemoryKeeper.App", "Views", "SettingsPage.xaml");

        Assert.Equal(1, Count(xaml, "Command=\"{Binding RestartMemoryKeeperCommand}\""));
        Assert.Equal(1, Count(xaml, "Style=\"{StaticResource MkDangerButtonStyle}\""));
        Assert.Contains("x:Name=\"ResetDetail\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding IsResetDetailVisible", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Layout_Has_Desktop_And_Compact_Navigation_Without_Legacy_Terms()
    {
        var xaml = LoadSource("MemoryKeeper.App", "Views", "SettingsPage.xaml");
        var codeBehind = LoadSource("MemoryKeeper.App", "Views", "SettingsPage.xaml.cs");

        Assert.Contains("AdaptiveTrigger MinWindowWidth=\"800\"", xaml, StringComparison.Ordinal);
        Assert.Contains("NavigationViewPaneDisplayMode.Top", codeBehind, StringComparison.Ordinal);
        Assert.Contains("NavigationViewPaneDisplayMode.Left", codeBehind, StringComparison.Ordinal);

        foreach (var term in new[]
                 {
                     "DB backup", "DB restore", "DB wipe", "장소 재생성", "장소 재정규화",
                     "여행기록 재생성", "integrity", "XMP", "EXIF", "IPTC",
                 })
        {
            Assert.DoesNotContain(term, xaml, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        for (var index = 0; (index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length)
        {
            count++;
        }

        return count;
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
