namespace MemoryKeeper.Tests.UnitTests;

public sealed class PendingMemoryPageLayoutTests
{
    [Fact]
    public void PendingThumbnails_UseFullImageUniformLayoutForLandscapeAndPortrait()
    {
        var sourcePath = FindSourceFile("MemoryKeeper.App", "Views", "PendingMemoryPage.xaml");
        var xaml = File.ReadAllText(sourcePath);

        Assert.DoesNotContain("UniformToFill", xaml, StringComparison.Ordinal);
        Assert.True(CountOccurrences(xaml, "Stretch=\"Uniform\"") >= 2);
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

        throw new FileNotFoundException("PendingMemoryPage.xaml source file was not found.");
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
