using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Domain.Entities;
using MemoryKeeper.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace MemoryKeeper.Infrastructure.Repositories;

public sealed class PlaceRepository : IPlaceRepository
{
    private readonly MemoryKeeperDbContext _dbContext;

    public PlaceRepository(MemoryKeeperDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Place?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _dbContext.Places
            .AsNoTracking()
            .FirstOrDefaultAsync(place => place.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Place>> GetByIdsAsync(
        IReadOnlyCollection<Guid> placeIds,
        CancellationToken cancellationToken = default)
    {
        if (placeIds.Count == 0)
        {
            return [];
        }

        return await _dbContext.Places
            .AsNoTracking()
            .Where(place => placeIds.Contains(place.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Place>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Places
            .AsNoTracking()
            .OrderBy(place => place.DisplayName)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Place>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Places
            .AsNoTracking()
            .Where(place => place.IsActive)
            .OrderBy(place => place.DisplayName)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Place>> SearchAsync(string keyword, CancellationToken cancellationToken = default)
    {
        var normalized = keyword.Trim();
        if (normalized.Length == 0)
        {
            return [];
        }

        var likePattern = $"%{normalized}%";
        return await _dbContext.Places
            .AsNoTracking()
            .Where(place =>
                EF.Functions.Like(place.DisplayName, likePattern)
                || EF.Functions.Like(place.City, likePattern)
                || EF.Functions.Like(place.Country, likePattern))
            .OrderBy(place => place.DisplayName)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Place place, CancellationToken cancellationToken = default)
    {
        await _dbContext.Places.AddAsync(place, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _dbContext.Entry(place).State = EntityState.Detached;
    }

    public async Task UpdateAsync(Place place, CancellationToken cancellationToken = default)
    {
        // Reads use AsNoTracking; long-lived DbContext may already track the same Place.
        var tracked = await _dbContext.Places
            .FirstOrDefaultAsync(item => item.Id == place.Id, cancellationToken);

        if (tracked is null)
        {
            _dbContext.Places.Update(place);
            await _dbContext.SaveChangesAsync(cancellationToken);
            _dbContext.Entry(place).State = EntityState.Detached;
            return;
        }

        _dbContext.Entry(tracked).CurrentValues.SetValues(place);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _dbContext.Entry(tracked).State = EntityState.Detached;
    }

    public async Task DeleteAsync(Place place, CancellationToken cancellationToken = default)
    {
        var tracked = await _dbContext.Places
            .FirstOrDefaultAsync(item => item.Id == place.Id, cancellationToken);

        if (tracked is null)
        {
            return;
        }

        _dbContext.Places.Remove(tracked);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
