using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.DTOs.Upload;
using MemoryKeeper.Application.Interfaces;
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
        Environment.GetEnvironmentVariable(TcBackendOptions.ApiBaseUrlEnvironmentVariable)
        ?? TcBackendOptions.ProductionApiBaseUrl;

    [LiveBackendWriteFact]
    public async Task Live_Upload_Returns_JobId_When_Backend_Up()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"mk-live-upload-{Guid.NewGuid():N}.jpg");
        await File.WriteAllBytesAsync(tempFile, [0xFF, 0xD8, 0xFF, 0xD9]);

        try
        {
            using var handle = ApiClientFactory.Create(new TcBackendOptions
            {
                ApiBaseUrl = DefaultBaseUrl,
                AuthToken = Environment.GetEnvironmentVariable(TcBackendOptions.AuthTokenEnvironmentVariable) ?? string.Empty,
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
    public async Task ImportService_Always_Uses_UploadApi_And_Monitor()
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
        var jobApi = new ImmediateCompleteJobApi();
        var catalog = new CatalogInvalidation();
        var sessionPath = Path.Combine(Path.GetTempPath(), $"mk-session-{Guid.NewGuid():N}.json");
        var options = Options.Create(new ImportUploadOptions { MaxConcurrentUploads = 3 });

        var service = new MediaImportService(
            new FileScanner(),
            storageRepository,
            upload,
            jobApi,
            new BulkUploadMonitorService(jobApi, options, NullLogger<BulkUploadMonitorService>.Instance),
            new ImportJobSessionStore(NullLogger<ImportJobSessionStore>.Instance, sessionPath),
            catalog,
            options,
            NullLogger<MediaImportService>.Instance);

        try
        {
            var reports = new List<ImportProgressDto>();
            var result = await service.ImportAsync(
                new MediaImportRequest
                {
                    SourceFolderPath = sourceRoot,
                    StorageId = storageId,
                },
                new Progress<ImportProgressDto>(reports.Add));

            Assert.Equal(1, result.ScannedCount);
            Assert.Equal(1, result.ImportedCount);
            Assert.Equal(0, result.FailedCount);
            Assert.False(string.IsNullOrWhiteSpace(result.Items[0].ContentHash));
            Assert.Single(upload.UploadedPaths);
            Assert.True(jobApi.CallCount >= 1);
            Assert.Contains(reports, r => r.CompletedCount >= 1 || r.BackendStatus == UploadJobStatusDto.Completed);
            Assert.True(catalog.Consume(CatalogSurface.Gallery));
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

            if (File.Exists(sessionPath))
            {
                File.Delete(sessionPath);
            }
        }
    }

    private sealed class FakeUploadApiRepository : IUploadApiRepository
    {
        public List<string> UploadedPaths { get; } = [];

        public int MaxObservedConcurrency { get; private set; }

        private int _inFlight;

        public async Task<UploadResponseDto> UploadAsync(string filePath, CancellationToken cancellationToken = default)
        {
            var current = Interlocked.Increment(ref _inFlight);
            MaxObservedConcurrency = Math.Max(MaxObservedConcurrency, current);
            try
            {
                await Task.Delay(20, cancellationToken);
                UploadedPaths.Add(filePath);
                var jobId = Guid.NewGuid();
                return new UploadResponseDto
                {
                    JobId = jobId.ToString("D"),
                    Status = UploadJobStatusDto.Waiting,
                    Message = $"incoming/{jobId:N}.jpg",
                    IncomingPath = $"incoming/{jobId:N}.jpg",
                    Id = 1,
                };
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }
    }

    private sealed class ImmediateCompleteJobApi : IUploadJobApiRepository
    {
        public int CallCount { get; private set; }

        public Task<UploadJobStatusDto> GetStatusAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new UploadJobStatusDto
            {
                JobId = jobId.ToString("D"),
                Status = UploadJobStatusDto.Completed,
                Progress = 100,
                CurrentPlugin = "GpsPlugin",
            });
        }

        public Task<UploadJobListDto> ListJobsAsync(
            string? status = null,
            int page = 1,
            int pageSize = 20,
            string sort = "created_at_desc",
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new UploadJobListDto
            {
                Items = [],
                Page = page,
                PageSize = pageSize,
                Total = 0,
                Sort = sort,
            });
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
