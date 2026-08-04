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

public class LibraryCopyIntegrityServiceTests
{
    [Fact]
    public async Task InspectAndRepair_DeletesDuplicateCopiesForSameHash()
    {
        var libraryRoot = Path.Combine(Path.GetTempPath(), $"mk-integrity-{Guid.NewGuid():N}");
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

            var bytes = "same-photo-bytes"u8.ToArray();
            var keepDir = Path.Combine(libraryRoot, "2026", "강남");
            var orphanDir = Path.Combine(libraryRoot, "2026", "구로");
            Directory.CreateDirectory(keepDir);
            Directory.CreateDirectory(orphanDir);
            var keepFile = Path.Combine(keepDir, "IMG001.jpg");
            var orphanFile = Path.Combine(orphanDir, "IMG001.jpg");
            await File.WriteAllBytesAsync(keepFile, bytes);
            await File.WriteAllBytesAsync(orphanFile, bytes);

            var hasher = new FileHasher();
            var hash = await hasher.ComputeSha256Async(keepFile);

            var mediaRepository = new InMemoryMediaRepository();
            await mediaRepository.AddAsync(new Media
            {
                Id = Guid.NewGuid(),
                FileName = "IMG001.jpg",
                MediaType = MediaType.Photo,
                Status = MediaStatus.Imported,
                OriginalPath = keepFile,
                RelativePath = "2026/강남/IMG001.jpg",
                ContentHash = hash,
                CapturedAt = DateTime.UtcNow,
                ImportedAt = DateTime.UtcNow,
                StorageId = storageId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            var fileAccess = new FakeFileAccessService();
            var fileStorage = new FileStorageService(new LocalStorageProvider(), fileAccess);
            var service = new LibraryCopyIntegrityService(
                mediaRepository,
                storageRepository,
                fileAccess,
                fileStorage,
                hasher,
                NullLogger<LibraryCopyIntegrityService>.Instance);

            var result = await service.InspectAndRepairAsync();

            Assert.True(result.Succeeded);
            Assert.Equal(1, result.DuplicateFileGroups);
            Assert.Equal(1, result.DeletedDuplicateFiles);
            Assert.True(File.Exists(keepFile));
            Assert.False(File.Exists(orphanFile));
            Assert.False(Directory.Exists(orphanDir));
        }
        finally
        {
            if (Directory.Exists(libraryRoot))
            {
                Directory.Delete(libraryRoot, recursive: true);
            }
        }
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
