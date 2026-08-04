using MemoryKeeper.Domain.Enums;
using MemoryKeeper.Infrastructure.Import;

namespace MemoryKeeper.Tests.UnitTests;

public class FileScannerTests
{
    [Fact]
    public async Task ScanAsync_ReturnsOnlySupportedMediaFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mk-scan-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var nested = Path.Combine(root, "nested");
        Directory.CreateDirectory(nested);

        var jpg = Path.Combine(root, "a.jpg");
        var png = Path.Combine(nested, "b.PNG");
        var mp4 = Path.Combine(root, "c.mp4");
        var txt = Path.Combine(root, "ignore.txt");

        await File.WriteAllTextAsync(jpg, "jpg");
        await File.WriteAllTextAsync(png, "png");
        await File.WriteAllTextAsync(mp4, "mp4");
        await File.WriteAllTextAsync(txt, "txt");

        try
        {
            var scanner = new FileScanner();
            var files = await scanner.ScanAsync(root);

            Assert.Equal(3, files.Count);
            Assert.Contains(jpg, files);
            Assert.Contains(png, files);
            Assert.Contains(mp4, files);
            Assert.Equal(MediaType.Photo, scanner.ResolveMediaType(jpg));
            Assert.Equal(MediaType.Video, scanner.ResolveMediaType(mp4));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
