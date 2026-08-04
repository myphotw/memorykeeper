using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Domain.Entities;

namespace MemoryKeeper.Tests.UnitTests;

public class PlacePickerServiceTests
{
    [Fact]
    public async Task LoadAsync_ReturnsRecentFavoritesAndHierarchy()
    {
        var repository = new InMemoryPlaceRepository();
        var now = DateTime.UtcNow;
        await repository.AddAsync(new Place
        {
            Id = Guid.NewGuid(),
            DisplayName = "유니버설 스튜디오 재팬",
            Country = "일본",
            City = "오사카",
            Latitude = 34.6654,
            Longitude = 135.4323,
            Radius = 300,
            IsActive = true,
            IsFavorite = true,
            UsageCount = 3,
            LastUsedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });
        await repository.AddAsync(new Place
        {
            Id = Guid.NewGuid(),
            DisplayName = "서울숲",
            Country = "대한민국",
            City = "서울",
            Latitude = 37.5446,
            Longitude = 127.0372,
            Radius = 200,
            IsActive = true,
            UsageCount = 1,
            LastUsedAt = now.AddDays(-1),
            CreatedAt = now,
            UpdatedAt = now
        });

        var service = new PlacePickerService(repository);
        var data = await service.LoadAsync();

        Assert.Single(data.FavoritePlaces);
        Assert.Equal(2, data.RecentPlaces.Count);
        Assert.Equal(2, data.Hierarchy.Count);
        Assert.Contains(data.Hierarchy, node => node.Title == "일본");
        Assert.Contains(data.Hierarchy, node => node.Title == "대한민국");
    }

    [Fact]
    public async Task SearchAsync_FiltersByCityAlias()
    {
        var repository = new InMemoryPlaceRepository();
        var now = DateTime.UtcNow;
        await repository.AddAsync(new Place
        {
            Id = Guid.NewGuid(),
            DisplayName = "도톤보리",
            Country = "Japan",
            City = "Osaka",
            Latitude = 34.6687,
            Longitude = 135.5013,
            Radius = 150,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        });

        var service = new PlacePickerService(repository);
        var results = await service.SearchAsync("오사카");

        Assert.Single(results);
        Assert.Equal("도톤보리", results[0].DisplayName);
    }

    private sealed class InMemoryPlaceRepository : IPlaceRepository
    {
        private readonly List<Place> _places = [];

        public Task AddAsync(Place place, CancellationToken cancellationToken = default)
        {
            _places.Add(place);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Place place, CancellationToken cancellationToken = default)
        {
            _places.Remove(place);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Place>> GetActiveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Place>>(_places.Where(place => place.IsActive).ToList());

        public Task<IReadOnlyList<Place>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Place>>(_places.ToList());

        public Task<Place?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_places.FirstOrDefault(place => place.Id == id));

        public Task<IReadOnlyList<Place>> GetByIdsAsync(
            IReadOnlyCollection<Guid> placeIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Place>>(_places.Where(place => placeIds.Contains(place.Id)).ToList());

        public Task<IReadOnlyList<Place>> SearchAsync(string keyword, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Place>>([]);

        public Task UpdateAsync(Place place, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

public class PlaceNormalizerSearchTests
{
    [Fact]
    public void MatchesSearch_FindsPlaceByAlias()
    {
        var place = new Place
        {
            DisplayName = "ユニバーサル・スタジオ・ジャパン",
            Country = "日本",
            City = "大阪市",
            CanonicalName = "유니버설 스튜디오 재팬",
            Latitude = 34.6654,
            Longitude = 135.4323,
            Radius = 300
        };

        Assert.True(PlaceNormalizer.MatchesSearch(place, "유니버설"));
        Assert.True(PlaceNormalizer.MatchesSearch(place, "오사카"));
    }
}
