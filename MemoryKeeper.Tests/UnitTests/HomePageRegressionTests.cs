namespace MemoryKeeper.Tests.UnitTests;

public sealed class HomePageRegressionTests
{
    [Fact]
    public void RecentVisitCards_KeepWholeThumbnailAtReadableSize()
    {
        var xaml = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Views", "HomePage.xaml"));

        Assert.Contains("Width=\"260\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<RowDefinition Height=\"172\" />", xaml, StringComparison.Ordinal);
        Assert.Contains(
            "Source=\"{Binding ThumbnailImage, Mode=OneWay}\" Stretch=\"Uniform\"",
            xaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void HeroNavigation_ClickIsNotConsumedByTappedHandler_AndWrapsBothDirections()
    {
        var xaml = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Views", "HomePage.xaml"));
        var codeBehind = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Views", "HomePage.xaml.cs"));
        var viewModel = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "ViewModels", "HomeViewModel.cs"));

        Assert.Contains("x:Name=\"HeroImageFrame\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Width=\"360\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"3*\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"4*\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("HeroImageFrame.Width = double.NaN", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Height=\"270\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"HeroImageFront\"", xaml, StringComparison.Ordinal);
        var heroImageStart = xaml.IndexOf("x:Name=\"HeroImageFront\"", StringComparison.Ordinal);
        var heroImageEnd = xaml.IndexOf("/>", heroImageStart, StringComparison.Ordinal);
        var heroImageTag = xaml[heroImageStart..(heroImageEnd + 2)];
        Assert.Contains("Stretch=\"Uniform\"", heroImageTag, StringComparison.Ordinal);
        Assert.DoesNotContain("UniformToFill", heroImageTag, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding CurrentHeroTitle, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding CurrentHeroSubtitle, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"HeroPrevious_OnClick\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"HeroNext_OnClick\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("HeroNav_OnTapped", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("HeroNav_OnTapped", codeBehind, StringComparison.Ordinal);
        Assert.Contains("(_heroIndex + 1) % HeroMemories.Count", viewModel, StringComparison.Ordinal);
        Assert.Contains("(_heroIndex - 1 + HeroMemories.Count) % HeroMemories.Count", viewModel, StringComparison.Ordinal);
        Assert.Contains("CurrentHeroTitle = CurrentHero.Title", viewModel, StringComparison.Ordinal);
        Assert.Contains("CurrentHeroSubtitle = CurrentHero.Subtitle", viewModel, StringComparison.Ordinal);
        Assert.Contains("CurrentHeroImage = CurrentHero.ThumbnailImage", viewModel, StringComparison.Ordinal);
        Assert.Contains("visualVersion != _heroVisualVersion", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void QuickActions_ContainOnlyImportAndPendingMemoryActions()
    {
        var xaml = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Views", "HomePage.xaml"));
        var codeBehind = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Views", "HomePage.xaml.cs"));
        var viewModel = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "ViewModels", "HomeViewModel.cs"));
        var mainWindow = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Views", "MainWindow.xaml.cs"));
        var mainWindowXaml = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Views", "MainWindow.xaml"));

        var quickStart = xaml.IndexOf("<!-- Quick actions -->", StringComparison.Ordinal);
        var quickEnd = xaml.IndexOf("<!-- Footer -->", quickStart, StringComparison.Ordinal);
        Assert.True(quickStart >= 0 && quickEnd > quickStart);
        var quickActions = xaml[quickStart..quickEnd];

        Assert.Contains("x:Name=\"QuickImportCard\"", quickActions, StringComparison.Ordinal);
        Assert.Contains("Text=\"사진 가져오기\"", quickActions, StringComparison.Ordinal);
        Assert.Contains("Text=\"새 사진 추가\"", quickActions, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding QuickImportCommand}\"", quickActions, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"QuickPendingCard\"", quickActions, StringComparison.Ordinal);
        Assert.Contains("Text=\"미완성 추억\"", quickActions, StringComparison.Ordinal);
        Assert.Contains("PendingQuickActionText", quickActions, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding QuickPendingCommand}\"", quickActions, StringComparison.Ordinal);
        Assert.DoesNotContain("사진 정리", quickActions, StringComparison.Ordinal);
        Assert.DoesNotContain("방문지도", quickActions, StringComparison.Ordinal);
        Assert.DoesNotContain("여행기록", quickActions, StringComparison.Ordinal);

        Assert.Equal(2, CountOccurrences(quickActions, "<Button"));
        Assert.Contains("MaxWidth=\"840\"", quickActions, StringComparison.Ordinal);
        Assert.Contains("ApplyQuickActionsLayout(ActualWidth >= 560)", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("QuickActionsPanel.ColumnDefinitions[2]", codeBehind, StringComparison.Ordinal);
        Assert.Contains("private void QuickPending() => OpenPendingRequested?.Invoke", viewModel, StringComparison.Ordinal);
        Assert.Contains("PendingSummary.Total:N0", viewModel, StringComparison.Ordinal);
        Assert.Contains("OnHomeOpenPendingRequested", mainWindow, StringComparison.Ordinal);
        Assert.Contains("SelectNavigationItem(\"pending\")", mainWindow, StringComparison.Ordinal);
        Assert.Contains("OnHomeOpenImportRequested", mainWindow, StringComparison.Ordinal);
        Assert.Contains("SelectNavigationItem(\"import\")", mainWindow, StringComparison.Ordinal);
        Assert.Contains("Content=\"사진첩\" Tag=\"gallery\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"방문지도\" Tag=\"visits\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"여행기록\" Tag=\"travel\"", mainWindowXaml, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        for (var index = 0; (index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length)
        {
            count++;
        }

        return count;
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
