using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Domain.Entities;
using MemoryKeeper.Domain.Enums;
using MemoryKeeper.Infrastructure.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using StorageEntity = MemoryKeeper.Domain.Entities.Storage;

namespace MemoryKeeper.Tests.UnitTests;

public class PendingMemoryServiceTests
{
    [Fact]
    public async Task GetPendingMemoriesAsync_SeparatesNoGpsGroupsAndGpsReclassificationCandidates()
    {
        var storageId = Guid.NewGuid();
        var mediaRepository = new InMemoryMediaRepository();
        var storageRepository = new FakeStorageRepository();
        await storageRepository.AddAsync(new StorageEntity
        {
            Id = storageId,
            Name = "Local",
            PhotoRoot = @"D:\Library",
            StorageType = StorageType.Local,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        var day = new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);
        await mediaRepository.AddAsync(CreateMedia("IMG_001.jpg", storageId, day, gps: false, placeId: null));
        await mediaRepository.AddAsync(CreateMedia("IMG_002.jpg", storageId, day.AddMinutes(5), gps: false, placeId: null));
        await mediaRepository.AddAsync(CreateMedia("GPS_FAIL.jpg", storageId, day, gps: true, placeId: null));

        var placeRepository = new FakePlaceRepository();
        var place = new Place
        {
            Id = Guid.NewGuid(),
            DisplayName = "Osaka",
            Country = "Japan",
            City = "Osaka",
            Latitude = 34.6,
            Longitude = 135.5,
            Radius = 200,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await placeRepository.AddAsync(place);

        var assignmentService = new MediaPlaceAssignmentService(
            mediaRepository,
            placeRepository,
            new NoOpMediaLibraryPathSyncService(),
            new CatalogInvalidation(),
            NullLogger<MediaPlaceAssignmentService>.Instance);

        var service = new PendingMemoryService(
            mediaRepository,
            storageRepository,
            new FakeFileAccessService(),
            new StubLocationResolver(),
            new MemoryGroupingService(),
            assignmentService,
            NullLogger<PendingMemoryService>.Instance);

        var overview = await service.GetPendingMemoriesAsync();

        Assert.Single(overview.Groups);
        Assert.Equal(2, overview.Groups[0].MediaCount);
        Assert.Equal("2026-05-01 사진 그룹", overview.Groups[0].GroupName);
        Assert.Single(overview.ReclassificationCandidates);
        Assert.Equal("GPS_FAIL.jpg", overview.ReclassificationCandidates[0].FileName);

        var assignResult = await service.AssignPlaceAsync(new AssignMediaPlaceRequest
        {
            PlaceId = place.Id,
            MediaIds = overview.Groups[0].MediaItems.Select(item => item.MediaId).ToList()
        });

        Assert.Equal(2, assignResult.UpdatedCount);

        var assigned = await mediaRepository.GetByIdsAsync(
            overview.Groups[0].MediaItems.Select(item => item.MediaId).ToList());
        Assert.All(assigned, media =>
        {
            Assert.Equal(place.Id, media.PlaceId);
            Assert.Equal(MediaStatus.Imported, media.Status);
            Assert.Equal(place.Latitude, media.Latitude);
            Assert.Equal(place.Longitude, media.Longitude);
        });

        var remaining = await service.GetPendingMemoriesAsync();
        Assert.Empty(remaining.Groups);
        Assert.Single(remaining.ReclassificationCandidates);
    }

    private static Media CreateMedia(
        string fileName,
        Guid storageId,
        DateTimeOffset capturedAt,
        bool gps,
        Guid? placeId)
    {
        return new Media
        {
            Id = Guid.NewGuid(),
            FileName = fileName,
            MediaType = MediaType.Photo,
            Status = MediaStatus.Pending,
            OriginalPath = $@"D:\src\{fileName}",
            RelativePath = $@"2026\{fileName}",
            ContentHash = Guid.NewGuid().ToString("N"),
            CapturedAt = capturedAt.UtcDateTime,
            ImportedAt = capturedAt.UtcDateTime,
            Latitude = gps ? 34.6873 : null,
            Longitude = gps ? 135.5262 : null,
            PlaceId = placeId,
            StorageId = storageId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private sealed class StubLocationResolver : ILocationResolver
    {
        public Task<LocationResult?> ResolveAsync(
            double latitude,
            double longitude,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<LocationResult?>(new LocationResult
            {
                DisplayName = "Osaka",
                Country = "Japan",
                City = "Osaka",
                Address = "Osaka Castle",
                Latitude = latitude,
                Longitude = longitude
            });
        }

        public Task<LocationResult?> ResolveAddressAsync(
            string address,
            CancellationToken cancellationToken = default)
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

    private sealed class FakeStorageRepository : IStorageRepository
    {
        private readonly List<StorageEntity> _items = [];

        public Task<StorageEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(_items.FirstOrDefault(item => item.Id == id));

        public Task<IReadOnlyList<StorageEntity>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<StorageEntity>>(_items.ToList());

        public Task AddAsync(StorageEntity storage, CancellationToken cancellationToken = default)
        {
            _items.Add(storage);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(StorageEntity storage, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteAsync(StorageEntity storage, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakePlaceRepository : IPlaceRepository
    {
        private readonly List<Place> _items = [];

        public Task<Place?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(_items.FirstOrDefault(item => item.Id == id));

        public Task<IReadOnlyList<Place>> GetByIdsAsync(
            IReadOnlyCollection<Guid> placeIds,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Place>>(_items.Where(item => placeIds.Contains(item.Id)).ToList());

        public Task<IReadOnlyList<Place>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Place>>(_items.ToList());

        public Task<IReadOnlyList<Place>> GetActiveAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Place>>(_items.Where(item => item.IsActive).ToList());

        public Task<IReadOnlyList<Place>> SearchAsync(string keyword, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Place>>([]);

        public Task AddAsync(Place place, CancellationToken cancellationToken = default)
        {
            _items.Add(place);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Place place, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteAsync(Place place, CancellationToken cancellationToken = default)
        {
            _items.RemoveAll(item => item.Id == place.Id);
            return Task.CompletedTask;
        }

    }
}
