using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Domain.Entities;
using MemoryKeeper.Domain.Enums;
using StorageEntity = MemoryKeeper.Domain.Entities.Storage;

namespace MemoryKeeper.Infrastructure.Repositories;

public sealed class InMemoryTravelRecordsRepository : ITravelRecordsRepository
{
    private readonly IFileAccessService _fileAccessService;
    private readonly List<Media> _media;
    private readonly List<Place> _places;
    private readonly List<StorageEntity> _storages;

    public InMemoryTravelRecordsRepository(
        IFileAccessService fileAccessService,
        IEnumerable<Media>? media = null,
        IEnumerable<Place>? places = null,
        IEnumerable<StorageEntity>? storages = null)
    {
        _fileAccessService = fileAccessService;
        _media = media?.ToList() ?? [];
        _places = places?.ToList() ?? [];
        _storages = storages?.ToList() ?? [];
    }

    public Task<IReadOnlyList<TravelPlaceAggregateRaw>> GetPlaceAggregatesAsync(
        CancellationToken cancellationToken = default)
    {
        var storageMap = _storages.ToDictionary(storage => storage.Id);
        IReadOnlyList<TravelPlaceAggregateRaw> result = _places
            .Where(place => place.IsActive)
            .Select(place =>
            {
                var media = _media
                    .Where(item => item.MediaType == MediaType.Photo && item.PlaceId == place.Id)
                    .ToList();
                if (media.Count == 0)
                {
                    return null;
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

                string? path = null;
                if (representative.Storage is not null)
                {
                    path = _fileAccessService.ResolveAbsolutePath(
                        representative.Storage.PhotoRoot,
                        representative.RelativePath);
                }
                else if (storageMap.TryGetValue(representative.StorageId, out var storage))
                {
                    path = _fileAccessService.ResolveAbsolutePath(storage.PhotoRoot, representative.RelativePath);
                }

                return new TravelPlaceAggregateRaw
                {
                    PlaceId = place.Id,
                    PlaceName = place.DisplayName,
                    Country = place.Country,
                    Latitude = place.Latitude,
                    Longitude = place.Longitude,
                    PhotoCount = media.Count,
                    FavoriteCount = media.Count(item => item.IsFavorite),
                    RepresentativeMediaId = representative.Id,
                    AbsoluteLibraryPath = path,
                    VisitDates = visitDates
                };
            })
            .Where(item => item is not null)
            .Cast<TravelPlaceAggregateRaw>()
            .ToList();

        return Task.FromResult(result);
    }
}
