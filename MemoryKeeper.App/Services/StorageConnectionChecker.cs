using Microsoft.Extensions.Logging;

namespace MemoryKeeper.App.Services;

public sealed class StorageConnectionResult
{
    public bool Exists { get; init; }

    public bool IsReadable { get; init; }

    public bool IsWritable { get; init; }

    public bool IsHealthy => Exists && IsReadable && IsWritable;
}

public static class StorageConnectionChecker
{
    public static StorageConnectionResult Check(string? path, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            logger?.LogWarning("Storage connection check skipped: empty path.");
            return new StorageConnectionResult();
        }

        var exists = false;
        try
        {
            exists = Directory.Exists(path);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Directory.Exists failed. Path={Path}", path);
        }

        logger?.LogInformation("Directory.Exists. Path={Path} Exists={Exists}", path, exists);
        if (!exists)
        {
            return new StorageConnectionResult();
        }

        var readable = TryRead(path, logger);
        var writable = TryWrite(path, logger);

        return new StorageConnectionResult
        {
            Exists = true,
            IsReadable = readable,
            IsWritable = writable
        };
    }

    private static bool TryRead(string path, ILogger? logger)
    {
        try
        {
            _ = Directory.EnumerateFileSystemEntries(path).GetEnumerator().MoveNext();
            logger?.LogInformation("Read check succeeded. Path={Path}", path);
            return true;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Read check failed. Path={Path}", path);
            return false;
        }
    }

    private static bool TryWrite(string path, ILogger? logger)
    {
        var testFile = Path.Combine(path, $".mk_write_test_{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);
            logger?.LogInformation("Write check succeeded. Path={Path}", path);
            return true;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Write check failed. Path={Path}", path);
            try
            {
                if (File.Exists(testFile))
                {
                    File.Delete(testFile);
                }
            }
            catch
            {
                // ignored
            }

            return false;
        }
    }
}
