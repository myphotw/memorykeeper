using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Domain.Entities;
using MemoryKeeper.Domain.Enums;
using MemoryKeeper.Infrastructure.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

namespace MemoryKeeper.Tests.UnitTests;

public class PlaceRenormalizationServiceTests
{
    [Fact]
    public async Task RenormalizeAndMerge_MergesOsakaVariants()
    {
        var placeRepository = new MutablePlaceRepository();
        var mediaRepository = new InMemoryMediaRepository();

        var osaka = new Place
        {
            Id = Guid.NewGuid(),
            DisplayName = "Osaka",
            Country = "Japan",
            City = "Osaka",
            Latitude = 34.69,
            Longitude = 135.50,
            Radius = 200,
            IsActive = true,
            GooglePlaceId = "ChIJ1",
            CreatedAt = DateTime.UtcNow.AddDays(-2),
            UpdatedAt = DateTime.UtcNow
        };
        var osakaShi = new Place
        {
            Id = Guid.NewGuid(),
            DisplayName = "Osaka-shi",
            Country = "Japan",
            City = "Osaka",
            Latitude = 34.70,
            Longitude = 135.51,
            Radius = 200,
            IsActive = true,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            UpdatedAt = DateTime.UtcNow
        };
        var osakaCity = new Place
        {
            Id = Guid.NewGuid(),
            DisplayName = "大阪市",
            Country = "日本",
            City = "大阪",
            Latitude = 34.68,
            Longitude = 135.52,
            Radius = 200,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await placeRepository.AddAsync(osaka);
        await placeRepository.AddAsync(osakaShi);
        await placeRepository.AddAsync(osakaCity);

        var mediaId = Guid.NewGuid();
        await mediaRepository.AddAsync(new Media
        {
            Id = mediaId,
            FileName = "a.jpg",
            MediaType = MediaType.Photo,
            Status = MediaStatus.Imported,
            OriginalPath = @"D:\a.jpg",
            RelativePath = @"2026\a.jpg",
            ContentHash = "h1",
            CapturedAt = DateTime.UtcNow,
            ImportedAt = DateTime.UtcNow,
            StorageId = Guid.NewGuid(),
            PlaceId = osakaShi.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        var service = new PlaceRenormalizationService(
            placeRepository,
            mediaRepository,
            new NoOpMediaLibraryPathSyncService(),
            new NoOpPlaceDisplayNameRefreshService(),
            NullLogger<PlaceRenormalizationService>.Instance);

        var result = await service.RenormalizeAndMergeAsync();

        Assert.True(result.Succeeded);
        var remaining = await placeRepository.GetAllAsync();
        Assert.Single(remaining);
        Assert.Equal("오사카", remaining[0].CanonicalName);

        var media = await mediaRepository.GetByIdAsync(mediaId);
        Assert.NotNull(media);
        Assert.Equal(remaining[0].Id, media!.PlaceId);
    }

    private sealed class NoOpPlaceDisplayNameRefreshService : IPlaceDisplayNameRefreshService
    {
        public Task<int> RefreshKoreanNamesAsync(IEnumerable<Place> places, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }

    private sealed class MutablePlaceRepository : IPlaceRepository
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
            => Task.FromResult<IReadOnlyList<Place>>(_items.ToList());

        public Task AddAsync(Place place, CancellationToken cancellationToken = default)
        {
            _items.Add(place);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Place place, CancellationToken cancellationToken = default)
        {
            var index = _items.FindIndex(item => item.Id == place.Id);
            if (index >= 0)
            {
                _items[index] = place;
            }

            return Task.CompletedTask;
        }

        public Task DeleteAsync(Place place, CancellationToken cancellationToken = default)
        {
            _items.RemoveAll(item => item.Id == place.Id);
            return Task.CompletedTask;
        }
    }
}
