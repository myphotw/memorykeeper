using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Domain.Entities;
using MemoryKeeper.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace MemoryKeeper.Tests.UnitTests;

public class PlaceAssignmentServiceTests
{
    [Fact]
    public async Task AssignAsync_ReturnsExistingPlace_WhenWithinRadius()
    {
        var existing = new Place
        {
            Id = Guid.NewGuid(),
            DisplayName = "Osaka Castle",
            Country = "일본",
            Province = "오사카",
            City = "오사카",
            Address = "Osaka",
            Latitude = 34.6873,
            Longitude = 135.5262,
            Radius = 300,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var placeRepository = new InMemoryPlaceRepository([existing]);
        var service = new PlaceAssignmentService(
            new StubLocationResolver(new LocationResult
            {
                DisplayName = "Osaka",
                Country = "일본",
                City = "오사카",
                Latitude = 34.6875,
                Longitude = 135.5264
            }),
            placeRepository,
            new InMemorySettingRepository(),
            NullLogger<PlaceAssignmentService>.Instance);

        var result = await service.AssignAsync(34.6875, 135.5264);

        Assert.NotNull(result);
        Assert.Equal(existing.Id, result!.Id);
        Assert.Single(await placeRepository.GetActiveAsync());
    }

    [Fact]
    public async Task AssignAsync_CreatesNewPlace_WhenNoMatch()
    {
        var placeRepository = new InMemoryPlaceRepository([]);
        var service = new PlaceAssignmentService(
            new StubLocationResolver(new LocationResult
            {
                DisplayName = "Nara Park",
                Country = "일본",
                Province = "나라",
                City = "나라",
                Address = "Nara Park",
                Latitude = 34.6851,
                Longitude = 135.8048
            }),
            placeRepository,
            new InMemorySettingRepository(new Dictionary<string, string>
            {
                [MemoryKeeper.Application.SettingKeys.PlaceDefaultRadiusMeters] = "150"
            }),
            NullLogger<PlaceAssignmentService>.Instance);

        var result = await service.AssignAsync(34.6851, 135.8048);

        Assert.NotNull(result);
        Assert.Equal("Nara Park", result!.DisplayName);
        Assert.Equal("Nara Park", result.CanonicalName);
        Assert.Equal(150d, result.Radius);
        Assert.True(result.IsActive);
        Assert.Single(await placeRepository.GetActiveAsync());
    }

    [Fact]
    public async Task AssignAsync_ReusesCanonical_WhenOsakaVariants()
    {
        var existing = new Place
        {
            Id = Guid.NewGuid(),
            DisplayName = "오사카",
            CanonicalName = "오사카",
            Country = "일본",
            City = "오사카",
            Latitude = 34.69,
            Longitude = 135.50,
            Radius = 200,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var placeRepository = new InMemoryPlaceRepository([existing]);
        var service = new PlaceAssignmentService(
            new StubLocationResolver(new LocationResult
            {
                DisplayName = "Osaka-shi",
                Country = "Japan",
                City = "Osaka",
                Latitude = 34.70,
                Longitude = 135.51
            }),
            placeRepository,
            new InMemorySettingRepository(),
            NullLogger<PlaceAssignmentService>.Instance);

        var result = await service.AssignAsync(34.70, 135.51);

        Assert.Equal(existing.Id, result.Id);
        Assert.Equal("오사카", result.CanonicalName);
        Assert.Single(await placeRepository.GetActiveAsync());
    }

    [Fact]
    public async Task AssignAsync_CreatesFallback_WhenGoogleReturnsNull()
    {
        var placeRepository = new InMemoryPlaceRepository([]);
        var service = new PlaceAssignmentService(
            new StubLocationResolver(null),
            placeRepository,
            new InMemorySettingRepository(),
            NullLogger<PlaceAssignmentService>.Instance);

        var result = await service.AssignAsync(3.2028, 73.2207);

        Assert.NotNull(result);
        Assert.Contains("GPS", result.DisplayName);
        Assert.Single(await placeRepository.GetActiveAsync());
    }

    [Fact]
    public async Task AssignAsync_MatchesByGooglePlaceId_BeforeRadius()
    {
        var existing = new Place
        {
            Id = Guid.NewGuid(),
            DisplayName = "Universal Studios Japan",
            Country = "일본",
            Province = "이쿠노",
            City = "오사카",
            GooglePlaceId = "ChIJ_usj",
            Category = "amusement_park",
            Latitude = 34.6654,
            Longitude = 135.4323,
            Radius = 50,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Far from existing coords so radius would NOT match — GooglePlaceId must win.
        var placeRepository = new InMemoryPlaceRepository([existing]);
        var service = new PlaceAssignmentService(
            new StubLocationResolver(new LocationResult
            {
                DisplayName = "Universal Studios Japan",
                Country = "일본",
                City = "오사카",
                Province = "고노하나",
                PlaceId = "ChIJ_usj",
                PlaceType = "amusement_park",
                Latitude = 34.9,
                Longitude = 135.9
            }),
            placeRepository,
            new InMemorySettingRepository(),
            NullLogger<PlaceAssignmentService>.Instance);

        var result = await service.AssignAsync(34.9, 135.9);

        Assert.NotNull(result);
        Assert.Equal(existing.Id, result!.Id);
        Assert.Single(await placeRepository.GetActiveAsync());
    }

    [Fact]
    public async Task AssignAsync_CreatesPlace_WithPoiNameAndType()
    {
        var placeRepository = new InMemoryPlaceRepository([]);
        var service = new PlaceAssignmentService(
            new StubLocationResolver(new LocationResult
            {
                DisplayName = "경복궁",
                Country = "대한민국",
                City = "서울",
                Province = "종로구",
                Address = "서울특별시 종로구",
                PlaceId = "ChIJgyeongbok",
                PlaceType = "tourist_attraction",
                Latitude = 37.5796,
                Longitude = 126.9770
            }),
            placeRepository,
            new InMemorySettingRepository(),
            NullLogger<PlaceAssignmentService>.Instance);

        var result = await service.AssignAsync(37.5796, 126.9770);

        Assert.NotNull(result);
        Assert.Equal("경복궁", result!.DisplayName);
        Assert.Equal("ChIJgyeongbok", result.GooglePlaceId);
        Assert.Equal("tourist_attraction", result.Category);
        Assert.Equal("서울", result.City);
        Assert.NotEqual("서울", result.DisplayName); // POI name, not city
    }

    private sealed class StubLocationResolver : ILocationResolver
    {
        private readonly LocationResult? _result;

        public StubLocationResolver(LocationResult? result)
        {
            _result = result;
        }

        public Task<LocationResult?> ResolveAsync(double latitude, double longitude, CancellationToken cancellationToken = default)
            => Task.FromResult(_result);

        public Task<LocationResult?> ResolveAddressAsync(string address, CancellationToken cancellationToken = default)
            => Task.FromResult(_result);

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

    private sealed class InMemoryPlaceRepository : IPlaceRepository
    {
        private readonly List<Place> _items;

        public InMemoryPlaceRepository(IEnumerable<Place> items)
        {
            _items = items.ToList();
        }

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
        {
            var normalized = keyword.Trim();
            return Task.FromResult<IReadOnlyList<Place>>(
                _items
                    .Where(item =>
                        item.DisplayName.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                        || item.City.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                        || item.Country.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                    .ToList());
        }

        public Task AddAsync(Place place, CancellationToken cancellationToken = default)
        {
            _items.Add(place);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Place place, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteAsync(Place place, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class InMemorySettingRepository : ISettingRepository
    {
        private readonly Dictionary<string, string> _values;

        public InMemorySettingRepository(Dictionary<string, string>? values = null)
        {
            _values = values ?? new Dictionary<string, string>();
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
}
