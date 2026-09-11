namespace MemoryKeeper.Tests.UnitTests;

public sealed class PendingMemoryPageLayoutTests
{
    [Theory]
    [InlineData("USER", "사용자 지정")]
    [InlineData("EXIF", "사진 촬영정보")]
    [InlineData("IMPORTED", "가져온 날짜 기준")]
    [InlineData("CREATED", "파일 생성일 기준")]
    [InlineData(null, "날짜 정보 없음")]
    public void CaptureDateBasis_UsesFriendlyText(string? value, string expected)
    {
        var model = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Models", "PendingMemoryGroupItem.cs"));
        var expectedSwitchArm = value is null
            ? $"_ => \"{expected}\""
            : $"\"{value}\" => \"{expected}\"";

        Assert.Contains(expectedSwitchArm, model, StringComparison.Ordinal);
    }

    [Fact]
    public void DatePrecision_HidesSyntheticMidnight()
    {
        var model = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Models", "PendingMemoryGroupItem.cs"));

        Assert.Contains("string.Equals(Media.UserCapturePrecision, \"DATE\"", model, StringComparison.Ordinal);
        Assert.Contains("? FormatDateOnly(Media.EffectiveCaptureDate, Media.CapturedAt)", model, StringComparison.Ordinal);
        Assert.Contains("date.ToString(\"yyyy.MM.dd\"", model, StringComparison.Ordinal);
        Assert.DoesNotContain("date.ToString(\"yyyy.MM.dd HH:mm\"", model, StringComparison.Ordinal);
    }

