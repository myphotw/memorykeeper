using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Domain.Entities;
using MemoryKeeper.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace MemoryKeeper.Infrastructure.Repositories;

public sealed class MediaTagRepository : IMediaTagRepository
{
    private readonly MemoryKeeperDbContext _dbContext;

    public MediaTagRepository(MemoryKeeperDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<MediaTag>> GetByMediaIdAsync(
        Guid mediaId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.MediaTags
            .AsNoTracking()
            .Include(mediaTag => mediaTag.Tag)
            .Where(mediaTag => mediaTag.MediaId == mediaId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MediaTag>> GetByMediaIdsAsync(
        IReadOnlyCollection<Guid> mediaIds,
        CancellationToken cancellationToken = default)
    {
        if (mediaIds.Count == 0)
        {
            return [];
        }

        return await _dbContext.MediaTags
            .AsNoTracking()
            .Include(mediaTag => mediaTag.Tag)
            .Where(mediaTag => mediaIds.Contains(mediaTag.MediaId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MediaTag>> GetByTagIdAsync(
        Guid tagId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.MediaTags
            .AsNoTracking()
            .Where(mediaTag => mediaTag.TagId == tagId)
            .ToListAsync(cancellationToken);
    }

    public Task<bool> ExistsAsync(Guid mediaId, Guid tagId, CancellationToken cancellationToken = default)
    {
        return _dbContext.MediaTags
            .AsNoTracking()
            .AnyAsync(
                mediaTag => mediaTag.MediaId == mediaId && mediaTag.TagId == tagId,
                cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetMediaIdsWithAllTagsAsync(
        IReadOnlyCollection<Guid> tagIds,
        CancellationToken cancellationToken = default)
    {
        if (tagIds.Count == 0)
        {
            return [];
        }

        var distinctTagIds = tagIds.Distinct().ToList();
        var requiredCount = distinctTagIds.Count;

        return await _dbContext.MediaTags
            .AsNoTracking()
            .Where(mediaTag => distinctTagIds.Contains(mediaTag.TagId))
            .GroupBy(mediaTag => mediaTag.MediaId)
            .Where(group => group.Select(item => item.TagId).Distinct().Count() == requiredCount)
            .Select(group => group.Key)
            .ToListAsync(cancellationToken);
    }

    public async Task AddRangeAsync(IEnumerable<MediaTag> mediaTags, CancellationToken cancellationToken = default)
    {
        await _dbContext.MediaTags.AddRangeAsync(mediaTags, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(MediaTag mediaTag, CancellationToken cancellationToken = default)
    {
        var tracked = await _dbContext.MediaTags
            .FirstOrDefaultAsync(item => item.Id == mediaTag.Id, cancellationToken);
        if (tracked is null)
        {
            return;
        }

        _dbContext.MediaTags.Remove(tracked);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteRangeAsync(IEnumerable<MediaTag> mediaTags, CancellationToken cancellationToken = default)
    {
        var ids = mediaTags.Select(item => item.Id).Distinct().ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var tracked = await _dbContext.MediaTags
            .Where(item => ids.Contains(item.Id))
            .ToListAsync(cancellationToken);
        if (tracked.Count == 0)
        {
            return;
        }

        _dbContext.MediaTags.RemoveRange(tracked);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteByTagIdAsync(Guid tagId, CancellationToken cancellationToken = default)
    {
        var items = await _dbContext.MediaTags
            .Where(mediaTag => mediaTag.TagId == tagId)
            .ToListAsync(cancellationToken);
        if (items.Count == 0)
        {
            return;
        }

        _dbContext.MediaTags.RemoveRange(items);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<int> CountByTagIdAsync(Guid tagId, CancellationToken cancellationToken = default)
    {
        return _dbContext.MediaTags
            .AsNoTracking()
            .CountAsync(mediaTag => mediaTag.TagId == tagId, cancellationToken);
    }
}
