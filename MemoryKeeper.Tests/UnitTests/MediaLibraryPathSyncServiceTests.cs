using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Domain.Entities;
using MemoryKeeper.Domain.Enums;
using MemoryKeeper.Infrastructure.Import;
using MemoryKeeper.Infrastructure.Repositories;
using MemoryKeeper.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using StorageEntity = MemoryKeeper.Domain.Entities.Storage;

namespace MemoryKeeper.Tests.UnitTests;

public class MediaLibraryPathSyncServiceTests
{
    [Fact]
    public async Task SyncMediaPathAsync_MovesFromPendingFolderToYearPlaceFolder()
    {
        var libraryRoot = Path.Combine(Path.GetTempPath(), $"mk-path-sync-{Guid.NewGuid():N}");
        Directory.CreateDirectory(libraryRoot);

        try
        {
            var storageId = Guid.NewGuid();
            var storageRepository = new InMemoryStorageRepository();
            await storageRepository.AddAsync(new StorageEntity
            {
                Id = storageId,
                Name = "Local",
                StorageType = StorageType.Local,
                PhotoRoot = libraryRoot,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            var mediaRepository = new InMemoryMediaRepository();
            var placeRepository = new SimplePlaceRepository();
            var place = new Place
            {
                Id = Guid.NewGuid(),
                DisplayName = "Osaka Castle",
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

            var pendingRelative = "미완성 추억/IMG001.jpg";
            var pendingAbsolute = Path.Combine(libraryRoot, "미완성 추억");
            Directory.CreateDirectory(pendingAbsolute);
            var sourceFile = Path.Combine(pendingAbsolute, "IMG001.jpg");
            await File.WriteAllBytesAsync(sourceFile, "photo-bytes"u8.ToArray());

            var media = new Media
            {
                Id = Guid.NewGuid(),
                FileName = "IMG001.jpg",
                MediaType = MediaType.Photo,
                Status = MediaStatus.Imported,
                OriginalPath = sourceFile,
                RelativePath = pendingRelative,
                ContentHash = "hash1",
                CapturedAt = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc),
                ImportedAt = DateTime.UtcNow,
                StorageId = storageId,
                PlaceId = place.Id,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await mediaRepository.AddAsync(media);

            var fileAccess = new FakeFileAccessService();
            var fileStorage = new FileStorageService(new LocalStorageProvider(), fileAccess);
            var sync = new MediaLibraryPathSyncService(
                mediaRepository,
                placeRepository,
                storageRepository,
                fileStorage,
                fileAccess,
                NullLogger<MediaLibraryPathSyncService>.Instance);

            var moved = await sync.SyncMediaPathAsync(media, place);
            Assert.True(moved);
            Assert.Equal("2026/Osaka Castle/IMG001.jpg", media.RelativePath.Replace('\\', '/'));
            Assert.False(File.Exists(sourceFile));
            Assert.True(File.Exists(fileAccess.ResolveAbsolutePath(libraryRoot, media.RelativePath)));

            var again = await sync.SyncMediaPathAsync(media, place);
            Assert.False(again);
            Assert.False(Directory.Exists(pendingAbsolute), "Empty pending folder should be deleted after move.");
        }
        finally
        {
            if (Directory.Exists(libraryRoot))
            {
                Directory.Delete(libraryRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SyncMediaPathAsync_PlaceChange_MovesAndRemovesOldFolder()
    {
        var libraryRoot = Path.Combine(Path.GetTempPath(), $"mk-path-move-{Guid.NewGuid():N}");
        Directory.CreateDirectory(libraryRoot);

        try
        {
            var storageId = Guid.NewGuid();
            var storageRepository = new InMemoryStorageRepository();
            await storageRepository.AddAsync(new StorageEntity
            {
                Id = storageId,
                Name = "Local",
                StorageType = StorageType.Local,
                PhotoRoot = libraryRoot,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            var mediaRepository = new InMemoryMediaRepository();
            var placeRepository = new SimplePlaceRepository();
            var gangnam = new Place
            {
                Id = Guid.NewGuid(),
                DisplayName = "강남",
                Country = "대한민국",
                City = "서울",
                Latitude = 37.5,
                Longitude = 127.0,
                Radius = 200,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await placeRepository.AddAsync(gangnam);

            var oldDir = Path.Combine(libraryRoot, "2026", "구로");
            Directory.CreateDirectory(oldDir);
            var sourceFile = Path.Combine(oldDir, "IMG001.jpg");
            await File.WriteAllBytesAsync(sourceFile, "photo-bytes"u8.ToArray());

            var media = new Media
            {
                Id = Guid.NewGuid(),
                FileName = "IMG001.jpg",
                MediaType = MediaType.Photo,
                Status = MediaStatus.Imported,
                OriginalPath = sourceFile,
                RelativePath = "2026/구로/IMG001.jpg",
                ContentHash = "hash-guro",
                CapturedAt = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc),
                ImportedAt = DateTime.UtcNow,
                StorageId = storageId,
                PlaceId = gangnam.Id,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await mediaRepository.AddAsync(media);

            var fileAccess = new FakeFileAccessService();
            var fileStorage = new FileStorageService(new LocalStorageProvider(), fileAccess);
            var sync = new MediaLibraryPathSyncService(
                mediaRepository,
                placeRepository,
                storageRepository,
                fileStorage,
                fileAccess,
                NullLogger<MediaLibraryPathSyncService>.Instance);

            var moved = await sync.SyncMediaPathAsync(media, gangnam);
            Assert.True(moved);
            Assert.Equal("2026/강남/IMG001.jpg", media.RelativePath.Replace('\\', '/'));
            Assert.False(File.Exists(sourceFile));
            Assert.False(Directory.Exists(oldDir));
            Assert.True(File.Exists(fileAccess.ResolveAbsolutePath(libraryRoot, media.RelativePath)));
        }
        finally
        {
            if (Directory.Exists(libraryRoot))
            {
                Directory.Delete(libraryRoot, recursive: true);
            }
        }
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

    private sealed class InMemoryStorageRepository : IStorageRepository
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
