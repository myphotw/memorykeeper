using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Time;
using MemoryKeeper.Domain.Entities;
using MemoryKeeper.Domain.Enums;
using MemoryKeeper.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace MemoryKeeper.Infrastructure.Repositories;

public sealed class DashboardRepository : IDashboardRepository
{
    private readonly MemoryKeeperDbContext _dbContext;

    public DashboardRepository(MemoryKeeperDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<Media>> GetOnThisDayPhotosAsync(
        int month,
        int day,
        int lookbackYears,
        CancellationToken cancellationToken = default)
    {
        var candidates = await _dbContext.Media
            .AsNoTracking()
            .Include(media => media.Place)
            .Include(media => media.Storage)
            .Where(media => media.MediaType == MediaType.Photo)
            .Where(media => media.PlaceId != null)
            .ToListAsync(cancellationToken);

        return MediaSort.ByFavoriteThenCapturedDesc(
            candidates.Where(media => MediaQueryFilters.MatchesOnThisDay(media, month, day, lookbackYears)));
    }

    public async Task<IReadOnlyList<Media>> GetRecentImportsAsync(
        int take,
        CancellationToken cancellationToken = default)
    {
        var items = await _dbContext.Media
            .AsNoTracking()
            .Include(media => media.Storage)
            .Where(media => media.MediaType == MediaType.Photo)
            .ToListAsync(cancellationToken);

        return MediaSort.ByImportedDesc(items).Take(Math.Max(1, take)).ToList();
    }

    public async Task<IReadOnlyList<Media>> GetFavoritePhotosAsync(
        int take,
        CancellationToken cancellationToken = default)
    {
        var items = await _dbContext.Media
            .AsNoTracking()
            .Include(media => media.Storage)
            .Where(media => media.MediaType == MediaType.Photo && media.IsFavorite)
            .ToListAsync(cancellationToken);

        return MediaSort.ByUpdatedThenImportedDesc(items).Take(Math.Max(1, take)).ToList();
    }

    public async Task<DashboardStatisticsRaw> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var photos = await _dbContext.Media
            .AsNoTracking()
            .Where(media => media.MediaType == MediaType.Photo)
            .Select(media => new
            {
                media.Id,
                media.IsFavorite,
                media.PlaceId,
                media.CapturedAt,
                media.ImportedAt
            })
            .ToListAsync(cancellationToken);

        var placeCount = await _dbContext.Places
            .AsNoTracking()
            .CountAsync(place => place.IsActive, cancellationToken);

        var tagCount = await _dbContext.Tags
            .AsNoTracking()
            .CountAsync(tag => tag.Source == TagSource.User, cancellationToken);

        var visitRecordCount = photos
            .Where(media => media.PlaceId is not null)
            .GroupBy(media => media.PlaceId!.Value)
            .Sum(group => group
                .Select(media => (media.CapturedAt ?? media.ImportedAt).Date)
                .Distinct()
                .Count());

        return new DashboardStatisticsRaw
        {
            PhotoCount = photos.Count,
            PlaceCount = placeCount,
            FavoriteCount = photos.Count(media => media.IsFavorite),
            TagCount = tagCount,
            VisitRecordCount = visitRecordCount
        };
    }

    public async Task<PendingBreakdownRaw> GetPendingBreakdownAsync(CancellationToken cancellationToken = default)
    {
        var pending = await _dbContext.Media
            .AsNoTracking()
            .Where(media => media.MediaType == MediaType.Photo)
            .Where(media => media.Status == MediaStatus.Pending)
            .Select(media => new
            {
                media.Id,
                media.Latitude,
                media.Longitude,
                media.CapturedAt,
                media.ImportedAt
            })
            .ToListAsync(cancellationToken);

        var latest = pending
            .OrderByDescending(media => media.ImportedAt)
            .FirstOrDefault();

        var noGps = pending.Count(media => media.Latitude is null || media.Longitude is null);
        var hasGps = pending.Count(media => media.Latitude is not null && media.Longitude is not null);
        var unknownDate = pending.Count(media => media.CapturedAt is null);

        return new PendingBreakdownRaw
        {
            Total = pending.Count,
            NoGps = noGps,
            HasGps = hasGps,
            UnknownDate = unknownDate,
            RepresentativeMediaId = latest?.Id,
            LatestImportedAt = DateTimeHelper.ToUtcOffset(latest?.ImportedAt)
        };
    }
}
