using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Domain.Entities;
using MemoryKeeper.Domain.Enums;
using MemoryKeeper.Infrastructure.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

namespace MemoryKeeper.Tests.UnitTests;

public class MemorySearchServiceTests
{
    [Fact]
    public async Task SearchAsync_SupportsYearPlaceAndKeywordScenarios()
    {
        var placeRepository = new InMemoryPlaceRepository();
        var mediaRepository = new InMemoryMediaRepository();
        var tagRepository = new InMemoryTagRepository();
        var settingRepository = new FakeSettingRepository();

        var osaka = await CreatePlaceAsync(placeRepository, "오사카", "일본", "오사카");
        var tokyo = await CreatePlaceAsync(placeRepository, "도쿄", "일본", "도쿄");

        await AddMediaAsync(mediaRepository, osaka.Id, new DateTimeOffset(2025, 5, 1, 10, 0, 0, TimeSpan.Zero), "o1");
        await AddMediaAsync(mediaRepository, osaka.Id, new DateTimeOffset(2025, 5, 1, 12, 0, 0, TimeSpan.Zero), "o2");
        await AddMediaAsync(mediaRepository, osaka.Id, new DateTimeOffset(2025, 5, 2, 9, 0, 0, TimeSpan.Zero), "o3");
        await AddMediaAsync(mediaRepository, osaka.Id, new DateTimeOffset(2024, 8, 1, 9, 0, 0, TimeSpan.Zero), "o4");
        await AddMediaAsync(mediaRepository, tokyo.Id, new DateTimeOffset(2025, 6, 1, 9, 0, 0, TimeSpan.Zero), "t1");

        var service = CreateService(mediaRepository, placeRepository, tagRepository, settingRepository);

        var byYear = await service.SearchAsync(new MemorySearchRequest { Year = 2025 });
        Assert.Equal(2, byYear.Items.Count);
        var osaka2025 = byYear.Items.Single(item => item.PlaceId == osaka.Id);
        Assert.Equal(3, osaka2025.PhotoCount);
        Assert.Equal(2, osaka2025.VisitRecordCount);

        var byPlace = await service.SearchAsync(new MemorySearchRequest { PlaceId = osaka.Id });
        Assert.Single(byPlace.Items);
        Assert.Equal(4, byPlace.Items[0].PhotoCount);
        Assert.Equal(3, byPlace.Items[0].VisitRecordCount);

        var byYearAndKeyword = await service.SearchAsync(new MemorySearchRequest
        {
            Year = 2025,
            Keyword = "오사카"
        });
        Assert.Single(byYearAndKeyword.Items);
        Assert.Equal("오사카", byYearAndKeyword.Items[0].PlaceName);
        Assert.Equal(3, byYearAndKeyword.Items[0].PhotoCount);
        Assert.Equal(2, byYearAndKeyword.Items[0].VisitRecordCount);
        Assert.Equal(new DateTime(2025, 5, 1), byYearAndKeyword.Items[0].FirstCapturedDate!.Value.Date);
        Assert.Equal(new DateTime(2025, 5, 2), byYearAndKeyword.Items[0].LastCapturedDate!.Value.Date);
    }

