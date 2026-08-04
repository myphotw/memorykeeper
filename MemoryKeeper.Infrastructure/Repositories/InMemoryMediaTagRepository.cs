using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Domain.Entities;

namespace MemoryKeeper.Infrastructure.Repositories;

public sealed class InMemoryMediaTagRepository : IMediaTagRepository
{
    private readonly List<MediaTag> _items = [];

    public Task<IReadOnlyList<MediaTag>> GetByMediaIdAsync(
        Guid mediaId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<MediaTag>>(_items.Where(item => item.MediaId == mediaId).Select(Clone).ToList());

    public Task<IReadOnlyList<MediaTag>> GetByMediaIdsAsync(
        IReadOnlyCollection<Guid> mediaIds,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<MediaTag>>(
            _items.Where(item => mediaIds.Contains(item.MediaId)).Select(Clone).ToList());

    public Task<IReadOnlyList<MediaTag>> GetByTagIdAsync(
        Guid tagId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<MediaTag>>(_items.Where(item => item.TagId == tagId).Select(Clone).ToList());

    public Task<bool> ExistsAsync(Guid mediaId, Guid tagId, CancellationToken cancellationToken = default)
        => Task.FromResult(_items.Any(item => item.MediaId == mediaId && item.TagId == tagId));

    public Task<IReadOnlyList<Guid>> GetMediaIdsWithAllTagsAsync(
        IReadOnlyCollection<Guid> tagIds,
        CancellationToken cancellationToken = default)
    {
        var distinct = tagIds.Distinct().ToList();
        if (distinct.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<Guid>>([]);
        }

        var result = _items
            .Where(item => distinct.Contains(item.TagId))
            .GroupBy(item => item.MediaId)
            .Where(group => group.Select(item => item.TagId).Distinct().Count() == distinct.Count)
            .Select(group => group.Key)
            .ToList();

        return Task.FromResult<IReadOnlyList<Guid>>(result);
    }

    public Task AddRangeAsync(IEnumerable<MediaTag> mediaTags, CancellationToken cancellationToken = default)
    {
        _items.AddRange(mediaTags.Select(Clone));
        return Task.CompletedTask;
    }

    public Task DeleteAsync(MediaTag mediaTag, CancellationToken cancellationToken = default)
    {
        _items.RemoveAll(item => item.Id == mediaTag.Id);
        return Task.CompletedTask;
    }

    public Task DeleteRangeAsync(IEnumerable<MediaTag> mediaTags, CancellationToken cancellationToken = default)
    {
        var ids = mediaTags.Select(item => item.Id).ToHashSet();
        _items.RemoveAll(item => ids.Contains(item.Id));
        return Task.CompletedTask;
    }

    public Task DeleteByTagIdAsync(Guid tagId, CancellationToken cancellationToken = default)
    {
        _items.RemoveAll(item => item.TagId == tagId);
        return Task.CompletedTask;
    }

    public Task<int> CountByTagIdAsync(Guid tagId, CancellationToken cancellationToken = default)
        => Task.FromResult(_items.Count(item => item.TagId == tagId));

    private static MediaTag Clone(MediaTag mediaTag) => new()
    {
        Id = mediaTag.Id,
        MediaId = mediaTag.MediaId,
        TagId = mediaTag.TagId,
        CreatedAt = mediaTag.CreatedAt,
        UpdatedAt = mediaTag.UpdatedAt,
        Tag = mediaTag.Tag
    };
}
