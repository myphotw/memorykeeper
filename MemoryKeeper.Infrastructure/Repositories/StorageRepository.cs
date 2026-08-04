using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using StorageEntity = MemoryKeeper.Domain.Entities.Storage;

namespace MemoryKeeper.Infrastructure.Repositories;

public sealed class StorageRepository : IStorageRepository
{
    private readonly MemoryKeeperDbContext _dbContext;

    public StorageRepository(MemoryKeeperDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<StorageEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _dbContext.Storages
            .AsNoTracking()
            .FirstOrDefaultAsync(storage => storage.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<StorageEntity>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Storages
            .AsNoTracking()
            .OrderBy(storage => storage.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(StorageEntity storage, CancellationToken cancellationToken = default)
    {
        await _dbContext.Storages.AddAsync(storage, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(StorageEntity storage, CancellationToken cancellationToken = default)
    {
        var tracked = await _dbContext.Storages
            .FirstOrDefaultAsync(item => item.Id == storage.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Storage '{storage.Id}' was not found.");

        tracked.Name = storage.Name;
        tracked.StorageType = storage.StorageType;
        tracked.PhotoRoot = storage.PhotoRoot;
        tracked.IsActive = storage.IsActive;
        tracked.CreatedAt = storage.CreatedAt;
        tracked.UpdatedAt = storage.UpdatedAt;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(StorageEntity storage, CancellationToken cancellationToken = default)
    {
        _dbContext.Storages.Remove(storage);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