    [Fact]
    public async Task SearchText_AnalyzesPlaceTagYearFavorite_AndBuildsChips()
    {
        var placeRepository = new InMemoryPlaceRepository();
        var mediaRepository = new InMemoryMediaRepository();
        var tagRepository = new InMemoryTagRepository();
        var mediaTagRepository = new InMemoryMediaTagRepository();
        var settingRepository = new FakeSettingRepository();

        var osaka = await CreatePlaceAsync(placeRepository, "오사카", "일본", "오사카");
        var food = await CreateTagAsync(tagRepository, "음식");
        var blossom = await CreateTagAsync(tagRepository, "벚꽃");

        var mediaId = Guid.NewGuid();
        await mediaRepository.AddAsync(new Media
        {
            Id = mediaId,
            FileName = "food.jpg",
            MediaType = MediaType.Photo,
            Status = MediaStatus.Imported,
            OriginalPath = @"D:\food.jpg",
            RelativePath = @"2024\food.jpg",
            ContentHash = "food",
            CapturedAt = new DateTimeOffset(2024, 4, 1, 10, 0, 0, TimeSpan.Zero).UtcDateTime,
            ImportedAt = new DateTimeOffset(2024, 4, 1, 10, 0, 0, TimeSpan.Zero).UtcDateTime,
            PlaceId = osaka.Id,
            StorageId = Guid.NewGuid(),
            IsFavorite = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await mediaTagRepository.AddRangeAsync(
        [
            new MediaTag
            {
                Id = Guid.NewGuid(),
                MediaId = mediaId,
                TagId = food.Id,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new MediaTag
            {
                Id = Guid.NewGuid(),
                MediaId = mediaId,
                TagId = blossom.Id,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }
        ]);

        var service = CreateService(
            mediaRepository,
            placeRepository,
            tagRepository,
            settingRepository,
            mediaTagRepository);

        var result = await service.SearchAsync(new MemorySearchRequest
        {
            SearchText = "오사카 음식"
        });

        Assert.Single(result.Items);
        Assert.Equal(osaka.Id, result.Items[0].PlaceId);
        Assert.Contains(result.Chips, chip => chip.Kind == MemorySearchChipKind.Place && chip.Label == "오사카");
        Assert.Contains(result.Chips, chip => chip.Kind == MemorySearchChipKind.Tag && chip.Label == "음식");

        var favorite = await service.SearchAsync(new MemorySearchRequest
        {
            SearchText = "즐겨찾기 오사카"
        });
        Assert.Single(favorite.Items);
        Assert.Contains(favorite.Chips, chip => chip.Kind == MemorySearchChipKind.Favorite);

        var lastYear = DateTime.Now.Year - 1;
        var blossomMediaId = Guid.NewGuid();
        await mediaRepository.AddAsync(new Media
        {
            Id = blossomMediaId,
            FileName = "blossom.jpg",
            MediaType = MediaType.Photo,
            Status = MediaStatus.Imported,
            OriginalPath = @"D:\blossom.jpg",
            RelativePath = @$"{lastYear}\blossom.jpg",
            ContentHash = "blossom",
            CapturedAt = new DateTimeOffset(lastYear, 3, 1, 10, 0, 0, TimeSpan.Zero).UtcDateTime,
            ImportedAt = new DateTimeOffset(lastYear, 3, 1, 10, 0, 0, TimeSpan.Zero).UtcDateTime,
            PlaceId = osaka.Id,
            StorageId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await mediaTagRepository.AddRangeAsync(
        [
            new MediaTag
            {
                Id = Guid.NewGuid(),
                MediaId = blossomMediaId,
                TagId = blossom.Id,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }
        ]);

        var yearSearch = await service.SearchAsync(new MemorySearchRequest
        {
            SearchText = "작년 벚꽃"
        });
        Assert.Equal(lastYear, yearSearch.ResolvedRequest.Year);
        Assert.Single(yearSearch.Items);
        Assert.Contains(yearSearch.Chips, chip => chip.Kind == MemorySearchChipKind.Year && chip.Label == lastYear.ToString());
        Assert.Contains(yearSearch.Chips, chip => chip.Kind == MemorySearchChipKind.Tag && chip.Label == "벚꽃");

        var recent = await service.GetRecentQueriesAsync();
        Assert.Contains(recent, query => query == "작년 벚꽃");
        Assert.True(recent.Count <= 10);

        var suggestions = await service.SuggestAsync("벚");
        Assert.Contains(suggestions, item => item.Text == "벚꽃" && item.Kind == MemorySearchSuggestionKind.Tag);
    }

    [Fact]
    public void RuleBasedAnalyzer_ParsesRelativeYears()
    {
        Assert.True(RuleBasedMemorySearchAnalyzer.TryParseYear("올해", out var thisYear, out _));
        Assert.Equal(DateTime.Now.Year, thisYear);
        Assert.True(RuleBasedMemorySearchAnalyzer.TryParseYear("작년", out var lastYear, out _));
        Assert.Equal(DateTime.Now.Year - 1, lastYear);
        Assert.True(RuleBasedMemorySearchAnalyzer.TryParseYear("재작년", out var twoYears, out _));
        Assert.Equal(DateTime.Now.Year - 2, twoYears);
        Assert.True(RuleBasedMemorySearchAnalyzer.TryParseYear("2024", out var y2024, out var label));
        Assert.Equal(2024, y2024);
        Assert.Equal("2024", label);
    }

    private static MemorySearchService CreateService(
        InMemoryMediaRepository mediaRepository,
        InMemoryPlaceRepository placeRepository,
        InMemoryTagRepository tagRepository,
        FakeSettingRepository settingRepository,
        InMemoryMediaTagRepository? mediaTagRepository = null)
    {
        mediaTagRepository ??= new InMemoryMediaTagRepository();
        var analyzer = new RuleBasedMemorySearchAnalyzer(
            placeRepository,
            tagRepository,
            NullLogger<RuleBasedMemorySearchAnalyzer>.Instance);

        return new MemorySearchService(
            mediaRepository,
            placeRepository,
            mediaTagRepository,
            tagRepository,
            settingRepository,
            analyzer,
            new VisitRecordService(),
            NullLogger<MemorySearchService>.Instance);
    }

    private static async Task<Place> CreatePlaceAsync(
        InMemoryPlaceRepository repository,
        string displayName,
        string country,
        string city)
    {
        var place = new Place
        {
            Id = Guid.NewGuid(),
            DisplayName = displayName,
            Country = country,
            City = city,
            Latitude = 1,
            Longitude = 1,
            Radius = 200,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await repository.AddAsync(place);
        return place;
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

    private static Task AddMediaAsync(
        InMemoryMediaRepository repository,
        Guid placeId,
        DateTimeOffset capturedAt,
        string hash)
    {
        return repository.AddAsync(new Media
        {
            Id = Guid.NewGuid(),
            FileName = $"{hash}.jpg",
            MediaType = MediaType.Photo,
            Status = MediaStatus.Imported,
            OriginalPath = $@"D:\{hash}.jpg",
            RelativePath = $@"2025\{hash}.jpg",
            ContentHash = hash,
            CapturedAt = capturedAt.UtcDateTime,
            ImportedAt = capturedAt.UtcDateTime,
            PlaceId = placeId,
            StorageId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
    }

    private sealed class InMemoryPlaceRepository : IPlaceRepository
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

            return Task.CompletedTask;
        }

        public Task DeleteAsync(Setting setting, CancellationToken cancellationToken = default)
        {
            _items.RemoveAll(item => item.Id == setting.Id);
            return Task.CompletedTask;
        }
    }
}
