using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Domain.Entities;
using MemoryKeeper.Domain.Enums;
using MemoryKeeper.Infrastructure.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using StorageEntity = MemoryKeeper.Domain.Entities.Storage;

namespace MemoryKeeper.Tests.UnitTests;

public class HomeDashboardServiceTests
{
    [Fact]
    public async Task GetDashboardAsync_BuildsHeroTodayFavoritesImportsPendingAndStats()
    {
        var today = DateTime.Now;
        var storageId = Guid.NewGuid();
        var osaka = CreatePlace("오사카");
        var tokyo = CreatePlace("도쿄");

        var onThisDayOsaka = CreatePhoto(
            "osaka1.jpg",
            storageId,
            osaka,
            new DateTimeOffset(today.Year - 5, today.Month, today.Day, 10, 0, 0, TimeSpan.Zero),
            isFavorite: true);
        var onThisDayOsaka2 = CreatePhoto(
            "osaka2.jpg",
            storageId,
            osaka,
            new DateTimeOffset(today.Year - 5, today.Month, today.Day, 11, 0, 0, TimeSpan.Zero));
        var onThisDayTokyo = CreatePhoto(
            "tokyo1.jpg",
            storageId,
            tokyo,
            new DateTimeOffset(today.Year - 7, today.Month, today.Day, 9, 0, 0, TimeSpan.Zero));
        var recentVisit = CreatePhoto(
            "recent.jpg",
            storageId,
            tokyo,
            DateTimeOffset.Now.AddDays(-2));
        var favorite = CreatePhoto(
            "fav.jpg",
            storageId,
            osaka,
            DateTimeOffset.Now.AddDays(-10),
            isFavorite: true);
        favorite.UpdatedAt = DateTime.UtcNow;
        var imported = CreatePhoto(
            "import.jpg",
            storageId,
            osaka,
            DateTimeOffset.Now.AddDays(-30));
        imported.ImportedAt = DateTime.UtcNow.AddMinutes(-1);
        var pending = CreatePhoto(
            "pending.jpg",
            storageId,
            place: null,
            capturedAt: null,
            status: MediaStatus.Pending);
        pending.PlaceId = null;
        pending.ImportedAt = DateTime.UtcNow.AddDays(-40);

        var media = new List<Media>
        {
            onThisDayOsaka,
            onThisDayOsaka2,
            onThisDayTokyo,
            recentVisit,
            favorite,
            imported,
            pending
        };

        var mediaRepository = new InMemoryMediaRepository();
        foreach (var item in media)
        {
            await mediaRepository.AddAsync(item);
        }

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

        var placeRepository = new InMemoryPlaceRepository([osaka, tokyo]);
        var tagRepository = new InMemoryTagRepository();
        var mediaTagRepository = new InMemoryMediaTagRepository();
        var blossom = await CreateTagAsync(tagRepository, "벚꽃");
        await mediaTagRepository.AddRangeAsync(
        [
            new MediaTag
            {
                Id = Guid.NewGuid(),
                MediaId = onThisDayOsaka.Id,
                TagId = blossom.Id,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }
        ]);

        var dashboardRepository = new InMemoryDashboardRepository(media, [osaka, tokyo], [blossom]);

        var settingRepository = new FakeSettingRepository();
        var memorySearch = new MemorySearchService(
            mediaRepository,
            placeRepository,
            mediaTagRepository,
            tagRepository,
            settingRepository,
            new RuleBasedMemorySearchAnalyzer(
                placeRepository,
                tagRepository,
                NullLogger<RuleBasedMemorySearchAnalyzer>.Instance),
            new VisitRecordService(),
            NullLogger<MemorySearchService>.Instance);

        await memorySearch.SearchAsync(new MemorySearchRequest { SearchText = "오사카" });

        var service = new HomeDashboardService(
            dashboardRepository,
            mediaRepository,
            mediaTagRepository,
            tagRepository,
            storageRepository,
            new FakeFileAccessService(),
            memorySearch,
            new VisitRecordService(),
            NullLogger<HomeDashboardService>.Instance);

        var dashboard = await service.GetDashboardAsync();

        Assert.NotEmpty(dashboard.HeroMemories);
        Assert.Single(dashboard.HeroMemories);
        Assert.Equal(osaka.Id, dashboard.HeroMemories[0].PlaceId);
        Assert.Equal(5, dashboard.HeroMemories[0].YearsAgo);
        Assert.Equal(2, dashboard.HeroMemories[0].PhotoCount);
        Assert.Equal("추천 추억", dashboard.HeroMemories[0].KindLabel);
        Assert.Equal("오사카", dashboard.HeroMemories[0].Title);
        Assert.Contains("벚꽃", dashboard.HeroMemories[0].TopTags);

        Assert.NotEmpty(dashboard.TodayMemories);
        Assert.True(dashboard.TodayMemories.Count <= 5);

        Assert.NotEmpty(dashboard.RecentVisits);
        Assert.True(dashboard.RecentVisits.Count <= 5);

        Assert.Contains(dashboard.Favorites, item => item.MediaId == favorite.Id);
        Assert.Equal(imported.Id, dashboard.RecentImports[0].MediaId);

        Assert.True(dashboard.PendingSummary.Total >= 1);
        Assert.True(dashboard.PendingSummary.UnknownDate >= 1);

        Assert.Contains("오사카", dashboard.RecentQueries);
        Assert.True(dashboard.Statistics.PhotoCount >= 6);
        Assert.Equal(2, dashboard.Statistics.PlaceCount);
        Assert.True(dashboard.Statistics.FavoriteCount >= 1);
        Assert.Equal(1, dashboard.Statistics.TagCount);
    }

