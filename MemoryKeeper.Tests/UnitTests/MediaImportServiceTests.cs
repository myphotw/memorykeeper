using MemoryKeeper.Application.DTOs;
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

public class MediaImportServiceTests
{
    [Fact]
    public async Task ImportAsync_ImportsNewFileAndDetectsDuplicate()
    {
        var sourceRoot = Path.Combine(Path.GetTempPath(), $"mk-import-src-{Guid.NewGuid():N}");
        var libraryRoot = Path.Combine(Path.GetTempPath(), $"mk-import-lib-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(libraryRoot);

        var sourceFile = Path.Combine(sourceRoot, "IMG001.jpg");
        await File.WriteAllBytesAsync(sourceFile, "fake-image-bytes"u8.ToArray());

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
        var fileAccess = new FakeFileAccessService();
        var fileStorage = new FileStorageService(new LocalStorageProvider(), fileAccess);
        var service = new MediaImportService(
            new FileScanner(),
            new StubMetadataExtractor(),
            new FileHasher(),
            fileStorage,
            fileAccess,
            mediaRepository,
            storageRepository,
            CreatePlaceAssignmentService(),
            new MediaLibraryPathSyncService(
                mediaRepository,
                new EmptyPlaceRepository(),
                storageRepository,
                fileStorage,
                fileAccess,
                NullLogger<MediaLibraryPathSyncService>.Instance),
            NullLogger<MediaImportService>.Instance);

        try
        {
            var first = await service.ImportAsync(new MediaImportRequest
            {
                SourceFolderPath = sourceRoot,
                StorageId = storageId
            });

            Assert.Equal(1, first.ScannedCount);
            Assert.Equal(1, first.ImportedCount);
            Assert.Equal(0, first.DuplicateCount);
            Assert.Equal(MediaStatus.Pending, first.Items[0].Status);

            var second = await service.ImportAsync(new MediaImportRequest
            {
                SourceFolderPath = sourceRoot,
                StorageId = storageId
            });

            Assert.Equal(1, second.DuplicateCount);
            Assert.Equal(MediaStatus.Duplicate, second.Items[0].Status);

            var allMedia = await mediaRepository.GetAllAsync();
            Assert.Single(allMedia);
            Assert.True(File.Exists(sourceFile), "Original file must remain.");
            Assert.True(File.Exists(fileAccess.ResolveAbsolutePath(libraryRoot, allMedia[0].RelativePath)));
            Assert.StartsWith("미완성 추억/", allMedia[0].RelativePath.Replace('\\', '/'));

            // MK-042P: re-register many times → still a single library copy.
            for (var i = 0; i < 4; i++)
            {
                await service.ImportAsync(new MediaImportRequest
                {
                    SourceFolderPath = sourceRoot,
                    StorageId = storageId
                });
            }

            var libraryFiles = Directory.GetFiles(libraryRoot, "*", SearchOption.AllDirectories);
            Assert.Single(libraryFiles);
            Assert.Single(await mediaRepository.GetAllAsync());
        }
        finally
        {
            Directory.Delete(sourceRoot, recursive: true);
            Directory.Delete(libraryRoot, recursive: true);
        }
    }

    private static PlaceAssignmentService CreatePlaceAssignmentService()
    {
        return new PlaceAssignmentService(
            new NullLocationResolver(),
            new EmptyPlaceRepository(),
            new EmptySettingRepository(),
            NullLogger<PlaceAssignmentService>.Instance);
    }

    private sealed class NullLocationResolver : ILocationResolver
    {
        public Task<LocationResult?> ResolveAsync(double latitude, double longitude, CancellationToken cancellationToken = default)
            => Task.FromResult<LocationResult?>(null);

        public Task<LocationResult?> ResolveAddressAsync(string address, CancellationToken cancellationToken = default)
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

    private sealed class EmptyPlaceRepository : IPlaceRepository
    {
        public Task<Place?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<Place?>(null);

        public Task<IReadOnlyList<Place>> GetByIdsAsync(
            IReadOnlyCollection<Guid> placeIds,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Place>>([]);

        public Task<IReadOnlyList<Place>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Place>>([]);

        public Task<IReadOnlyList<Place>> GetActiveAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Place>>([]);

        public Task<IReadOnlyList<Place>> SearchAsync(string keyword, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Place>>([]);

        public Task AddAsync(Place place, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task UpdateAsync(Place place, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteAsync(Place place, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
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

    private sealed class StubMetadataExtractor : IMetadataExtractor
    {
        public Task<MediaMetadataDto> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new MediaMetadataDto
            {
                MediaType = MediaType.Photo,
                CapturedAt = new DateTimeOffset(2025, 7, 1, 12, 0, 0, TimeSpan.Zero)
            });
        }

        public Task<IReadOnlyDictionary<string, string>> DumpTagsAsync(
            string filePath,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<string, string>>(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
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