    [Fact]
    public void PendingThumbnails_UseFullImageUniformLayoutForLandscapeAndPortrait()
    {
        var sourcePath = FindSourceFile("MemoryKeeper.App", "Views", "PendingMemoryView.xaml");
        var xaml = File.ReadAllText(sourcePath);

        Assert.DoesNotContain("UniformToFill", xaml, StringComparison.Ordinal);
        Assert.True(CountOccurrences(xaml, "Stretch=\"Uniform\"") >= 1);
        Assert.Contains("Width=\"232\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Height=\"320\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"220\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding FileName}", xaml, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"{Binding IsIncluded, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding IncludeAllCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ExcludeAllCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding AssignPlaceCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<ItemsWrapGrid Orientation=\"Horizontal\" />", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void CleanupScreen_UsesIndependentFiveItemPlaceAndCaptureDateQueues()
    {
        var xaml = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Views", "PendingMemoryView.xaml"));
        var viewModel = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "ViewModels", "PendingMemoryViewModel.cs"));
        var repository = File.ReadAllText(FindSourceFile("MemoryKeeper.Infrastructure", "Repositories", "Api", "MemoryKeeperWriteApiRepository.cs"));

        Assert.Contains("ItemsSource=\"{Binding PlaceGroups", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding CaptureDateGroups", xaml, StringComparison.Ordinal);
        Assert.Contains("촬영일 변경", xaml, StringComparison.Ordinal);
        Assert.Contains("촬영일 보정 해제", xaml, StringComparison.Ordinal);
        Assert.Contains("private const int CleanupGroupLimit = 5;", viewModel, StringComparison.Ordinal);
        Assert.Contains("PlaceCleanupGroupPhotosAsync", viewModel, StringComparison.Ordinal);
        Assert.Contains("CaptureDateCleanupGroupPhotosAsync", viewModel, StringComparison.Ordinal);
        Assert.Contains("_placeGroupCursor", viewModel, StringComparison.Ordinal);
        Assert.Contains("_captureDateGroupCursor", viewModel, StringComparison.Ordinal);
        Assert.Contains("_groupPhotoCursor", viewModel, StringComparison.Ordinal);
        var loadStart = viewModel.IndexOf("private async Task LoadCoreAsync", StringComparison.Ordinal);
        var loadEnd = viewModel.IndexOf("private void ApplyOverview", loadStart, StringComparison.Ordinal);
        Assert.DoesNotContain("GetPlaceCleanupMemoriesAsync", viewModel[loadStart..loadEnd], StringComparison.Ordinal);
        Assert.Contains("/groups?limit=", repository, StringComparison.Ordinal);
        Assert.Contains("/photos?limit=", repository, StringComparison.Ordinal);
    }

    [Fact]
    public void CleanupGroupTemplates_UseLooseXamlSafeBindingsForTheirDisplayModels()
    {
        var xaml = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Views", "PendingMemoryView.xaml"));

        var placeTemplateStart = xaml.IndexOf(
            "<DataTemplate x:DataType=\"models:PlaceCleanupGroupItem\">",
            StringComparison.Ordinal);
        var captureDateTemplateStart = xaml.IndexOf(
            "<DataTemplate x:DataType=\"models:CaptureDateCleanupGroupItem\">",
            StringComparison.Ordinal);
        Assert.True(placeTemplateStart >= 0);
        Assert.True(captureDateTemplateStart > placeTemplateStart);

        var placeTemplate = xaml[placeTemplateStart..captureDateTemplateStart];
        var captureDateTemplateEnd = xaml.IndexOf("</DataTemplate>", captureDateTemplateStart, StringComparison.Ordinal);
        Assert.True(captureDateTemplateEnd > captureDateTemplateStart);
        var captureDateTemplate = xaml[captureDateTemplateStart..captureDateTemplateEnd];

        Assert.Contains("Text=\"{Binding Title, Mode=OneWay}\"", placeTemplate, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding CountText, Mode=OneWay}\"", placeTemplate, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding PeriodText, Mode=OneWay}\"", placeTemplate, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding LocationText, Mode=OneWay}\"", placeTemplate, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding IssueStatusText, Mode=OneWay}\"", placeTemplate, StringComparison.Ordinal);
        Assert.DoesNotContain("{x:Bind", placeTemplate, StringComparison.Ordinal);

        Assert.Contains("Text=\"{Binding Title, Mode=OneWay}\"", captureDateTemplate, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding CountText, Mode=OneWay}\"", captureDateTemplate, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding ReasonText, Mode=OneWay}\"", captureDateTemplate, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding DateSummaryText, Mode=OneWay}\"", captureDateTemplate, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding PeriodText, Mode=OneWay}\"", captureDateTemplate, StringComparison.Ordinal);
        Assert.DoesNotContain("{x:Bind", captureDateTemplate, StringComparison.Ordinal);

        Assert.Contains("SelectedItem=\"{Binding SelectedPlaceGroup, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding SelectedCaptureDateGroup, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.True(CountOccurrences(xaml, "SelectionMode=\"Single\"") >= 2);
    }

    [Fact]
    public void CaptureDateMutation_ReloadsDateAndPlaceQueuesWithoutRawExifWrite()
    {
        var viewModel = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "ViewModels", "PendingMemoryViewModel.cs"));
        var writeService = File.ReadAllText(FindSourceFile("MemoryKeeper.Application", "Services", "MemoryKeeperWriteService.cs"));
        var photoDetail = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "ViewModels", "PhotoDetailViewModel.cs"));

        var reloadStart = viewModel.IndexOf("private async Task ReloadAfterCaptureDateMutationAsync", StringComparison.Ordinal);
        var reloadEnd = viewModel.IndexOf("private void LogCaptureDateFailure", reloadStart, StringComparison.Ordinal);
        var reload = viewModel[reloadStart..reloadEnd];
        Assert.Contains("LoadCaptureDateQueuePageAsync(cursor: null", reload, StringComparison.Ordinal);
        Assert.Contains("LoadPlaceQueuePageAsync(cursor: null", reload, StringComparison.Ordinal);
        Assert.Contains("ExpectedDateRevisions", writeService, StringComparison.Ordinal);
        Assert.Contains("userCaptureDate?.ToString(\"yyyy-MM-dd\"", writeService, StringComparison.Ordinal);
        Assert.Contains("await ReloadBackendDetailAsync();", photoDetail, StringComparison.Ordinal);
        Assert.DoesNotContain("EXIF write", viewModel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PhotoDetail_UsesBatchCaptureDateEndpointForSinglePhotoAndOffersOverrideClear()
    {
        var xaml = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Views", "PhotoDetailView.xaml"));
        var viewModel = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "ViewModels", "PhotoDetailViewModel.cs"));

        Assert.Contains("CaptureDateBasisText", xaml, StringComparison.Ordinal);
        Assert.Contains("OpenCaptureDateEditorCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("RequestClearCaptureDateCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("new Dictionary<Guid, int> { [MediaId] = _dateRevision }", viewModel, StringComparison.Ordinal);
        Assert.Contains("await ReloadBackendDetailAsync();", viewModel, StringComparison.Ordinal);
        Assert.Contains("HasUserCaptureOverride", viewModel, StringComparison.Ordinal);
        Assert.Contains("UserCapturePrecision, \"DATE\"", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaceMutationReloadsOnlyPlaceQueueWhileDateMutationReloadsBothQueues()
    {
        var viewModel = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "ViewModels", "PendingMemoryViewModel.cs"));
        var placeStart = viewModel.IndexOf("private async Task ReloadPlaceQueueAfterMutationAsync", StringComparison.Ordinal);
        var placeEnd = viewModel.IndexOf("private static string WithRadiusChangeNotice", placeStart, StringComparison.Ordinal);
        var placeReload = viewModel[placeStart..placeEnd];
        Assert.Contains("LoadPlaceQueuePageAsync(cursor: null", placeReload, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadCaptureDateQueuePageAsync", placeReload, StringComparison.Ordinal);

        var dateStart = viewModel.IndexOf("private async Task ReloadAfterCaptureDateMutationAsync", StringComparison.Ordinal);
        var dateEnd = viewModel.IndexOf("private void LogCaptureDateFailure", dateStart, StringComparison.Ordinal);
        var dateReload = viewModel[dateStart..dateEnd];
        Assert.Contains("LoadCaptureDateQueuePageAsync(cursor: null", dateReload, StringComparison.Ordinal);
        Assert.Contains("LoadPlaceQueuePageAsync(cursor: null", dateReload, StringComparison.Ordinal);
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

        throw new FileNotFoundException("PendingMemoryView.xaml source file was not found.");
    }

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        for (var index = 0; (index = value.IndexOf(search, index, StringComparison.Ordinal)) >= 0; index += search.Length)
        {
            count++;
        }

        return count;
    }
}
