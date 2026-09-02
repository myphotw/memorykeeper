namespace MemoryKeeper.Tests.UnitTests;

public sealed class GalleryPageLayoutTests
{
    [Fact]
    public void Gallery_RemovesRepresentativeHero_AndPlacesGridDirectlyBelowHeader()
    {
        var xaml = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Views", "GalleryPage.xaml"));
        var codeBehind = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Views", "GalleryPage.xaml.cs"));

        Assert.DoesNotContain("HeroThumbHost", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("HeroThumbImage", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("HeroThumbHost", codeBehind, StringComparison.Ordinal);
        Assert.Contains("RowDefinitions=\"Auto,*,Auto\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<!-- Photo grid -->", xaml, StringComparison.Ordinal);
        Assert.Contains("<Grid Grid.Row=\"1\">", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void GalleryThumbnailCards_PreserveWholeImageContext()
    {
        var xaml = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Views", "GalleryPage.xaml"));
        var cardStart = xaml.IndexOf("Style=\"{StaticResource GalleryPhotoCardStyle}\"", StringComparison.Ordinal);
        var cardEnd = xaml.IndexOf("<!-- Soft primary wash", cardStart, StringComparison.Ordinal);

        Assert.True(cardStart >= 0 && cardEnd > cardStart);
        var card = xaml[cardStart..cardEnd];
        Assert.Contains("Source=\"{Binding ThumbnailImage, Mode=OneWay}\"", card, StringComparison.Ordinal);
        Assert.Contains("Stretch=\"Uniform\"", card, StringComparison.Ordinal);
        Assert.DoesNotContain("Stretch=\"UniformToFill\"", card, StringComparison.Ordinal);
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