    [Fact]
    public async Task GetOnThisDayPhotosAsync_FiltersSameMonthDayWithinLookbackYears()
    {
        var today = DateTime.Now;
        var place = CreatePlace("오사카");
        var storageId = Guid.NewGuid();
        var match = CreatePhoto(
            "match.jpg",
            storageId,
            place,
            new DateTimeOffset(today.Year - 3, today.Month, today.Day, 8, 0, 0, TimeSpan.Zero));
        var wrongDayOffset = today.Day >= 28 ? -1 : 1;
        var wrongDay = CreatePhoto(
            "wrong-day.jpg",
            storageId,
            place,
            new DateTimeOffset(today.Year - 3, today.Month, today.Day + wrongDayOffset, 8, 0, 0, TimeSpan.Zero));
        var tooOld = CreatePhoto(
            "old.jpg",
            storageId,
            place,
            new DateTimeOffset(today.Year - 11, today.Month, today.Day, 8, 0, 0, TimeSpan.Zero));

        var repository = new InMemoryDashboardRepository([match, wrongDay, tooOld], [place]);
        var result = await repository.GetOnThisDayPhotosAsync(today.Month, today.Day, lookbackYears: 10);

        Assert.Single(result);
        Assert.Equal(match.Id, result[0].Id);
    }

    private static Place CreatePlace(string name) => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = name,
        Country = "Japan",
        City = name,
        Latitude = 34.6,
        Longitude = 135.5,
        Radius = 200,
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static Media CreatePhoto(
        string fileName,
        Guid storageId,
        Place? place,
        DateTimeOffset? capturedAt,
        bool isFavorite = false,
        MediaStatus status = MediaStatus.Imported)
    {
        return new Media
        {
            Id = Guid.NewGuid(),
            FileName = fileName,
            MediaType = MediaType.Photo,
            Status = status,
            OriginalPath = $@"D:\src\{fileName}",
            RelativePath = $@"2024\{fileName}",
            ContentHash = fileName,
            CapturedAt = capturedAt?.UtcDateTime,
            ImportedAt = capturedAt?.UtcDateTime ?? DateTime.UtcNow,
            PlaceId = place?.Id,
            Place = place,
            StorageId = storageId,
            IsFavorite = isFavorite,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow.AddMinutes(-10)
        };
    }

    private static async Task<Tag> CreateTagAsync(InMemoryTagRepository repository, string name)
    {
        var tag = new Tag
        {
            Id = Guid.NewGuid(),
            Name = name,
            Color = "#64B5F6",
            UsageCount = 1,
            Source = TagSource.User,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await repository.AddAsync(tag);
        return tag;
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
        {
            _items.RemoveAll(item => item.Id == storage.Id);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSettingRepository : ISettingRepository
    {
        private readonly List<Setting> _items = [];

        public Task<Setting?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(_items.FirstOrDefault(item => item.Id == id));

        public Task<Setting?> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(_items.FirstOrDefault(item => item.Key == key));

        public Task<IReadOnlyList<Setting>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Setting>>(_items.ToList());

        public Task AddAsync(Setting setting, CancellationToken cancellationToken = default)
        {
            _items.Add(setting);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Setting setting, CancellationToken cancellationToken = default)
        {
            var index = _items.FindIndex(item => item.Id == setting.Id || item.Key == setting.Key);
            if (index >= 0)
            {
                _items[index] = setting;
            }
            else
            {
                _items.Add(setting);
            }

            return Task.CompletedTask;
        }

        public Task DeleteAsync(Setting setting, CancellationToken cancellationToken = default)
        {
            _items.RemoveAll(item => item.Id == setting.Id);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryPlaceRepository : IPlaceRepository
    {
        private readonly List<Place> _items;

        public InMemoryPlaceRepository(IEnumerable<Place> places) => _items = places.ToList();

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
        {
            _items.RemoveAll(item => item.Id == place.Id);
            return Task.CompletedTask;
        }

    }
}

