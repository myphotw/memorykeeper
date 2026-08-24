using System.Xml.Linq;

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

    [Fact]
    public void Home_Centers_Standard_Content_Without_PageSpecific_Offset_Or_Legacy_Width()
    {
        var home = LoadPage("HomePage.xaml");
        var document = XDocument.Parse(home);
        var mainScroll = FindNamedElement(document, "MainScroll");
        var mainContent = FindNamedElement(document, "MainContent");

        Assert.Equal("Center", mainScroll.Attribute("HorizontalContentAlignment")?.Value);
        Assert.Equal("Disabled", mainScroll.Attribute("HorizontalScrollBarVisibility")?.Value);
        Assert.Equal("{StaticResource MkStandardPageContainerStyle}", mainContent.Attribute("Style")?.Value);
        Assert.Equal("Center", mainContent.Attribute("HorizontalAlignment")?.Value);
        Assert.Null(mainContent.Attribute("Margin"));
        Assert.Equal(
            "{Binding ViewportWidth, ElementName=MainScroll, Mode=OneWay}",
            mainContent.Attribute("Width")?.Value);
        Assert.Null(mainContent.Attribute("MaxWidth"));
        Assert.DoesNotContain("MaxWidth=\"1180\"", home, StringComparison.Ordinal);
    }

    [Fact]
    public void Home_Hero_Uses_Responsive_Balanced_Columns_And_Full_Image_Policy()
    {
        var document = XDocument.Parse(LoadPage("HomePage.xaml"));
        var heroLayout = FindNamedElement(document, "HeroLayoutGrid");
        var columns = heroLayout
            .Elements()
            .Single(element => element.Name.LocalName == "Grid.ColumnDefinitions")
            .Elements()
            .Select(element => element.Attribute("Width")?.Value)
            .ToArray();
        var heroImageFrame = FindNamedElement(document, "HeroImageFrame");
        var heroImages = heroImageFrame.Descendants().Where(element => element.Name.LocalName == "Image").ToList();

        Assert.Equal(["3*", "4*"], columns);
        Assert.Equal("Stretch", heroImageFrame.Attribute("HorizontalAlignment")?.Value);
        Assert.Null(heroImageFrame.Attribute("Width"));
        Assert.NotEmpty(heroImages);
        Assert.All(heroImages, image => Assert.Equal("Uniform", image.Attribute("Stretch")?.Value));
    }

    [Fact]
    public void Shared_Container_Contracts_Keep_Standard_Wide_Full_Policies()
    {
        var layout = LoadSource("MemoryKeeper.App", "Themes", "Styles", "Layout.xaml");

        Assert.Contains("Value=\"{StaticResource MkStandardPageMaxWidth}\"", layout, StringComparison.Ordinal);
        Assert.Contains("Value=\"{StaticResource MkWidePageMaxWidth}\"", layout, StringComparison.Ordinal);
        Assert.Contains("Value=\"{StaticResource MkDesktopPagePaddingThickness}\"", layout, StringComparison.Ordinal);

        var spacing = LoadSource("MemoryKeeper.App", "Themes", "Tokens", "Spacing.xaml");
        Assert.Contains("x:Key=\"MkStandardPageMaxWidth\">1520", spacing, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"MkWidePageMaxWidth\">1640", spacing, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"MkPageHorizontalPadding\">32", spacing, StringComparison.Ordinal);
    }

    private static string LoadPage(string name) =>
        LoadSource("MemoryKeeper.App", "Views", name);

    private static XElement FindNamedElement(XDocument document, string name)
    {
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        return document
            .Descendants()
            .Single(element => element.Attribute(xaml + "Name")?.Value == name);
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
