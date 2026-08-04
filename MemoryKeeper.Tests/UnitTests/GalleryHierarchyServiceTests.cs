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

public class GalleryHierarchyServiceTests
{
    [Fact]
    public async Task Hierarchy_YearCountryProvincePlace_AndUnclassified_Work()
    {
        var storageId = Guid.NewGuid();
        var mediaRepository = new InMemoryMediaRepository();
        var placeRepository = new SimplePlaceRepository();
        var storageRepository = new SimpleStorageRepository();
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

        var seoulPlace = new Place
        {
            Id = Guid.NewGuid(),
            DisplayName = "경복궁",
            Country = "대한민국",
            Province = "종로",
            City = "서울",
            Latitude = 37.5,
            Longitude = 127.0,
            Radius = 200,
            IsActive = true,
            Category = "tourist_attraction",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var busanPlace = new Place
        {
            Id = Guid.NewGuid(),
            DisplayName = "해운대",
            Country = "대한민국",
            Province = "해운대",
            City = "부산",
            Latitude = 35.1,
            Longitude = 129.1,
            Radius = 200,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await placeRepository.AddAsync(seoulPlace);
        await placeRepository.AddAsync(busanPlace);

        await mediaRepository.AddAsync(CreatePhoto(storageId, "a.jpg", seoulPlace.Id, 2026, 3, 1));
        await mediaRepository.AddAsync(CreatePhoto(storageId, "b.jpg", seoulPlace.Id, 2026, 3, 2));
        await mediaRepository.AddAsync(CreatePhoto(storageId, "c.jpg", busanPlace.Id, 2026, 4, 1));
        await mediaRepository.AddAsync(CreatePhoto(storageId, "pending.jpg", placeId: null, 2026, 5, 1));

        var service = new GalleryHierarchyService(
            mediaRepository,
            placeRepository,
            storageRepository,
            new FakeFileAccessService(),
            new NoOpPlaceDisplayNameRefreshService(),
            NullLogger<GalleryHierarchyService>.Instance);

        var years = await service.GetYearsAsync();
        Assert.Contains(years, year => year.Year == 2026 && year.Count == 4);

        var countries = await service.GetCountriesAsync(2026);
        Assert.Contains(countries, item => item.IsUnclassified && item.Count == 1);
        Assert.Contains(countries, item => item.Title == "대한민국" && item.Count == 3);

        var cities = await service.GetCitiesAsync(2026, "대한민국");
        Assert.Equal(2, cities.Count);
        Assert.Contains(cities, item => item.Title == "서울" && item.Count == 2);

        var places = await service.GetPlacesAsync(2026, "대한민국", "서울");
        Assert.Single(places);
        Assert.Equal("경복궁", places[0].Title);
        Assert.Equal(2, places[0].Count);

        var seoulPhotos = await service.QueryAsync(new GalleryHierarchyQuery
        {
            Year = 2026,
            Country = "대한민국",
            City = "서울"
        });
        Assert.Equal(2, seoulPhotos.Count);

        var unclassified = await service.QueryAsync(new GalleryHierarchyQuery
        {
            Year = 2026,
            UnclassifiedOnly = true
        });
        Assert.Single(unclassified);
        Assert.Equal("pending.jpg", unclassified[0].FileName);
    }

    [Fact]
    public async Task PlaceBrowse_RootsAndYears_SortedAsExpected()
    {
        var storageId = Guid.NewGuid();
        var mediaRepository = new InMemoryMediaRepository();
        var placeRepository = new SimplePlaceRepository();
        var storageRepository = new SimpleStorageRepository();
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

        var usj = new Place
        {
            Id = Guid.NewGuid(),
            DisplayName = "유니버설 스튜디오 재팬",
            Country = "일본",
            City = "오사카",
            Latitude = 34.6,
            Longitude = 135.4,
            Radius = 300,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var dotonbori = new Place
        {
            Id = Guid.NewGuid(),
            DisplayName = "도톤보리",
            Country = "일본",
            City = "오사카",
            Latitude = 34.66,
            Longitude = 135.5,
            Radius = 150,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await placeRepository.AddAsync(usj);
        await placeRepository.AddAsync(dotonbori);
        await mediaRepository.AddAsync(CreatePhoto(storageId, "u24.jpg", usj.Id, 2024, 1, 1));
        await mediaRepository.AddAsync(CreatePhoto(storageId, "u26a.jpg", usj.Id, 2026, 2, 1));
        await mediaRepository.AddAsync(CreatePhoto(storageId, "u26b.jpg", usj.Id, 2026, 2, 2));
        await mediaRepository.AddAsync(CreatePhoto(storageId, "d26.jpg", dotonbori.Id, 2026, 3, 1));

        var service = new GalleryHierarchyService(
            mediaRepository,
            placeRepository,
            storageRepository,
            new FakeFileAccessService(),
            new NoOpPlaceDisplayNameRefreshService(),
            NullLogger<GalleryHierarchyService>.Instance);

        var roots = await service.GetPlaceBrowseRootsAsync();
        Assert.Equal(2, roots.Count);
        Assert.Equal("도톤보리", roots[0].Title);
        Assert.Equal("유니버설 스튜디오 재팬", roots[1].Title);
        Assert.Equal(3, roots[1].Count);

        var years = await service.GetYearsForPlaceAsync(usj.Id);
        Assert.Equal(2, years.Count);
        Assert.Equal(2026, years[0].Year);
        Assert.Equal(2, years[0].Count);
        Assert.Equal(2024, years[1].Year);

        var filtered = await service.GetPlaceBrowseRootsAsync("유니버설");
        Assert.Single(filtered);
        Assert.Equal(usj.Id, filtered[0].PlaceId);

        var yearPhotos = await service.QueryAsync(new GalleryHierarchyQuery
        {
            PlaceId = usj.Id,
            Year = 2026
        });
        Assert.Equal(2, yearPhotos.Count);
    }

    [Fact]
    public async Task Hierarchy_ShowsKoreanLabels_ForNonKoreanPlaceFields()
    {
        var storageId = Guid.NewGuid();
        var mediaRepository = new InMemoryMediaRepository();
        var placeRepository = new SimplePlaceRepository();
        var storageRepository = new SimpleStorageRepository();
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

        var osaka = new Place
        {
            Id = Guid.NewGuid(),
            DisplayName = "Osaka",
            CanonicalName = "오사카",
            Country = "Japan",
            Province = "Osaka",
            City = "Osaka-shi",
            Latitude = 34.6,
            Longitude = 135.5,
            Radius = 200,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await placeRepository.AddAsync(osaka);
        await mediaRepository.AddAsync(CreatePhoto(storageId, "osaka.jpg", osaka.Id, 2026, 2, 1));

        var service = new GalleryHierarchyService(
            mediaRepository,
            placeRepository,
            storageRepository,
            new FakeFileAccessService(),
            new NoOpPlaceDisplayNameRefreshService(),
            NullLogger<GalleryHierarchyService>.Instance);

        var countries = await service.GetCountriesAsync(2026);
        Assert.Contains(countries, item => item.Title == "일본" && item.Count == 1);

        var cities = await service.GetCitiesAsync(2026, "일본");
        Assert.Contains(cities, item => item.Title == "오사카" && item.Count == 1);

        var places = await service.GetPlacesAsync(2026, "일본", "오사카");
        Assert.Single(places);
        Assert.Equal("오사카", places[0].Title);
    }

    [Fact]
    public async Task Hierarchy_UsesProvinceFallback_ForJapaneseWardCity()
    {
        var storageId = Guid.NewGuid();
        var mediaRepository = new InMemoryMediaRepository();
        var placeRepository = new SimplePlaceRepository();
        var storageRepository = new SimpleStorageRepository();
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

        var usj = new Place
        {
            Id = Guid.NewGuid(),
            DisplayName = "ユニバーサル・スタジオ・ジャパン",
            CanonicalName = "ユニバーサル・スタジオ・ジャパン",
            Country = "日本",
            Province = "大阪府",
            City = "此花区",
            Latitude = 34.6,
            Longitude = 135.4,
            Radius = 300,
            IsActive = true,
            GooglePlaceId = "ChIJ-usj",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await placeRepository.AddAsync(usj);
        await mediaRepository.AddAsync(CreatePhoto(storageId, "20251030_094121.jpg", usj.Id, 2025, 10, 30));

        var refresh = new FakePlaceDisplayNameRefreshService();
        refresh.Register(usj.Id, new LocationResult
        {
            DisplayName = "유니버설 스튜디오 재팬",
            Country = "일본",
            Province = "오사카",
            City = "오사카",
            PlaceId = "ChIJ-usj",
            Latitude = 34.6,
            Longitude = 135.4
        });

        var service = new GalleryHierarchyService(
            mediaRepository,
            placeRepository,
            storageRepository,
            new FakeFileAccessService(),
            refresh,
            NullLogger<GalleryHierarchyService>.Instance);

        var countries = await service.GetCountriesAsync(2025);
        Assert.Contains(countries, item => item.Title == "일본" && item.Count == 1);

        var cities = await service.GetCitiesAsync(2025, "일본");
        Assert.Contains(cities, item => item.Title == "오사카" && item.Count == 1);

        var places = await service.GetPlacesAsync(2025, "일본", "오사카");
        Assert.Single(places);
        Assert.Equal("유니버설 스튜디오 재팬", places[0].Title);
    }

    private sealed class NoOpPlaceDisplayNameRefreshService : IPlaceDisplayNameRefreshService
    {
        public Task<int> RefreshKoreanNamesAsync(IEnumerable<Place> places, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }

    private sealed class FakePlaceDisplayNameRefreshService : IPlaceDisplayNameRefreshService
    {
        private readonly Dictionary<Guid, LocationResult> _locations = new();

        public void Register(Guid placeId, LocationResult location) => _locations[placeId] = location;

        public Task<int> RefreshKoreanNamesAsync(IEnumerable<Place> places, CancellationToken cancellationToken = default)
        {
            var updated = 0;
            foreach (var place in places)
            {
                if (!_locations.TryGetValue(place.Id, out var location))
                {
                    continue;
                }

                var normalized = PlaceNormalizer.Normalize(location);
                place.DisplayName = normalized.DisplayName;
                place.CanonicalName = normalized.CanonicalName;
                place.Country = normalized.Country;
                place.Province = normalized.Province;
                place.City = normalized.City;
                updated++;
            }

            return Task.FromResult(updated);
        }
    }

    private static Media CreatePhoto(
        Guid storageId,
        string fileName,
        Guid? placeId,
        int year,
        int month,
        int day)
    {
        var captured = new DateTime(year, month, day, 12, 0, 0, DateTimeKind.Utc);
        return new Media
        {
            Id = Guid.NewGuid(),
            FileName = fileName,
            MediaType = MediaType.Photo,
            Status = placeId is null ? MediaStatus.Pending : MediaStatus.Imported,
            OriginalPath = $@"D:\src\{fileName}",
            RelativePath = $@"{year}\{fileName}",
            ContentHash = Guid.NewGuid().ToString("N"),
            CapturedAt = captured,
            ImportedAt = captured,
            StorageId = storageId,
            PlaceId = placeId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private sealed class SimplePlaceRepository : IPlaceRepository
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
            => Task.CompletedTask;

        public Task DeleteAsync(Place place, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class SimpleStorageRepository : IStorageRepository
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
}
