using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Domain.Entities;
using MemoryKeeper.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace MemoryKeeper.Infrastructure.Repositories;

public sealed class MediaRepository : IMediaRepository
{
    private readonly MemoryKeeperDbContext _dbContext;

    public MediaRepository(MemoryKeeperDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Media?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _dbContext.Media
            .AsNoTracking()
            .FirstOrDefaultAsync(media => media.Id == id, cancellationToken);
    }

    public Task<Media?> GetByContentHashAsync(string contentHash, CancellationToken cancellationToken = default)
    {
        return _dbContext.Media
            .AsNoTracking()
            .FirstOrDefaultAsync(media => media.ContentHash == contentHash, cancellationToken);
    }

    public async Task<IReadOnlyList<Media>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var items = await _dbContext.Media
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        return MediaSort.ByCapturedDesc(items);
    }

    public Task<IReadOnlyList<Media>> GetByPlaceIdAsync(Guid placeId, CancellationToken cancellationToken = default)
    {
        return GetByPlaceAsync(placeId, cancellationToken);
    }

    public async Task<IReadOnlyList<Media>> GetByPlaceAsync(Guid placeId, CancellationToken cancellationToken = default)
    {
        var items = await CreateSearchQuery(year: null, placeId: placeId, placeIds: null)
            .ToListAsync(cancellationToken);
        return MediaSort.ByFavoriteThenCapturedDesc(items);
    }

    public async Task<IReadOnlyList<Media>> GetByYearAsync(int year, CancellationToken cancellationToken = default)
    {
        var items = await CreateSearchQuery(year: null, placeId: null, placeIds: null)
            .ToListAsync(cancellationToken);
        return MediaSort.ByCapturedDesc(items.Where(media => MediaQueryFilters.MatchesYear(media, year)));
    }

    public async Task<IReadOnlyList<Media>> GetWithGpsAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Media
            .AsNoTracking()
            .Where(media => media.Latitude != null && media.Longitude != null)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Media>> GetUnassignedAsync(CancellationToken cancellationToken = default)
    {
        var items = await _dbContext.Media
            .AsNoTracking()
            .Where(media => media.PlaceId == null)
            .ToListAsync(cancellationToken);
        return MediaSort.ByCapturedAsc(items);
    }

    public async Task<IReadOnlyList<Media>> GetByIdsAsync(
        IReadOnlyCollection<Guid> mediaIds,
        CancellationToken cancellationToken = default)
    {
        if (mediaIds.Count == 0)
        {
            return [];
        }

        return await _dbContext.Media
            .AsNoTracking()
            .Where(media => mediaIds.Contains(media.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Media>> SearchAsync(
        int? year,
        Guid? placeId,
        IReadOnlyCollection<Guid>? placeIds,
        CancellationToken cancellationToken = default)
    {
        var items = await CreateSearchQuery(year: null, placeId, placeIds)
            .ToListAsync(cancellationToken);
        if (year.HasValue)
        {
            items = items.Where(media => MediaQueryFilters.MatchesYear(media, year.Value)).ToList();
        }

        return MediaSort.ByFavoriteThenCapturedDesc(items);
    }

    public Task<Media?> GetPhotoDetailAsync(Guid mediaId, CancellationToken cancellationToken = default)
    {
        return _dbContext.Media
            .AsNoTracking()
            .Include(media => media.Place)
            .Include(media => media.Storage)
            .FirstOrDefaultAsync(media => media.Id == mediaId, cancellationToken);
    }

    public async Task UpdateFavoriteAsync(
        Guid mediaId,
        bool isFavorite,
        CancellationToken cancellationToken = default)
    {
        var media = await _dbContext.Media
            .FirstOrDefaultAsync(item => item.Id == mediaId, cancellationToken)
            ?? throw new InvalidOperationException($"Media '{mediaId}' was not found.");

        media.IsFavorite = isFavorite;
        media.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Media>> GetRelatedPhotosAsync(
        Guid placeId,
        Guid? excludeMediaId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Media
            .AsNoTracking()
            .Where(media => media.PlaceId == placeId);

        if (excludeMediaId.HasValue)
        {
            var excluded = excludeMediaId.Value;
            query = query.Where(media => media.Id != excluded);
        }

        var items = await query.ToListAsync(cancellationToken);
        return MediaSort.ByFavoriteThenCapturedDesc(items);
    }

    public async Task<IReadOnlyList<Media>> GetFavoritesAsync(CancellationToken cancellationToken = default)
    {
        var items = await _dbContext.Media
            .AsNoTracking()
            .Where(media => media.IsFavorite)
            .ToListAsync(cancellationToken);
        return MediaSort.ByCapturedDesc(items);
    }

    public async Task AddAsync(Media media, CancellationToken cancellationToken = default)
    {
        await _dbContext.Media.AddAsync(media, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _dbContext.Entry(media).State = EntityState.Detached;
    }

    public async Task UpdateAsync(Media media, CancellationToken cancellationToken = default)
    {
        var tracked = await _dbContext.Media
            .FirstOrDefaultAsync(item => item.Id == media.Id, cancellationToken);

        if (tracked is null)
        {
            _dbContext.Media.Update(media);
            await _dbContext.SaveChangesAsync(cancellationToken);
            _dbContext.Entry(media).State = EntityState.Detached;
            return;
        }

        _dbContext.Entry(tracked).CurrentValues.SetValues(media);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _dbContext.Entry(tracked).State = EntityState.Detached;
    }

    public async Task DeleteAsync(Media media, CancellationToken cancellationToken = default)
    {
        var tracked = await _dbContext.Media
            .FirstOrDefaultAsync(item => item.Id == media.Id, cancellationToken);

        if (tracked is null)
        {
            return;
        }

        _dbContext.Media.Remove(tracked);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private IQueryable<Media> CreateSearchQuery(
        int? year,
        Guid? placeId,
        IReadOnlyCollection<Guid>? placeIds)
    {
        var query = _dbContext.Media.AsNoTracking();

        if (year.HasValue)
        {
            // Year is filtered in-memory after materialization (SQLite DateTime standard).
        }

        if (placeId.HasValue)
        {
            var selectedPlaceId = placeId.Value;
            query = query.Where(media => media.PlaceId == selectedPlaceId);
        }

        if (placeIds is not null)
        {
            query = query.Where(media => media.PlaceId != null && placeIds.Contains(media.PlaceId.Value));
        }

        return query;
    }
}
