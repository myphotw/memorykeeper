using MemoryKeeper.Domain.Entities;

namespace MemoryKeeper.Application.Interfaces;

/// <summary>
/// Keeps library RelativePath / on-disk folders aligned with place classification.
/// Pending → 미완성 추억/{file}; classified → {year}/{place}/{file}.
/// </summary>
public interface IMediaLibraryPathSyncService
{
    /// <summary>
    /// Moves the file when needed, updates <see cref="Media.RelativePath"/>, and persists the media row.
    /// </summary>
    Task<bool> SyncMediaPathAsync(
        Media media,
        Place? place,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Syncs every media item against its current PlaceId (startup / repair).
    /// </summary>
    Task<int> SyncAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Syncs all media linked to a place (e.g. after DisplayName rename).
    /// </summary>
    Task<int> SyncPlaceMediaAsync(Guid placeId, CancellationToken cancellationToken = default);
}
