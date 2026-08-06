using System.Collections.Concurrent;
using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.DTOs.Upload;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Domain.Entities;
using MemoryKeeper.Domain.Enums;
using MemoryKeeper.Infrastructure.Import;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StorageEntity = MemoryKeeper.Domain.Entities.Storage;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class ParallelImportPhase3BTests
{
    [Fact]
    public async Task Parallel_Import_Caps_Concurrent_Uploads_At_Three()
    {
        var sourceRoot = Path.Combine(Path.GetTempPath(), $"mk-p3b-src-{Guid.NewGuid():N}");
        var libraryRoot = Path.Combine(Path.GetTempPath(), $"mk-p3b-lib-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(libraryRoot);

        for (var i = 0; i < 10; i++)
        {
            await File.WriteAllBytesAsync(
                Path.Combine(sourceRoot, $"IMG_{i:00}.jpg"),
                "fake-image"u8.ToArray());
        }

        var storageId = Guid.NewGuid();
        var storageRepository = new MemStorageRepo();
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

        var upload = new SlowUploadApi();
        var jobApi = new CompleteJobApi();
        var sessionPath = Path.Combine(Path.GetTempPath(), $"mk-p3b-session-{Guid.NewGuid():N}.json");
        var options = Options.Create(new ImportUploadOptions { MaxConcurrentUploads = 3 });
        var catalog = new CatalogInvalidation();

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
                new MediaImportRequest { SourceFolderPath = sourceRoot, StorageId = storageId },
                new Progress<ImportProgressDto>(reports.Add));

            Assert.Equal(10, result.ScannedCount);
            Assert.Equal(10, result.ImportedCount);
            Assert.Equal(0, result.FailedCount);
            Assert.True(upload.MaxObservedConcurrency <= 3);
            Assert.True(upload.MaxObservedConcurrency >= 2);
            Assert.Contains(reports, r => r.UploadFinishedCount == 10);
            Assert.Contains(reports, r => r.AnalysisFinishedCount == 10);
            Assert.True(BulkUploadMonitorService.IsDuplicateCompleted(new UploadJobStatusDto
            {
                Status = UploadJobStatusDto.Completed,
                ProcessingLog = "DUPLICATE_FOUND\nCOMPLETED",
            }));
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

    [Fact]
    public async Task BulkMonitor_Tracks_Multiple_Jobs()
    {
        var jobA = Guid.NewGuid();
        var jobB = Guid.NewGuid();
        var repo = new DictionaryJobApi(new Dictionary<Guid, Queue<UploadJobStatusDto>>
        {
            [jobA] = new Queue<UploadJobStatusDto>([
                new UploadJobStatusDto { JobId = jobA.ToString("D"), Status = UploadJobStatusDto.Waiting, Progress = 0 },
                new UploadJobStatusDto { JobId = jobA.ToString("D"), Status = UploadJobStatusDto.Completed, Progress = 100 },
            ]),
            [jobB] = new Queue<UploadJobStatusDto>([
                new UploadJobStatusDto { JobId = jobB.ToString("D"), Status = UploadJobStatusDto.Processing, Progress = 50 },
                new UploadJobStatusDto
                {
                    JobId = jobB.ToString("D"),
                    Status = UploadJobStatusDto.Completed,
                    Progress = 100,
                    ProcessingLog = "DUPLICATE_FOUND",
                },
            ]),
        });

        var options = Options.Create(new ImportUploadOptions());
        var monitor = new BulkUploadMonitorService(repo, options, NullLogger<BulkUploadMonitorService>.Instance);
        var active = new ConcurrentDictionary<Guid, byte>();
        active[jobA] = 0;
        active[jobB] = 0;
        var reports = new List<UploadJobStatusDto>();

        await monitor.MonitorAsync(
            active,
            isProducerComplete: () => true,
            onStatus: reports.Add);

        Assert.Empty(active);
        Assert.Contains(reports, r => r.JobId == jobA.ToString("D") && r.IsCompleted);
        Assert.Contains(reports, r => BulkUploadMonitorService.IsDuplicateCompleted(r));
    }

    private sealed class SlowUploadApi : IUploadApiRepository
    {
        private int _inFlight;

        public int MaxObservedConcurrency { get; private set; }

        public async Task<UploadResponseDto> UploadAsync(string filePath, CancellationToken cancellationToken = default)
        {
            var n = Interlocked.Increment(ref _inFlight);
            MaxObservedConcurrency = Math.Max(MaxObservedConcurrency, n);
            try
            {
                await Task.Delay(80, cancellationToken);
                var id = Guid.NewGuid();
                return new UploadResponseDto
                {
                    JobId = id.ToString("D"),
                    Status = UploadJobStatusDto.Waiting,
                    IncomingPath = $"incoming/{id:N}.jpg",
                    Message = "ok",
                    Id = 1,
                };
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }
    }

    private sealed class CompleteJobApi : IUploadJobApiRepository
    {
        public Task<UploadJobStatusDto> GetStatusAsync(Guid jobId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new UploadJobStatusDto
            {
                JobId = jobId.ToString("D"),
                Status = UploadJobStatusDto.Completed,
                Progress = 100,
            });

        public Task<UploadJobListDto> ListJobsAsync(
            string? status = null,
            int page = 1,
            int pageSize = 20,
            string sort = "created_at_desc",
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new UploadJobListDto());
    }

    private sealed class DictionaryJobApi : IUploadJobApiRepository
    {
        private readonly Dictionary<Guid, Queue<UploadJobStatusDto>> _map;
        private readonly Dictionary<Guid, UploadJobStatusDto> _last = new();

        public DictionaryJobApi(Dictionary<Guid, Queue<UploadJobStatusDto>> map) => _map = map;

        public Task<UploadJobStatusDto> GetStatusAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            var q = _map[jobId];
            if (q.Count > 0)
            {
                _last[jobId] = q.Dequeue();
            }

            return Task.FromResult(_last[jobId]);
        }

        public Task<UploadJobListDto> ListJobsAsync(
            string? status = null,
            int page = 1,
            int pageSize = 20,
            string sort = "created_at_desc",
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new UploadJobListDto());
    }

    private sealed class MemStorageRepo : IStorageRepository
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
