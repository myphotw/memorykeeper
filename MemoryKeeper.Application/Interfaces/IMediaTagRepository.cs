using MemoryKeeper.Domain.Entities;

namespace MemoryKeeper.Application.Interfaces;

public interface IMediaTagRepository
{
    Task<IReadOnlyList<MediaTag>> GetByMediaIdAsync(Guid mediaId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MediaTag>> GetByMediaIdsAsync(
        IReadOnlyCollection<Guid> mediaIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MediaTag>> GetByTagIdAsync(Guid tagId, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(Guid mediaId, Guid tagId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Media ids that contain all specified tags (AND).
    /// </summary>
    Task<IReadOnlyList<Guid>> GetMediaIdsWithAllTagsAsync(
        IReadOnlyCollection<Guid> tagIds,
        CancellationToken cancellationToken = default);

    Task AddRangeAsync(IEnumerable<MediaTag> mediaTags, CancellationToken cancellationToken = default);

    Task DeleteAsync(MediaTag mediaTag, CancellationToken cancellationToken = default);

    Task DeleteRangeAsync(IEnumerable<MediaTag> mediaTags, CancellationToken cancellationToken = default);

    Task DeleteByTagIdAsync(Guid tagId, CancellationToken cancellationToken = default);

    Task<int> CountByTagIdAsync(Guid tagId, CancellationToken cancellationToken = default);
}
