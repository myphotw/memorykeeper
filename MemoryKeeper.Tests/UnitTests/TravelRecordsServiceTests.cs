using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Domain.Entities;
using MemoryKeeper.Domain.Enums;
using MemoryKeeper.Infrastructure.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using StorageEntity = MemoryKeeper.Domain.Entities.Storage;

namespace MemoryKeeper.Tests.UnitTests;

public class TravelRecordsServiceTests
{
    [Fact]
    public async Task GetDashboardAsync_ComputesMostVisitedLongUnvisitedSeasonCountryAndFarthest()
    {
        var storageId = Guid.NewGuid();
        var sokcho = CreatePlace("속초", "대한민국", 38.2070, 128.5918);
        var busan = CreatePlace("부산", "대한민국", 35.1796, 129.0756);
        var maldives = CreatePlace("몰디브", "몰디브", 3.2028, 73.2207);

        var now = DateTime.Now;
        var media = new List<Media>
        {
            CreatePhoto("s1.jpg", storageId, sokcho, new DateTimeOffset(now.Year - 1, 4, 10, 10, 0, 0, TimeSpan.Zero)),
            CreatePhoto("s2.jpg", storageId, sokcho, new DateTimeOffset(now.Year - 1, 4, 11, 10, 0, 0, TimeSpan.Zero)),
            CreatePhoto("s3.jpg", storageId, sokcho, new DateTimeOffset(now.Year - 1, 7, 1, 10, 0, 0, TimeSpan.Zero)),
            CreatePhoto("b1.jpg", storageId, busan, new DateTimeOffset(2019, 5, 18, 10, 0, 0, TimeSpan.Zero)),
            CreatePhoto("b2.jpg", storageId, busan, new DateTimeOffset(2019, 5, 19, 10, 0, 0, TimeSpan.Zero)),
            CreatePhoto("m1.jpg", storageId, maldives, new DateTimeOffset(2024, 1, 5, 10, 0, 0, TimeSpan.Zero))
        };

        var storage = new StorageEntity
        {
            Id = storageId,
            Name = "Local",
            PhotoRoot = @"D:\Library",
            StorageType = StorageType.Local,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var fileAccess = new FakeFileAccessService();
        var repository = new InMemoryTravelRecordsRepository(fileAccess, media, [sokcho, busan, maldives], [storage]);
        var homeLocation = new HomeLocationService(
            new HomeSettingRepository(new Dictionary<string, string>
            {
                [SettingKeys.TravelHomeLatitude] = "37.5665",
                [SettingKeys.TravelHomeLongitude] = "126.9780",
                [SettingKeys.TravelHomeAddress] = "Seoul"
            }),
            new NoOpLocationResolver(),
            NullLogger<HomeLocationService>.Instance);
        var service = new TravelRecordsService(
            repository,
            new InMemoryMediaTagRepository(),
            new InMemoryTagRepository(),
            homeLocation,
            NullLogger<TravelRecordsService>.Instance);

        var dashboard = await service.GetDashboardAsync();

        Assert.NotNull(dashboard.MostVisitedPlace);
        Assert.Equal(sokcho.Id, dashboard.MostVisitedPlace!.PlaceId);
        Assert.Equal(3, dashboard.MostVisitedPlace.VisitRecordCount);

        Assert.NotNull(dashboard.LongUnvisitedPlace);
        Assert.Equal(busan.Id, dashboard.LongUnvisitedPlace!.PlaceId);

        Assert.Equal(4, dashboard.SeasonHighlights.Count);
        Assert.Contains(dashboard.SeasonHighlights, item =>
            item.Season == TravelSeason.Spring && item.PlaceId == sokcho.Id);

        Assert.NotNull(dashboard.TopCountry);
        Assert.Equal("대한민국", dashboard.TopCountry!.Country);

        Assert.NotNull(dashboard.FarthestPlace);
        Assert.Equal(maldives.Id, dashboard.FarthestPlace!.PlaceId);
        Assert.True(dashboard.FarthestPlace.DistanceKm > 1000);

        Assert.NotEmpty(dashboard.YearChapters);
        Assert.Contains(dashboard.YearChapters, chapter => chapter.Year == 2024);
        Assert.Contains(dashboard.YearChapters, chapter => chapter.Year == 2019);
        var chapter2024 = Assert.Single(dashboard.YearChapters, chapter => chapter.Year == 2024);
        Assert.Contains(chapter2024.Trips, trip => trip.TripName == "몰디브");
        var sokchoTrip = dashboard.YearChapters
            .SelectMany(chapter => chapter.Trips)
            .FirstOrDefault(trip => trip.TripName == "속초");
        Assert.NotNull(sokchoTrip);
        Assert.Equal(sokcho.Id, sokchoTrip!.FocusPlaceId);

        var mostVisitedDetail = await service.GetDetailAsync(TravelRecordsDetailKind.MostVisited);
        Assert.True(mostVisitedDetail.Places.Count >= 2);
        Assert.Equal(1, mostVisitedDetail.Places[0].Rank);

        var seasonDetail = await service.GetDetailAsync(TravelRecordsDetailKind.Season, TravelSeason.Winter);
        Assert.Contains(seasonDetail.Places, item => item.PlaceId == maldives.Id);
    }

    private static Place CreatePlace(string name, string country, double lat, double lon) => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = name,
        Country = country,
        City = name,
        Latitude = lat,
        Longitude = lon,
        Radius = 200,
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static Media CreatePhoto(
        string fileName,
        Guid storageId,
        Place place,
        DateTimeOffset capturedAt) => new()
    {
        Id = Guid.NewGuid(),
        FileName = fileName,
        MediaType = MediaType.Photo,
        Status = MediaStatus.Imported,
        OriginalPath = $@"D:\src\{fileName}",
        RelativePath = $@"2024\{fileName}",
        ContentHash = fileName,
        CapturedAt = capturedAt.UtcDateTime,
        ImportedAt = capturedAt.UtcDateTime,
        PlaceId = place.Id,
        Place = place,
        StorageId = storageId,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private sealed class HomeSettingRepository : ISettingRepository
    {
        private readonly Dictionary<string, string> _values;

        public HomeSettingRepository(Dictionary<string, string> values)
        {
            _values = values;
        }

        public Task<Setting?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<Setting?>(null);

        public Task<Setting?> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
        {
            if (!_values.TryGetValue(key, out var value))
            {
                return Task.FromResult<Setting?>(null);
            }

            return Task.FromResult<Setting?>(new Setting
            {
                Id = Guid.NewGuid(),
                Key = key,
                Value = value,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        public Task<IReadOnlyList<Setting>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Setting>>([]);

        public Task AddAsync(Setting setting, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task UpdateAsync(Setting setting, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteAsync(Setting setting, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class NoOpLocationResolver : ILocationResolver
    {
        public Task<LocationResult?> ResolveAsync(
            double latitude,
            double longitude,
            CancellationToken cancellationToken = default)
            => Task.FromResult<LocationResult?>(null);

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
}
