namespace MemoryKeeper.Tests.UnitTests;

public sealed class BackNavigationUiTests
{
    [Fact]
    public void SharedBackStyle_IsCompactAndUsesThemeInteractionFeedback()
    {
        var buttons = LoadSource("MemoryKeeper.App", "Themes", "Styles", "Buttons.xaml");

        Assert.Contains("x:Key=\"MkBackNavigationButtonStyle\"", buttons, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Background\" Value=\"{ThemeResource MkBrushSurfaceMuted}\"", buttons, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"BorderThickness\" Value=\"1\"", buttons, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"BorderBrush\" Value=\"{ThemeResource MkBrushBorder}\"", buttons, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"MinHeight\" Value=\"{StaticResource MkControlHeight}\"", buttons, StringComparison.Ordinal);
        Assert.Contains("UseSystemFocusVisuals", buttons, StringComparison.Ordinal);
        Assert.Contains("MkBrushSurfaceMuted", buttons, StringComparison.Ordinal);
        Assert.Contains("MkBrushTextPrimary", buttons, StringComparison.Ordinal);
    }

    [Fact]
    public void VisitRecord_BackUiUsesHistoryLabelAndHidesForTopLevel()
    {
        var xaml = LoadSource("MemoryKeeper.App", "Views", "VisitRecordPage.xaml");
        var code = LoadSource("MemoryKeeper.App", "Views", "VisitRecordPage.xaml.cs");

        Assert.Contains("x:Name=\"BackNavigationButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource MkBackNavigationButtonStyle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Glyph=\"&#xE72B;\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TextTrimming=\"CharacterEllipsis\"", xaml, StringComparison.Ordinal);
        Assert.Contains("entry.Kind != NavigationKind.TopLevel", code, StringComparison.Ordinal);
        Assert.Contains("&& _navigation.CanGoBack", code, StringComparison.Ordinal);
        Assert.Contains("_navigation.BackEntry?.DisplayLabel", code, StringComparison.Ordinal);
        Assert.Contains("? \"뒤로\"", code, StringComparison.Ordinal);
    }

    [Fact]
    public void TravelDetail_BackUiUsesHistoryLabelAndRetainsTravelFallback()
    {
        var xaml = LoadSource("MemoryKeeper.App", "Views", "TravelRecordsDetailPage.xaml");
        var code = LoadSource("MemoryKeeper.App", "Views", "TravelRecordsDetailPage.xaml.cs");

        Assert.Contains("Style=\"{StaticResource MkBackNavigationButtonStyle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding BackCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("_navigation.BackEntry?.DisplayLabel", code, StringComparison.Ordinal);
        Assert.Contains("hasTravelFallback", code, StringComparison.Ordinal);
        Assert.Contains("? \"여행기록\"", code, StringComparison.Ordinal);
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
