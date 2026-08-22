using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Application.Services;

/// <summary>
/// Advances the canonical Backend change cursor and marks existing screen caches dirty.
/// The first successful call establishes a baseline without replaying historical events.
/// </summary>
public sealed class BackendChangeMonitorService
{
    private readonly IBackendChangeFeed _feed;
    private readonly ICatalogInvalidation _invalidation;
    private readonly ILogger<BackendChangeMonitorService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _cursor;
    private bool _initialized;

    public BackendChangeMonitorService(
        IBackendChangeFeed feed,
        ICatalogInvalidation invalidation,
        ILogger<BackendChangeMonitorService> logger)
    {
        _feed = feed ?? throw new ArgumentNullException(nameof(feed));
        _invalidation = invalidation ?? throw new ArgumentNullException(nameof(invalidation));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> CheckForChangesAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var cursor = _cursor;
            var foundChanges = false;
            var affectedSurfaces = CatalogSurface.None;
            BackendChangeFeedDto page;
            do
            {
                page = await _feed.GetChangesAsync(cursor, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                foundChanges |= page.Items.Count > 0;
                foreach (var item in page.Items)
                {
                    affectedSurfaces |= SurfacesFor(item);
                }
                cursor = Math.Max(cursor, page.NextCursor);
            }
            while (page.HasMore);

            _cursor = cursor;
            if (!_initialized)
            {
                _initialized = true;
                _logger.LogInformation("Backend change cursor initialized at {Cursor}.", _cursor);
                return false;
            }

            if (foundChanges)
            {
                _invalidation.Invalidate(affectedSurfaces);
                _logger.LogInformation(
                    "Backend changes detected through cursor {Cursor}; invalidated surfaces {Surfaces}.",
                    _cursor,
                    affectedSurfaces);
            }

            return foundChanges;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static CatalogSurface SurfacesFor(BackendChangeEventDto change) =>
        change.ResourceType?.Trim() switch
        {
            // File creation/deletion and place assignment affect every derived screen.
            "MemoryKeeperFile" => CatalogSurface.AllRelated
                                  | (change.Tombstone ? CatalogSurface.Tags : CatalogSurface.None),
            "MemoryKeeperFilePlace" => CatalogSurface.AllRelated,
            // Favorite, memo and raw geography can affect Home/Gallery/map/travel and
            // the raw metadata shown by Pending items.
            "MemoryKeeperFileMetadata" => CatalogSurface.AllRelated,
            // Tag catalog/relation changes do not alter Pending membership or map grouping.
            "MemoryKeeperTag" or "MemoryKeeperFileTag" =>
                CatalogSurface.Gallery | CatalogSurface.Home | CatalogSurface.Travel | CatalogSurface.Tags,
            _ => CatalogSurface.AllRelated,
        };
}
