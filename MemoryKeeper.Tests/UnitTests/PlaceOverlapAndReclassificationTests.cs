using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Domain.Entities;
using MemoryKeeper.Domain.Enums;
using MemoryKeeper.Infrastructure.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

namespace MemoryKeeper.Tests.UnitTests;

public class PlaceOverlapAndReclassificationTests
{
    [Fact]
    public async Task FindOverlappingPlaces_DetectsIntersectingRadii()
    {
        var placeRepository = new InMemoryPlaceRepository();
        var mediaRepository = new InMemoryMediaRepository();
        var service = CreatePlaceService(placeRepository, mediaRepository);

        await placeRepository.AddAsync(new Place
        {
            Id = Guid.NewGuid(),
            DisplayName = "장소A",
            Latitude = 37.5665,
            Longitude = 126.9780,
            Radius = 100,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        var overlaps = await service.FindOverlappingPlacesAsync(37.5665, 126.9780, 80);

        Assert.Single(overlaps);
        Assert.Equal("장소A", overlaps[0].DisplayName);
    }

    [Fact]
    public async Task Reclassify_WithReassignFromOtherPlaces_MovesExistingAssignments()
    {
        var placeRepository = new InMemoryPlaceRepository();
        var mediaRepository = new InMemoryMediaRepository();
        var reclassification = new PlaceReclassificationService(
            placeRepository,
            mediaRepository,
            new NoOpMediaLibraryPathSyncService(),
            NullLogger<PlaceReclassificationService>.Instance);

        var oldPlaceId = Guid.NewGuid();
        var newPlaceId = Guid.NewGuid();
        await placeRepository.AddAsync(CreatePlace(oldPlaceId, "옛장소", 50));
        await placeRepository.AddAsync(CreatePlace(newPlaceId, "새장소", 200));

        var mediaId = Guid.NewGuid();
        await mediaRepository.AddAsync(new Media
        {
            Id = mediaId,
            FileName = "photo.jpg",
            MediaType = MediaType.Photo,
            Status = MediaStatus.Imported,
            PlaceId = oldPlaceId,
            Latitude = 37.5665,
            Longitude = 126.9780,
            RelativePath = "a/photo.jpg",
            OriginalPath = "a/photo.jpg",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            ImportedAt = DateTime.UtcNow
        });

        var withoutSteal = await reclassification.ReclassifyAsync(newPlaceId, reassignFromOtherPlaces: false);
        Assert.Equal(0, withoutSteal.AssignedCount);

        var withSteal = await reclassification.ReclassifyAsync(newPlaceId, reassignFromOtherPlaces: true);
        Assert.Equal(1, withSteal.AssignedCount);
        Assert.Equal(1, withSteal.ReassignedFromOtherCount);

        var media = await mediaRepository.GetByIdAsync(mediaId);
        Assert.Equal(newPlaceId, media!.PlaceId);
    }

    [Fact]
    public async Task CountRadiusImpact_SeparatesUnassignedAndFromOther()
    {
        var placeRepository = new InMemoryPlaceRepository();
        var mediaRepository = new InMemoryMediaRepository();
        var service = CreatePlaceService(placeRepository, mediaRepository);

        var otherPlaceId = Guid.NewGuid();
        await placeRepository.AddAsync(CreatePlace(otherPlaceId, "다른곳", 50, 37.5, 126.9));

        await mediaRepository.AddAsync(new Media
        {
            Id = Guid.NewGuid(),
            FileName = "open.jpg",
            MediaType = MediaType.Photo,
            Status = MediaStatus.Pending,
            PlaceId = null,
            Latitude = 37.5665,
            Longitude = 126.9780,
            RelativePath = "open.jpg",
            OriginalPath = "open.jpg",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            ImportedAt = DateTime.UtcNow
        });
        await mediaRepository.AddAsync(new Media
        {
            Id = Guid.NewGuid(),
            FileName = "taken.jpg",
            MediaType = MediaType.Photo,
            Status = MediaStatus.Imported,
            PlaceId = otherPlaceId,
            Latitude = 37.5666,
            Longitude = 126.9781,
            RelativePath = "taken.jpg",
            OriginalPath = "taken.jpg",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            ImportedAt = DateTime.UtcNow
        });

        var impact = await service.CountRadiusImpactAsync(37.5665, 126.9780, 200);

        Assert.Equal(1, impact.UnassignedCount);
        Assert.Equal(1, impact.FromOtherPlacesCount);
        Assert.Equal(2, impact.TotalInRadius);
    }

    private static Place CreatePlace(
        Guid id,
        string name,
        double radius,
        double lat = 37.5665,
        double lng = 126.9780) =>
        new()
        {
            Id = id,
            DisplayName = name,
            Latitude = lat,
            Longitude = lng,
            Radius = radius,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

    private static PlaceService CreatePlaceService(
        InMemoryPlaceRepository placeRepository,
        InMemoryMediaRepository mediaRepository)
    {
        var reclassification = new PlaceReclassificationService(
            placeRepository,
            mediaRepository,
            new NoOpMediaLibraryPathSyncService(),
            NullLogger<PlaceReclassificationService>.Instance);
        return new PlaceService(
            placeRepository,
            mediaRepository,
            new EmptySettingRepository(),
            new NullLocationResolver(),
            reclassification,
            new NoOpMediaLibraryPathSyncService(),
            new VisitRecordService(),
            new CatalogInvalidation(),
            NullLogger<PlaceService>.Instance);
    }

    private sealed class InMemoryPlaceRepository : IPlaceRepository
    {
        private readonly List<Place> _items = [];

        public Task<Place?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(_items.Select(Clone).FirstOrDefault(item => item.Id == id));

        public Task<IReadOnlyList<Place>> GetByIdsAsync(
            IReadOnlyCollection<Guid> placeIds,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Place>>(
                _items.Where(item => placeIds.Contains(item.Id)).Select(Clone).ToList());

        public Task<IReadOnlyList<Place>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Place>>(_items.Select(Clone).ToList());

        public Task<IReadOnlyList<Place>> GetActiveAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Place>>(_items.Where(item => item.IsActive).Select(Clone).ToList());

        public Task<IReadOnlyList<Place>> SearchAsync(string keyword, CancellationToken cancellationToken = default)
            => GetAllAsync(cancellationToken);

        public Task AddAsync(Place place, CancellationToken cancellationToken = default)
        {
            _items.Add(Clone(place));
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Place place, CancellationToken cancellationToken = default)
        {
            var index = _items.FindIndex(item => item.Id == place.Id);
            if (index >= 0)
            {
                _items[index] = Clone(place);
            }

            return Task.CompletedTask;
        }

        public Task DeleteAsync(Place place, CancellationToken cancellationToken = default)
        {
            _items.RemoveAll(item => item.Id == place.Id);
            return Task.CompletedTask;
        }

        private static Place Clone(Place place) => new()
        {
            Id = place.Id,
            DisplayName = place.DisplayName,
            Country = place.Country,
            Province = place.Province,
            City = place.City,
            Address = place.Address,
            Latitude = place.Latitude,
            Longitude = place.Longitude,
            Radius = place.Radius,
            IsActive = place.IsActive,
            CreatedAt = place.CreatedAt,
            UpdatedAt = place.UpdatedAt
        };
    }

    private sealed class NullLocationResolver : ILocationResolver
    {
        public Task<LocationResult?> ResolveAsync(double latitude, double longitude, CancellationToken cancellationToken = default)
            => Task.FromResult<LocationResult?>(null);

        public Task<LocationResult?> ResolveAddressAsync(string address, CancellationToken cancellationToken = default)
            => Task.FromResult<LocationResult?>(null);

        public Task<IReadOnlyList<PlaceSuggestionDto>> SuggestPlacesAsync(
            string input,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<PlaceSuggestionDto>>([]);

        public Task<LocationResult?> ResolvePlaceIdAsync(
            string placeId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<LocationResult?>(null);

        public Task<IReadOnlyList<NearbyPlaceCandidateDto>> SearchNearbyAsync(
            double latitude,
            double longitude,
            int maxResults = 5,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<NearbyPlaceCandidateDto>>([]);
    }

    private sealed class EmptySettingRepository : ISettingRepository
    {
        public Task<Setting?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<Setting?>(null);

        public Task<Setting?> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult<Setting?>(null);

        public Task<IReadOnlyList<Setting>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Setting>>([]);

        public Task AddAsync(Setting setting, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task UpdateAsync(Setting setting, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteAsync(Setting setting, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
