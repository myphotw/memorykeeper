using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Domain.Entities;
using MemoryKeeper.Domain.Enums;
using MemoryKeeper.Infrastructure.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using StorageEntity = MemoryKeeper.Domain.Entities.Storage;

namespace MemoryKeeper.Tests.UnitTests;

public class PhotoDetailServiceTests
{
    [Fact]
    public async Task ToggleFavoriteAndRelatedPhotos_WorkAsExpected()
    {
        var storageId = Guid.NewGuid();
        var placeId = Guid.NewGuid();
        var mediaRepository = new InMemoryMediaRepository();
        var placeRepository = new FakePlaceRepository();
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

        var place = new Place
        {
            Id = placeId,
            DisplayName = "Osaka",
            Country = "Japan",
            City = "Osaka",
            Address = "Castle",
            Latitude = 34.6,
            Longitude = 135.5,
            Radius = 200,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await placeRepository.AddAsync(place);

        var main = CreateMedia("main.jpg", storageId, placeId, isFavorite: false);
        var relatedFavorite = CreateMedia("fav.jpg", storageId, placeId, isFavorite: true);
        var relatedOther = CreateMedia("other.jpg", storageId, placeId, isFavorite: false);
        await mediaRepository.AddAsync(main);
        await mediaRepository.AddAsync(relatedFavorite);
        await mediaRepository.AddAsync(relatedOther);

        var tagService = new TagService(
            new InMemoryTagRepository(),
            new InMemoryMediaTagRepository(),
            mediaRepository,
            storageRepository,
            new EmptySettingRepository(),
            new FakeFileAccessService(),
            NullLogger<TagService>.Instance);
        var service = new PhotoDetailService(
            mediaRepository,
            placeRepository,
            storageRepository,
            new FakeFileAccessService(),
            new NoOpMediaLibraryPathSyncService(),
            tagService,
            new CatalogInvalidation(),
            NullLogger<PhotoDetailService>.Instance);

        var detail = await service.GetPhotoDetailAsync(main.Id);
        Assert.Equal("오사카", detail.PlaceName);
        Assert.Equal(2, detail.RelatedPhotos.Count);
        Assert.Equal("fav.jpg", detail.RelatedPhotos[0].FileName);

        var favorite = await service.ToggleFavoriteAsync(main.Id);
        Assert.True(favorite);

        var updated = await service.GetPhotoDetailAsync(main.Id);
        Assert.True(updated.IsFavorite);
    }

    [Fact]
    public async Task UpdateMemo_PersistsMemoOnMedia()
    {
        var storageId = Guid.NewGuid();
        var mediaRepository = new InMemoryMediaRepository();
        var placeRepository = new FakePlaceRepository();
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

        var media = CreateMedia("memo.jpg", storageId, placeId: null, isFavorite: false);
        media.Latitude = 35.0;
        media.Longitude = 135.0;
        await mediaRepository.AddAsync(media);

        var tagService = new TagService(
            new InMemoryTagRepository(),
            new InMemoryMediaTagRepository(),
            mediaRepository,
            storageRepository,
            new EmptySettingRepository(),
            new FakeFileAccessService(),
            NullLogger<TagService>.Instance);
        var service = new PhotoDetailService(
            mediaRepository,
            placeRepository,
            storageRepository,
            new FakeFileAccessService(),
            new NoOpMediaLibraryPathSyncService(),
            tagService,
            new CatalogInvalidation(),
            NullLogger<PhotoDetailService>.Instance);

        var detail = await service.UpdateMemoAsync(media.Id, "바닷가 일몰");
        Assert.Equal("바닷가 일몰", detail.Memo);
        Assert.True(detail.HasGps);

        var stored = await mediaRepository.GetByIdAsync(media.Id);
        Assert.Equal("바닷가 일몰", stored!.Memo);
    }

    [Fact]
    public async Task DeleteFromLibrary_RemovesMediaRecordOnly()
    {
        var storageId = Guid.NewGuid();
        var mediaRepository = new InMemoryMediaRepository();
        var placeRepository = new FakePlaceRepository();
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

        var media = CreateMedia("delete-me.jpg", storageId, placeId: null, isFavorite: false);
        await mediaRepository.AddAsync(media);

        var tagService = new TagService(
            new InMemoryTagRepository(),
            new InMemoryMediaTagRepository(),
            mediaRepository,
            storageRepository,
            new EmptySettingRepository(),
            new FakeFileAccessService(),
            NullLogger<TagService>.Instance);
        var service = new PhotoDetailService(
            mediaRepository,
            placeRepository,
            storageRepository,
            new FakeFileAccessService(),
            new NoOpMediaLibraryPathSyncService(),
            tagService,
            new CatalogInvalidation(),
            NullLogger<PhotoDetailService>.Instance);

        await service.DeleteFromLibraryAsync(media.Id);
        Assert.Null(await mediaRepository.GetByIdAsync(media.Id));
    }

    [Fact]
    public async Task UpdatePlace_InheritsGpsAndInvalidatesCatalog()
    {
        var storageId = Guid.NewGuid();
        var placeId = Guid.NewGuid();
        var mediaRepository = new InMemoryMediaRepository();
        var placeRepository = new FakePlaceRepository();
        var storageRepository = new FakeStorageRepository();
        var catalog = new CatalogInvalidation();

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
        await placeRepository.AddAsync(new Place
        {
            Id = placeId,
            DisplayName = "교토",
            Country = "일본",
            City = "교토",
            Latitude = 35.0116,
            Longitude = 135.7681,
            Radius = 100,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        var media = CreateMedia("no-gps.jpg", storageId, placeId: null, isFavorite: false);
        await mediaRepository.AddAsync(media);

        var tagService = new TagService(
            new InMemoryTagRepository(),
            new InMemoryMediaTagRepository(),
            mediaRepository,
            storageRepository,
            new EmptySettingRepository(),
            new FakeFileAccessService(),
            NullLogger<TagService>.Instance);
        var service = new PhotoDetailService(
            mediaRepository,
            placeRepository,
            storageRepository,
            new FakeFileAccessService(),
            new NoOpMediaLibraryPathSyncService(),
            tagService,
            catalog,
            NullLogger<PhotoDetailService>.Instance);

        var detail = await service.UpdatePlaceAsync(media.Id, placeId);

        Assert.Equal(placeId, detail.PlaceId);
        Assert.Equal(35.0116, detail.Latitude);
        Assert.Equal(135.7681, detail.Longitude);
        Assert.True(catalog.Consume(CatalogSurface.Visits));
        Assert.True(catalog.Consume(CatalogSurface.Pending));
    }

    private static Media CreateMedia(string fileName, Guid storageId, Guid? placeId, bool isFavorite)
    {
        return new Media
        {
            Id = Guid.NewGuid(),
            FileName = fileName,
            MediaType = MediaType.Photo,
            Status = placeId is null ? MediaStatus.Pending : MediaStatus.Imported,
            OriginalPath = $@"D:\src\{fileName}",
            RelativePath = $@"2026\{fileName}",
            ContentHash = Guid.NewGuid().ToString("N"),
            CapturedAt = DateTime.UtcNow,
            ImportedAt = DateTime.UtcNow,
            PlaceId = placeId,
            StorageId = storageId,
            IsFavorite = isFavorite,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
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
