using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Domain.Enums;
using MemoryKeeper.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace MemoryKeeper.Infrastructure.Repositories;

public sealed class TravelRecordsRepository : ITravelRecordsRepository
{
    private readonly MemoryKeeperDbContext _dbContext;
    private readonly IFileAccessService _fileAccessService;

    public TravelRecordsRepository(
        MemoryKeeperDbContext dbContext,
        IFileAccessService fileAccessService)
    {
        _dbContext = dbContext;
        _fileAccessService = fileAccessService;
    }

    public async Task<IReadOnlyList<TravelPlaceAggregateRaw>> GetPlaceAggregatesAsync(
        CancellationToken cancellationToken = default)
    {
        var places = await _dbContext.Places
            .AsNoTracking()
            .Where(place => place.IsActive)
            .ToListAsync(cancellationToken);

        if (places.Count == 0)
        {
            return [];
        }

        var placeIds = places.Select(place => place.Id).ToList();
        var mediaRows = await _dbContext.Media
            .AsNoTracking()
            .Include(media => media.Storage)
            .Where(media => media.MediaType == MediaType.Photo)
            .Where(media => media.PlaceId != null && placeIds.Contains(media.PlaceId.Value))
            .Select(media => new
            {
                media.Id,
                media.PlaceId,
                media.IsFavorite,
                media.CapturedAt,
                media.ImportedAt,
                media.RelativePath,
                StorageRoot = media.Storage != null ? media.Storage.PhotoRoot : null
            })
            .ToListAsync(cancellationToken);

        var mediaByPlace = mediaRows
            .Where(media => media.PlaceId.HasValue)
            .GroupBy(media => media.PlaceId!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());

        var result = new List<TravelPlaceAggregateRaw>();
        foreach (var place in places)
        {
            if (!mediaByPlace.TryGetValue(place.Id, out var media) || media.Count == 0)
            {
                continue;
            }

            var visitDates = media
                .Select(item => (item.CapturedAt ?? item.ImportedAt).Date)
                .Distinct()
                .OrderBy(date => date)
                .ToList();

            var representative = media
                .OrderByDescending(item => item.IsFavorite)
                .ThenByDescending(item => item.CapturedAt)
                .ThenByDescending(item => item.ImportedAt)
                .First();

            string? absolutePath = null;
            if (!string.IsNullOrWhiteSpace(representative.StorageRoot))
            {
                absolutePath = _fileAccessService.ResolveAbsolutePath(
                    representative.StorageRoot,
                    representative.RelativePath);
            }

            result.Add(new TravelPlaceAggregateRaw
            {
                PlaceId = place.Id,
                PlaceName = place.DisplayName,
                Country = place.Country,
                Latitude = place.Latitude,
                Longitude = place.Longitude,
                PhotoCount = media.Count,
                FavoriteCount = media.Count(item => item.IsFavorite),
                RepresentativeMediaId = representative.Id,
                AbsoluteLibraryPath = absolutePath,
                VisitDates = visitDates
            });
        }

        return result;
    }
}
