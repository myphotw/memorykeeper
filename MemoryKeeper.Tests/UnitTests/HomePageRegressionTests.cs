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
