using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Domain.Entities;
using MemoryKeeper.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace MemoryKeeper.Infrastructure.Repositories;

public sealed class SettingRepository : ISettingRepository
{
    private readonly MemoryKeeperDbContext _dbContext;

    public SettingRepository(MemoryKeeperDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Setting?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _dbContext.Settings
            .AsNoTracking()
            .FirstOrDefaultAsync(setting => setting.Id == id, cancellationToken);
    }

    public Task<Setting?> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        return _dbContext.Settings
            .AsNoTracking()
            .FirstOrDefaultAsync(setting => setting.Key == key, cancellationToken);
    }

    public async Task<IReadOnlyList<Setting>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Settings
            .AsNoTracking()
            .OrderBy(setting => setting.Key)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Setting setting, CancellationToken cancellationToken = default)
    {
        var exists = await _dbContext.Settings
            .AsNoTracking()
            .AnyAsync(item => item.Id == setting.Id || item.Key == setting.Key, cancellationToken);
        if (exists)
        {
            throw new InvalidOperationException($"Setting '{setting.Key}' already exists.");
        }

        await _dbContext.Settings.AddAsync(setting, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _dbContext.Entry(setting).State = EntityState.Detached;
    }

    public async Task UpdateAsync(Setting setting, CancellationToken cancellationToken = default)
    {
        // GetByKey uses AsNoTracking; a long-lived DbContext may already track the same key/id.
        var tracked = await _dbContext.Settings
            .FirstOrDefaultAsync(
                item => item.Id == setting.Id || item.Key == setting.Key,
                cancellationToken);

        if (tracked is null)
        {
            _dbContext.Settings.Update(setting);
            await _dbContext.SaveChangesAsync(cancellationToken);
            _dbContext.Entry(setting).State = EntityState.Detached;
            return;
        }

        tracked.Value = setting.Value;
        tracked.UpdatedAt = setting.UpdatedAt;
        await _dbContext.SaveChangesAsync(cancellationToken);
        _dbContext.Entry(tracked).State = EntityState.Detached;
    }

    public async Task DeleteAsync(Setting setting, CancellationToken cancellationToken = default)
    {
        var tracked = await _dbContext.Settings
            .FirstOrDefaultAsync(
                item => item.Id == setting.Id || item.Key == setting.Key,
                cancellationToken);

        if (tracked is null)
        {
            return;
        }

        _dbContext.Settings.Remove(tracked);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
