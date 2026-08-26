using System.Xml.Linq;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class PackagingIconTests
{
    private static readonly int[] RequiredSizes = [16, 24, 32, 48, 64, 128, 256];

    [Fact]
    public void AppAndInstallerUseMemoryKeeperIcon()
    {
        var root = FindProjectRoot();
        var projectPath = Path.Combine(root, "MemoryKeeper.App", "MemoryKeeper.App.csproj");
        var installerPath = Path.Combine(root, "installer", "MemoryKeeper.iss");
        var project = XDocument.Load(projectPath);
        var applicationIcon = project.Descendants("ApplicationIcon").Single().Value;
        var installer = File.ReadAllText(installerPath);

        Assert.Equal(@"Assets\MemoryKeeper.ico", applicationIcon);
        Assert.Contains(@"SetupIconFile=..\MemoryKeeper.App\Assets\MemoryKeeper.ico", installer, StringComparison.Ordinal);
        Assert.Contains(@"UninstallDisplayIcon={app}\MemoryKeeper.exe", installer, StringComparison.Ordinal);
        Assert.Contains(@"Name: ""{autoprograms}\MemoryKeeper""; Filename: ""{app}\MemoryKeeper.exe""", installer, StringComparison.Ordinal);
        Assert.Contains(@"Name: ""{autodesktop}\MemoryKeeper""; Filename: ""{app}\MemoryKeeper.exe""", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("IconFilename:", installer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IcoContainsAllRequiredWindowsSizes()
    {
        var iconPath = Path.Combine(FindProjectRoot(), "MemoryKeeper.App", "Assets", "MemoryKeeper.ico");
        using var stream = File.OpenRead(iconPath);
        using var reader = new BinaryReader(stream);
        Assert.Equal(0, reader.ReadUInt16());
        Assert.Equal(1, reader.ReadUInt16());
        var count = reader.ReadUInt16();
        var sizes = new HashSet<int>();

        for (var index = 0; index < count; index++)
        {
            var width = reader.ReadByte();
            var height = reader.ReadByte();
            sizes.Add(width == 0 ? 256 : width);
            Assert.Equal(width == 0 ? 256 : width, height == 0 ? 256 : height);
            stream.Position += 14;
        }

        Assert.Equal(RequiredSizes, sizes.OrderBy(size => size));
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MemoryKeeper.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
