using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Domain.Entities;
using MemoryKeeper.Domain.Enums;
using MemoryKeeper.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace MemoryKeeper.Infrastructure.Repositories;

public sealed class TagRepository : ITagRepository
{
    private readonly MemoryKeeperDbContext _dbContext;

    public TagRepository(MemoryKeeperDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Tag?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _dbContext.Tags
            .AsNoTracking()
            .FirstOrDefaultAsync(tag => tag.Id == id, cancellationToken);
    }

    public Task<Tag?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        var normalized = name.Trim();
        return _dbContext.Tags
            .AsNoTracking()
            .FirstOrDefaultAsync(
                tag => tag.Name.ToLower() == normalized.ToLower(),
                cancellationToken);
    }

    public async Task<IReadOnlyList<Tag>> GetAllAsync(
        TagSource? source = TagSource.User,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Tags.AsNoTracking().AsQueryable();
        if (source.HasValue)
        {
            query = query.Where(tag => tag.Source == source.Value);
        }

        return await query
            .OrderByDescending(tag => tag.UsageCount)
            .ThenBy(tag => tag.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Tag>> GetPopularAsync(
        int take = 20,
        TagSource? source = TagSource.User,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Tags.AsNoTracking().AsQueryable();
        if (source.HasValue)
        {
            query = query.Where(tag => tag.Source == source.Value);
        }

        return await query
            .OrderByDescending(tag => tag.UsageCount)
            .ThenBy(tag => tag.Name)
            .Take(Math.Max(1, take))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Tag>> SearchAsync(
        string keyword,
        TagSource? source = TagSource.User,
        CancellationToken cancellationToken = default)
    {
        var normalized = keyword.Trim();
        var query = _dbContext.Tags.AsNoTracking().AsQueryable();
        if (source.HasValue)
        {
            query = query.Where(tag => tag.Source == source.Value);
        }

        if (!string.IsNullOrWhiteSpace(normalized))
        {
            query = query.Where(tag => tag.Name.Contains(normalized));
        }

        return await query
            .OrderByDescending(tag => tag.UsageCount)
            .ThenBy(tag => tag.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Tag tag, CancellationToken cancellationToken = default)
    {
        await _dbContext.Tags.AddAsync(tag, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _dbContext.Entry(tag).State = EntityState.Detached;
    }

    public async Task UpdateAsync(Tag tag, CancellationToken cancellationToken = default)
    {
        var tracked = await _dbContext.Tags
            .FirstOrDefaultAsync(item => item.Id == tag.Id, cancellationToken);

        if (tracked is null)
        {
            _dbContext.Tags.Update(tag);
            await _dbContext.SaveChangesAsync(cancellationToken);
            _dbContext.Entry(tag).State = EntityState.Detached;
            return;
        }

        _dbContext.Entry(tracked).CurrentValues.SetValues(tag);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _dbContext.Entry(tracked).State = EntityState.Detached;
    }

    public async Task DeleteAsync(Tag tag, CancellationToken cancellationToken = default)
    {
        var tracked = await _dbContext.Tags
            .FirstOrDefaultAsync(item => item.Id == tag.Id, cancellationToken);

        if (tracked is null)
        {
            return;
        }

        _dbContext.Tags.Remove(tracked);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
