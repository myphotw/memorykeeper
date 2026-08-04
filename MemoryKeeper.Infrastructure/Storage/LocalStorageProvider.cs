using MemoryKeeper.Domain.Interfaces;

namespace MemoryKeeper.Infrastructure.Storage;

public sealed class LocalStorageProvider : IStorageProvider
{
    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(File.Exists(path));
    }

    public async Task CopyAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        await using var sourceStream = File.OpenRead(sourcePath);
        await using var destinationStream = File.Create(destinationPath);
        await sourceStream.CopyToAsync(destinationStream, cancellationToken);
    }

    public async Task MoveAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        cancellationToken.ThrowIfCancellationRequested();

        var sourceFull = Path.GetFullPath(sourcePath);
        var destinationFull = Path.GetFullPath(destinationPath);
        if (string.Equals(sourceFull, destinationFull, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var destinationDirectory = Path.GetDirectoryName(destinationFull);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        // MK-042P: destination orphans must be replaced — never leave two copies.
        if (File.Exists(destinationFull))
        {
            File.Delete(destinationFull);
        }

        try
        {
            File.Move(sourceFull, destinationFull, overwrite: false);
            return;
        }
        catch (IOException)
        {
            // Cross-volume / locked handles: copy then delete source (must succeed).
            await CopyAsync(sourceFull, destinationFull, cancellationToken);
            await DeleteWithRetryAsync(sourceFull, cancellationToken);

            if (File.Exists(sourceFull))
            {
                throw new IOException(
                    $"Library move left the source file after copy. Source={sourceFull}, Destination={destinationFull}");
            }
        }
    }

    private static async Task DeleteWithRetryAsync(string path, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!File.Exists(path))
                {
                    return;
                }

                File.Delete(path);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                await Task.Delay(50 * (attempt + 1), cancellationToken);
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                await Task.Delay(50 * (attempt + 1), cancellationToken);
            }
        }
    }
}
