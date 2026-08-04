using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Infrastructure.Database;

/// <summary>
/// Converts legacy absolute RelativePath values into RelativePath and normalizes separators.
/// Runs after schema rename migration.
/// </summary>
public static class RelativePathDataMigrator
{
    public static async Task<int> NormalizeAsync(
        MemoryKeeperDbContext dbContext,
        IFileAccessService fileAccessService,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var storages = await dbContext.Storages.AsNoTracking().ToListAsync(cancellationToken);
        var storageMap = storages.ToDictionary(storage => storage.Id);
        var mediaItems = await dbContext.Media.ToListAsync(cancellationToken);
        var updated = 0;

        foreach (var media in mediaItems)
        {
            storageMap.TryGetValue(media.StorageId, out var storage);
            var photoRoot = storage?.PhotoRoot;
            var normalized = fileAccessService.ToRelativePath(media.RelativePath, photoRoot);
            if (string.Equals(media.RelativePath, normalized, StringComparison.Ordinal))
            {
                continue;
            }

            media.RelativePath = normalized;
            media.UpdatedAt = DateTime.UtcNow;
            updated++;
        }

        if (updated > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        logger?.LogInformation("RelativePath normalization completed. Updated={UpdatedCount}", updated);
        return updated;
    }
}
