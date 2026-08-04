using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Domain.Entities;
using MemoryKeeper.Domain.Enums;
using MemoryKeeper.Infrastructure.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

namespace MemoryKeeper.Tests.UnitTests;

public class PlaceServiceTests
{
    [Fact]
    public async Task CreateUpdateAndReclassify_WorksAsExpected()
    {
        var placeRepository = new InMemoryPlaceRepository();
        var mediaRepository = new InMemoryMediaRepository();
        var settingRepository = new EmptySettingRepository();
        var reclassification = new PlaceReclassificationService(
            placeRepository,
            mediaRepository,
            new NoOpMediaLibraryPathSyncService(),
            NullLogger<PlaceReclassificationService>.Instance);
        var service = new PlaceService(
            placeRepository,
            mediaRepository,
            settingRepository,
            new NullLocationResolver(),
            reclassification,
            new NoOpMediaLibraryPathSyncService(),
            new VisitRecordService(),
            new CatalogInvalidation(),
            NullLogger<PlaceService>.Instance);

        var created = await service.CreatePlaceAsync(new CreatePlaceRequest
        {
            DisplayName = "Osaka",
            Country = "일본",
            City = "오사카",
            Latitude = 34.6873,
            Longitude = 135.5262,
            Radius = 100,
            IsActive = true
        });

        await mediaRepository.AddAsync(new Media
        {
            Id = Guid.NewGuid(),
            FileName = "near.jpg",
            MediaType = MediaType.Photo,
            Status = MediaStatus.Imported,
            OriginalPath = @"D:\a.jpg",
            RelativePath = @"2025\a.jpg",
            ContentHash = "h1",
            CapturedAt = new DateTimeOffset(2025, 7, 1, 10, 0, 0, TimeSpan.Zero).UtcDateTime,
            ImportedAt = DateTime.UtcNow,
            Latitude = 34.6874,
            Longitude = 135.5263,
            StorageId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await mediaRepository.AddAsync(new Media
        {
            Id = Guid.NewGuid(),
            FileName = "far.jpg",
            MediaType = MediaType.Photo,
            Status = MediaStatus.Imported,
            OriginalPath = @"D:\b.jpg",
            RelativePath = @"2025\b.jpg",
            ContentHash = "h2",
            CapturedAt = new DateTimeOffset(2025, 7, 2, 10, 0, 0, TimeSpan.Zero).UtcDateTime,
            ImportedAt = DateTime.UtcNow,
            Latitude = 35.0,
            Longitude = 136.0,
            PlaceId = created.Id,
            StorageId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        var updated = await service.UpdatePlaceAsync(new UpdatePlaceRequest
        {
            Id = created.Id,
            DisplayName = "Osaka Castle",
            Country = "일본",
            City = "오사카",
            Latitude = 34.6873,
            Longitude = 135.5262,
            Radius = 200,
            IsActive = true,
            ReclassifyMedia = true
        });

        Assert.Equal("Osaka Castle", updated.DisplayName);
        Assert.Equal(1, updated.MediaCount);
        Assert.Equal(1, updated.VisitRecordCount);

        var otherPlace = await service.CreatePlaceAsync(new CreatePlaceRequest
        {
            DisplayName = "Nearby Cafe",
            Country = "일본",
            City = "오사카",
            Latitude = 34.6875,
            Longitude = 135.5264,
            Radius = 50,
            IsActive = true
        });

        var ownedByOther = Guid.NewGuid();
        await mediaRepository.AddAsync(new Media
        {
            Id = ownedByOther,
            FileName = "owned.jpg",
            MediaType = MediaType.Photo,
            Status = MediaStatus.Imported,
            OriginalPath = @"D:\c.jpg",
            RelativePath = @"2025\c.jpg",
            ContentHash = "h3",
            CapturedAt = new DateTimeOffset(2025, 7, 3, 10, 0, 0, TimeSpan.Zero).UtcDateTime,
            ImportedAt = DateTime.UtcNow,
            Latitude = 34.6874,
            Longitude = 135.5263,
            PlaceId = otherPlace.Id,
            StorageId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await service.UpdatePlaceAsync(new UpdatePlaceRequest
        {
            Id = created.Id,
            DisplayName = "Osaka Castle",
            Country = "일본",
            City = "오사카",
            Latitude = 34.6873,
            Longitude = 135.5262,
            Radius = 500,
            IsActive = true,
            ReclassifyMedia = true
        });

        var untouched = await mediaRepository.GetByIdAsync(ownedByOther);
        Assert.NotNull(untouched);
        Assert.Equal(otherPlace.Id, untouched!.PlaceId);

        var toggled = await service.SetPlaceActiveAsync(created.Id, false);
        Assert.False(toggled.IsActive);

        var detail = await service.GetPlaceAsync(created.Id);
        Assert.Equal("Osaka Castle", detail.DisplayName);
        Assert.False(detail.IsActive);
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
        {
            var normalized = keyword.Trim();
            return Task.FromResult<IReadOnlyList<Place>>(
                _items
                    .Where(item =>
                        item.DisplayName.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                        || item.City.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                        || item.Country.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                    .Select(Clone)
                    .ToList());
        }

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

    [Fact]
    public async Task CreateOrGetFromGooglePlace_PrefersPlaceDetailsCoordinatesOverSeed()
    {
        var placeRepository = new InMemoryPlaceRepository();
        var mediaRepository = new InMemoryMediaRepository();
        var settingRepository = new EmptySettingRepository();
        var reclassification = new PlaceReclassificationService(
            placeRepository,
            mediaRepository,
            new NoOpMediaLibraryPathSyncService(),
            NullLogger<PlaceReclassificationService>.Instance);
        var resolver = new FixedPlaceIdResolver(34.6873, 135.5262, "Osaka Castle");
        var service = new PlaceService(
            placeRepository,
            mediaRepository,
            settingRepository,
            resolver,
            reclassification,
            new NoOpMediaLibraryPathSyncService(),
            new VisitRecordService(),
            new CatalogInvalidation(),
            NullLogger<PlaceService>.Instance);

        var created = await service.CreateOrGetFromGooglePlaceAsync(
            "google-place-1",
            "Osaka",
            seedLatitude: 0,
            seedLongitude: 0);

        Assert.Equal(34.6873, created.Latitude);
        Assert.Equal(135.5262, created.Longitude);
        Assert.Equal("Osaka Castle", created.DisplayName);
    }

    [Fact]
    public async Task CreateOrGetFromGooglePlace_RefreshesStaleCoordinatesOnExistingPlace()
    {
        var placeRepository = new InMemoryPlaceRepository();
        var mediaRepository = new InMemoryMediaRepository();
        var settingRepository = new EmptySettingRepository();
        var reclassification = new PlaceReclassificationService(
            placeRepository,
            mediaRepository,
            new NoOpMediaLibraryPathSyncService(),
            NullLogger<PlaceReclassificationService>.Instance);
        var resolver = new FixedPlaceIdResolver(4.1012, 73.3950, "Ozen Reserve Bolifushi");
        var service = new PlaceService(
            placeRepository,
            mediaRepository,
            settingRepository,
            resolver,
            reclassification,
            new NoOpMediaLibraryPathSyncService(),
            new VisitRecordService(),
            new CatalogInvalidation(),
            NullLogger<PlaceService>.Instance);

        await placeRepository.AddAsync(new Place
        {
            Id = Guid.NewGuid(),
            DisplayName = "Ozen Reserve Bolifushi",
            CanonicalName = "Ozen Reserve Bolifushi",
            GooglePlaceId = "google-place-1",
            Country = "몰디브",
            Latitude = 37.5665,
            Longitude = 126.9780,
            Radius = 100,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        var refreshed = await service.CreateOrGetFromGooglePlaceAsync("google-place-1");

        Assert.Equal(4.1012, refreshed.Latitude);
        Assert.Equal(73.3950, refreshed.Longitude);
    }

    private sealed class FixedPlaceIdResolver : ILocationResolver
    {
        private readonly double _lat;
        private readonly double _lng;
        private readonly string _name;

        public FixedPlaceIdResolver(double lat, double lng, string name)
        {
            _lat = lat;
            _lng = lng;
            _name = name;
        }

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
            => Task.FromResult<LocationResult?>(new LocationResult
            {
                DisplayName = _name,
                Country = "일본",
                City = "오사카",
                Address = _name,
                Latitude = _lat,
                Longitude = _lng,
                PlaceId = placeId
            });

        public Task<IReadOnlyList<NearbyPlaceCandidateDto>> SearchNearbyAsync(
            double latitude,
            double longitude,
            int maxResults = 5,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<NearbyPlaceCandidateDto>>([]);
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

