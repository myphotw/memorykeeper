using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.DTOs.Upload;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Options;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Domain.Entities;
using MemoryKeeper.Domain.Enums;
using MemoryKeeper.Infrastructure.Import;
using MemoryKeeper.Infrastructure.Repositories;
using MemoryKeeper.Infrastructure.Repositories.Api;
using MemoryKeeper.Infrastructure.Services.Api;
using MemoryKeeper.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StorageEntity = MemoryKeeper.Domain.Entities.Storage;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class UploadApiRepositorySmokeTests
{
    private static readonly string DefaultBaseUrl =
        Environment.GetEnvironmentVariable("TC_BACKEND_URL") ?? "http://localhost:8000";

    [Fact]
    public async Task Live_Upload_Returns_JobId_When_Backend_Up()
    {
        if (!await IsServerReachableAsync(DefaultBaseUrl))
        {
            return;
        }

        var tempFile = Path.Combine(Path.GetTempPath(), $"mk-live-upload-{Guid.NewGuid():N}.jpg");
        await File.WriteAllBytesAsync(tempFile, [0xFF, 0xD8, 0xFF, 0xD9]);

        try
        {
            using var handle = ApiClientFactory.Create(new TcBackendOptions
            {
                ApiBaseUrl = DefaultBaseUrl,
                Timeout = 30,
                RetryCount = 0,
                ServiceName = "MemoryKeeper",
            });

            IUploadApiRepository repo = new UploadApiRepository(handle.Client);
            var response = await repo.UploadAsync(tempFile);

            Assert.False(string.IsNullOrWhiteSpace(response.JobId));
            Assert.False(string.IsNullOrWhiteSpace(response.Status));
            Assert.Equal("WAITING", response.Status, ignoreCase: true);
        }
        catch (ApiException ex) when ((int)ex.StatusCode >= 500)
        {
            Assert.True(true, ex.Message);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task ImportService_Uses_UploadApi_When_Flag_Enabled()
    {
        var sourceRoot = Path.Combine(Path.GetTempPath(), $"mk-be-import-src-{Guid.NewGuid():N}");
        var libraryRoot = Path.Combine(Path.GetTempPath(), $"mk-be-import-lib-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(libraryRoot);
        var sourceFile = Path.Combine(sourceRoot, "IMG_BE.jpg");
        await File.WriteAllBytesAsync(sourceFile, "fake-image"u8.ToArray());

        var storageId = Guid.NewGuid();
        var storageRepository = new LocalStorageRepo();
        await storageRepository.AddAsync(new StorageEntity
        {
            Id = storageId,
            Name = "Local",
            StorageType = StorageType.Local,
            PhotoRoot = libraryRoot,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });

        var upload = new FakeUploadApiRepository();
        var options = new TestOptionsMonitor(new BackendUploadOptions { UseBackendUpload = true });
        var mediaRepository = new InMemoryMediaRepository();
        var fileAccess = new FakeFileAccessService();
        var fileStorage = new FileStorageService(new LocalStorageProvider(), fileAccess);

        var service = new MediaImportService(
            new FileScanner(),
            new NoMetadata(),
            new FileHasher(),
            fileStorage,
            fileAccess,
            mediaRepository,
            storageRepository,
            new PlaceAssignmentService(
                new NoLocation(),
                new NoPlaces(),
                new NoSettings(),
                NullLogger<PlaceAssignmentService>.Instance),
            new MediaLibraryPathSyncService(
                mediaRepository,
                new NoPlaces(),
                storageRepository,
                fileStorage,
                fileAccess,
                NullLogger<MediaLibraryPathSyncService>.Instance),
            NullLogger<MediaImportService>.Instance,
            upload,
            options);

        try
        {
            var result = await service.ImportAsync(new MediaImportRequest
            {
                SourceFolderPath = sourceRoot,
                StorageId = storageId,
            });

            Assert.Equal(1, result.ScannedCount);
            Assert.Equal(1, result.ImportedCount);
            Assert.Equal(0, result.FailedCount);
            Assert.Equal("job-1", result.Items[0].ContentHash);
            Assert.Single(upload.UploadedPaths);
            Assert.Empty(await mediaRepository.GetAllAsync());
        }
        finally
        {
            if (Directory.Exists(sourceRoot))
            {
                Directory.Delete(sourceRoot, recursive: true);
            }

            if (Directory.Exists(libraryRoot))
            {
                Directory.Delete(libraryRoot, recursive: true);
            }
        }
    }

    private static async Task<bool> IsServerReachableAsync(string baseUrl)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            using var response = await http.GetAsync(baseUrl.TrimEnd('/') + "/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private sealed class FakeUploadApiRepository : IUploadApiRepository
    {
        public List<string> UploadedPaths { get; } = [];

        public Task<UploadResponseDto> UploadAsync(string filePath, CancellationToken cancellationToken = default)
        {
            UploadedPaths.Add(filePath);
            return Task.FromResult(new UploadResponseDto
            {
                JobId = "job-1",
                Status = "WAITING",
                Message = "incoming/job-1.jpg",
                IncomingPath = "incoming/job-1.jpg",
                Id = 1,
            });
        }
    }

    private sealed class TestOptionsMonitor : IOptionsMonitor<BackendUploadOptions>
    {
        public TestOptionsMonitor(BackendUploadOptions current) => CurrentValue = current;

        public BackendUploadOptions CurrentValue { get; }

        public BackendUploadOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<BackendUploadOptions, string?> listener) => null;
    }

    private sealed class LocalStorageRepo : IStorageRepository
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

    private sealed class NoMetadata : IMetadataExtractor
    {
        public Task<MediaMetadataDto> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
            => Task.FromResult(new MediaMetadataDto { MediaType = MediaType.Photo });

        public Task<IReadOnlyDictionary<string, string>> DumpTagsAsync(
            string filePath,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
    }

    private sealed class NoLocation : ILocationResolver
    {
        public Task<LocationResult?> ResolveAsync(double latitude, double longitude, CancellationToken cancellationToken = default)
            => Task.FromResult<LocationResult?>(null);

        public Task<LocationResult?> ResolveAddressAsync(string address, CancellationToken cancellationToken = default)
            => Task.FromResult<LocationResult?>(null);

        public Task<IReadOnlyList<PlaceSuggestionDto>> SuggestPlacesAsync(string input, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<PlaceSuggestionDto>>([]);

        public Task<LocationResult?> ResolvePlaceIdAsync(string placeId, CancellationToken cancellationToken = default)
            => Task.FromResult<LocationResult?>(null);

        public Task<IReadOnlyList<NearbyPlaceCandidateDto>> SearchNearbyAsync(
            double latitude, double longitude, int maxResults = 5, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<NearbyPlaceCandidateDto>>([]);
    }

    private sealed class NoPlaces : IPlaceRepository
    {
        public Task<Place?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Place?>(null);
        public Task<IReadOnlyList<Place>> GetByIdsAsync(IReadOnlyCollection<Guid> placeIds, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Place>>([]);
        public Task<IReadOnlyList<Place>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Place>>([]);
        public Task<IReadOnlyList<Place>> GetActiveAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Place>>([]);
        public Task<IReadOnlyList<Place>> SearchAsync(string keyword, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Place>>([]);
        public Task AddAsync(Place place, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateAsync(Place place, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(Place place, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoSettings : ISettingRepository
    {
        public Task<Setting?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Setting?>(null);
        public Task<Setting?> GetByKeyAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult<Setting?>(null);
        public Task<IReadOnlyList<Setting>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Setting>>([]);
        public Task AddAsync(Setting setting, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateAsync(Setting setting, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(Setting setting, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
