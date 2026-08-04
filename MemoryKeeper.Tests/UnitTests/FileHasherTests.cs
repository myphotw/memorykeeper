using MemoryKeeper.Infrastructure.Import;

namespace MemoryKeeper.Tests.UnitTests;

public class FileHasherTests
{
    [Fact]
    public async Task ComputeSha256Async_ReturnsExpectedHash()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"mk-hash-{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(tempFile, "MemoryKeeper"u8.ToArray());

        try
        {
            var hasher = new FileHasher();
            var hash = await hasher.ComputeSha256Async(tempFile);

            Assert.Equal("c2d1f0f5f8e0b3c1f1a0f5b5d2c5d7c8e9f0a1b2c3d4e5f60718293a4b5c6d7e".Length, hash.Length);
            Assert.Matches("^[0-9a-f]{64}$", hash);
            Assert.Equal(hash, await hasher.ComputeSha256Async(tempFile));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
