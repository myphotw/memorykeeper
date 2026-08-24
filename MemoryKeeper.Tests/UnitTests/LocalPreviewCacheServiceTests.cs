using MemoryKeeper.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class LocalPreviewCacheServiceTests
{
    [Fact]
    public async Task Clear_DeletesOnlyFilesUnderConfiguredLocalCacheRoot()
    {
        var parent = Path.Combine(Path.GetTempPath(), "MemoryKeeper.Tests", Guid.NewGuid().ToString("N"));
        var cache = Path.Combine(parent, "ThumbnailCache");
        var outside = Path.Combine(parent, "NAS-original.jpg");
        Directory.CreateDirectory(Path.Combine(cache, "nested"));
        await File.WriteAllTextAsync(Path.Combine(cache, "one.jpg"), "cache");
        await File.WriteAllTextAsync(Path.Combine(cache, "nested", "two.jpg"), "cache");
        await File.WriteAllTextAsync(outside, "original");
        try
        {
            var service = new LocalPreviewCacheService(
                cache,
                NullLogger<LocalPreviewCacheService>.Instance);

            var result = await service.ClearAsync();

            Assert.True(result.Succeeded);
            Assert.Empty(Directory.EnumerateFiles(cache, "*", SearchOption.AllDirectories));
            Assert.True(File.Exists(outside));
            Assert.Equal("original", await File.ReadAllTextAsync(outside));
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }
}
