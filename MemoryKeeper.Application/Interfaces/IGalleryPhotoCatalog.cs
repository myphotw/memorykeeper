using MemoryKeeper.Application.DTOs.Gallery;

namespace MemoryKeeper.Application.Interfaces;

/// <summary>
/// Provides a complete NAS Gallery view without falling back to local SQLite.
/// </summary>
public interface IGalleryPhotoCatalog
{
    Task<GalleryPhotoCatalogSnapshot> QueryAsync(
        int? year = null,
        string? country = null,
        string? keyword = null,
        CancellationToken cancellationToken = default);
}
