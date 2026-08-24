namespace MemoryKeeper.Tests.UnitTests;

public sealed class PageContainerLayoutTests
{
    [Fact]
    public void DesignSystem_Defines_Standard_Wide_Full_Containers_And_Shared_Tokens()
    {
        var spacing = LoadSource("MemoryKeeper.App", "Themes", "Tokens", "Spacing.xaml");
        var layout = LoadSource("MemoryKeeper.App", "Themes", "Styles", "Layout.xaml");
        var designSystem = LoadSource("MemoryKeeper.App", "Themes", "DesignSystem.xaml");

        foreach (var token in new[]
                 {
                     "MkPageHorizontalPadding", "MkStandardPageMaxWidth", "MkWidePageMaxWidth",
                     "MkSettingsSimpleDetailMaxWidth", "MkSettingsNavigationWidth", "MkPageColumnGap",
                 })
        {
            Assert.Contains($"x:Key=\"{token}\"", spacing, StringComparison.Ordinal);
        }

        foreach (var style in new[]
                 {
                     "MkStandardPageContainerStyle", "MkWidePageContainerStyle", "MkFullPageContainerStyle",
                 })
        {
            Assert.Contains($"x:Key=\"{style}\"", layout, StringComparison.Ordinal);
        }

        Assert.Contains("Themes/Styles/Layout.xaml", designSystem, StringComparison.Ordinal);
    }

    [Fact]
    public void Primary_Pages_Use_Their_Assigned_Container_Class()
    {
        Assert.Contains("MkStandardPageContainerStyle", LoadPage("HomePage.xaml"), StringComparison.Ordinal);
        Assert.Contains("MkWidePageContainerStyle", LoadPage("GalleryPage.xaml"), StringComparison.Ordinal);
        Assert.Contains("MkFullPageContainerStyle", LoadPage("VisitRecordPage.xaml"), StringComparison.Ordinal);
        Assert.Contains("MkStandardStackPageContainerStyle", LoadPage("TravelRecordsPage.xaml"), StringComparison.Ordinal);
        Assert.Contains("MkStandardPageContainerStyle", LoadPage("TravelRecordsDetailPage.xaml"), StringComparison.Ordinal);
        Assert.Contains("MkWidePageContainerStyle", LoadPage("SettingsPage.xaml"), StringComparison.Ordinal);
    }

    [Fact]
    public void Legacy_Narrow_Page_MaxWidths_Are_Not_Used_By_Primary_Containers()
    {
        foreach (var page in new[] { "HomePage.xaml", "SettingsPage.xaml" })
        {
            var xaml = LoadPage(page);
            Assert.DoesNotContain("MaxWidth=\"1180\"", xaml, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("MaxWidth=\"{StaticResource MkContentMaxWidth}\"", LoadPage("TravelRecordsPage.xaml"), StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"DetailHost\" MaxWidth=\"900\"", LoadPage("SettingsPage.xaml"), StringComparison.Ordinal);
    }

    private static string LoadPage(string name) =>
        LoadSource("MemoryKeeper.App", "Views", name);

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
