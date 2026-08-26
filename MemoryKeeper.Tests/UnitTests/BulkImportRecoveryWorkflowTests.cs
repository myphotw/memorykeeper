using System.Net;
using MemoryKeeper.Application;
using MemoryKeeper.Application.Diagnostics;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.DTOs.Upload;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Domain.Entities;
using MemoryKeeper.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class BulkImportRecoveryWorkflowTests
{
    [Fact]
    public async Task BackendAcceptance_SessionSaveFailure_DoesNotBecomeUploadFailure()
    {
        var jobId = Guid.NewGuid();
        var upload = new FakeUploadApi(jobId);
        var jobs = new SequenceJobApi(jobId, Status(jobId, UploadJobStatusDto.Completed));
        var session = new RecordingSessionStore { FailSave = true };
        var service = CreateService(upload, jobs, session, checkpointBatchSize: 1);

        var result = await service.ImportAsync(Request());

        Assert.Equal(0, result.FailedCount);
        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(1, upload.CallCount);
        Assert.True(session.SaveAttempts > 0);
    }

    [Fact]
    public async Task BackendUploadFailure_RemainsFailedWithReasonAndCategory()
    {
        var expected = new IOException("synthetic transport failure");
        var upload = new FakeUploadApi(expected);
        var jobs = new SequenceJobApi();
        var service = CreateService(upload, jobs, new RecordingSessionStore());

        var result = await service.ImportAsync(Request());

        var failed = Assert.Single(result.Items);
        Assert.Equal(MediaStatus.Failed, failed.Status);
        Assert.Contains("synthetic transport failure", failed.ErrorMessage);
        Assert.Equal("FileIO", failed.ErrorCategory);
    }

    [Fact]
    public async Task SessionCheckpoint_CoalescesOneThousandDirtyUpdates()
    {
        var store = new RecordingSessionStore();
        var snapshot = Enumerable.Range(0, 1000).Select(index => new ImportSessionJobDto
        {
            JobId = Guid.NewGuid().ToString("D"),
            FileName = $"IMG_{index:0000}.jpg",
            LocalFilePath = $"synthetic/{index:0000}.jpg",
            Status = "Waiting",
        }).ToList();
        await using var checkpoint = new ImportSessionCheckpoint(
            store,
            () => snapshot,
            () => snapshot.Select(job => job.JobId).ToArray(),
            batchSize: 100,
            interval: TimeSpan.FromMinutes(1),
            NullLogger.Instance);

        for (var index = 0; index < 1000; index++)
        {
            await checkpoint.RequestAsync();
        }

        await checkpoint.FlushAsync();
        Assert.InRange(store.SaveAttempts, 10, 11);
    }

    [Fact]
    public async Task RestartResume_LoadsImmediately_AndReconcilesToBackendTerminalState()
    {
        var waitingId = Guid.NewGuid();
        var processingId = Guid.NewGuid();
        var session = new RecordingSessionStore
        {
            Jobs =
            [
                Session(waitingId, "WAITING.jpg", "Waiting"),
                Session(processingId, "PROCESSING.jpg", "Processing"),
            ],
        };
        var jobs = new SequenceJobApi(
            (waitingId, [Status(waitingId, UploadJobStatusDto.Waiting), Status(waitingId, UploadJobStatusDto.Completed)]),
            (processingId, [Status(processingId, UploadJobStatusDto.Processing), Status(processingId, UploadJobStatusDto.Completed)]));
        var service = CreateService(new FakeUploadApi(Guid.NewGuid()), jobs, session);
        var reports = new List<ImportProgressDto>();

        var resumed = await service.ResumePersistedJobsAsync(new ImmediateProgress<ImportProgressDto>(reports.Add));
        await session.Cleared.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, resumed);
        Assert.Contains(reports, report => report.IsResumedSession && report.WaitingCount == 1 && report.ProcessingCount == 1);
        Assert.Contains(reports, report => report.CompletedCount == 2);
    }

    [Theory]
    [InlineData(UploadJobStatusDto.Completed)]
    [InlineData(UploadJobStatusDto.Processing)]
    public async Task Retry_WithExistingBackendJob_DoesNotReupload(string backendStatus)
    {
        var jobId = Guid.NewGuid();
        var upload = new FakeUploadApi(Guid.NewGuid());
        var sequence = backendStatus == UploadJobStatusDto.Completed
            ? new[] { Status(jobId, UploadJobStatusDto.Completed) }
            : new[] { Status(jobId, UploadJobStatusDto.Processing), Status(jobId, UploadJobStatusDto.Completed) };
        var jobs = new SequenceJobApi(jobId, sequence);
        var service = CreateService(upload, jobs, new RecordingSessionStore());
        var failed = new MediaImportItemResult
        {
            OriginalPath = "synthetic/retry.jpg",
            FileName = "retry.jpg",
            Status = MediaStatus.Failed,
            ContentHash = jobId.ToString("D"),
        };

        var result = await service.RetryFailedAsync(StorageId, "synthetic", [failed], progress: null);

        Assert.Equal(0, upload.CallCount);
        Assert.Equal(1, result.ImportedCount);
    }

    [Fact]
    public void StalledPolicy_DetectsNoProgress_AndClearsAfterRecovery()
    {
        var started = new DateTimeOffset(2026, 8, 27, 0, 0, 0, TimeSpan.Zero);
        var policy = new ImportStallPolicy(TimeSpan.FromMinutes(10), started);
        var waiting = Status(Guid.NewGuid(), UploadJobStatusDto.Waiting);
        var completed = Status(Guid.NewGuid(), UploadJobStatusDto.Completed);

        Assert.False(policy.Observe(waiting, activeJobCount: 10, started.AddMinutes(9)));
        Assert.True(policy.Observe(waiting, activeJobCount: 10, started.AddMinutes(10)));
        Assert.False(policy.Observe(completed, activeJobCount: 9, started.AddMinutes(11)));
    }

    [Fact]
    public async Task Cancel_StopsNewWork_ButKeepsAcceptedJobInSession()
    {
        var jobId = Guid.NewGuid();
        var session = new RecordingSessionStore();
        var jobs = new BlockingJobApi(jobId);
        var service = CreateService(new FakeUploadApi(jobId), jobs, session, checkpointBatchSize: 1);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        var result = await service.ImportAsync(Request(), progress: null, cancellation.Token);

        Assert.Equal(0, result.FailedCount);
        Assert.Equal(MediaStatus.Pending, Assert.Single(result.Items).Status);
        Assert.Contains(session.Jobs, item => item.JobId == jobId.ToString("D"));
    }

    [Fact]
    public void PhotoRegisterLog_RedactsCredentialQueryValues()
    {
        var sanitized = PhotoRegisterLog.SanitizeMessage(
            "request failed https://host/path?token=secret-value&api_key=another-secret");

        Assert.DoesNotContain("secret-value", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("another-secret", sanitized, StringComparison.Ordinal);
        Assert.Contains("<redacted>", sanitized, StringComparison.Ordinal);
    }

    private static readonly Guid StorageId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static MediaImportRequest Request() => new()
    {
        SourceFolderPath = "synthetic",
        StorageId = StorageId,
    };

    private static MediaImportService CreateService(
        IUploadApiRepository upload,
        IUploadJobApiRepository jobs,
        IImportJobSessionStore session,
        int checkpointBatchSize = 25)
    {
        var options = Options.Create(new ImportUploadOptions
        {
            MaxConcurrentUploads = 3,
            MaxConcurrentJobPolls = 5,
            SessionCheckpointBatchSize = checkpointBatchSize,
            SessionCheckpointIntervalSeconds = 1,
        });
        return new MediaImportService(
            new SyntheticScanner(),
            new StorageRepository(),
            upload,
            jobs,
            new BulkUploadMonitorService(jobs, options, NullLogger<BulkUploadMonitorService>.Instance),
            session,
            new CatalogInvalidation(),
            options,
            NullLogger<MediaImportService>.Instance);
    }

    private static UploadJobStatusDto Status(Guid id, string status) => new()
    {
        JobId = id.ToString("D"),
        Status = status,
        Progress = status == UploadJobStatusDto.Completed ? 100 : 0,
    };

    private static ImportSessionJobDto Session(Guid id, string fileName, string status) => new()
    {
        JobId = id.ToString("D"),
        FileName = fileName,
        LocalFilePath = $"synthetic/{fileName}",
        Status = status,
        UploadedAt = DateTimeOffset.UtcNow,
    };

    private sealed class ImmediateProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class SyntheticScanner : IFileScanner
    {
        public Task<IReadOnlyList<string>> ScanAsync(string rootPath, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(["synthetic/photo.jpg"]);

        public MediaType? ResolveMediaType(string filePath) => MediaType.Photo;
    }

    private sealed class StorageRepository : IStorageRepository
    {
        private static readonly Storage Storage = new()
        {
            Id = StorageId,
            Name = "Synthetic",
            StorageType = StorageType.Local,
            PhotoRoot = "synthetic",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        public Task<Storage?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Storage?>(id == StorageId ? Storage : null);
        public Task<IReadOnlyList<Storage>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Storage>>([Storage]);
        public Task AddAsync(Storage storage, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateAsync(Storage storage, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(Storage storage, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeUploadApi : IUploadApiRepository
    {
        private readonly Guid _jobId;
        private readonly Exception? _exception;
        public int CallCount { get; private set; }

        public FakeUploadApi(Guid jobId) => _jobId = jobId;
        public FakeUploadApi(Exception exception) => _exception = exception;

        public Task<UploadResponseDto> UploadAsync(string filePath, CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (_exception is not null)
            {
                return Task.FromException<UploadResponseDto>(_exception);
            }

            return Task.FromResult(new UploadResponseDto
            {
                JobId = _jobId.ToString("D"),
                Status = UploadJobStatusDto.Waiting,
                IncomingPath = $"incoming/{_jobId:N}.jpg",
            });
        }
    }

    private sealed class SequenceJobApi : IUploadJobApiRepository
    {
        private readonly Dictionary<Guid, Queue<UploadJobStatusDto>> _statuses = [];
        private readonly Dictionary<Guid, UploadJobStatusDto> _last = [];

        public SequenceJobApi()
        {
        }

        public SequenceJobApi(Guid id, params UploadJobStatusDto[] statuses) => _statuses[id] = new Queue<UploadJobStatusDto>(statuses);

        public SequenceJobApi(params (Guid Id, UploadJobStatusDto[] Statuses)[] items)
        {
            foreach (var item in items)
            {
                _statuses[item.Id] = new Queue<UploadJobStatusDto>(item.Statuses);
            }
        }

        public Task<UploadJobStatusDto> GetStatusAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            var queue = _statuses[jobId];
            if (queue.Count > 0)
            {
                _last[jobId] = queue.Dequeue();
            }

            return Task.FromResult(_last[jobId]);
        }

        public Task<UploadJobListDto> ListJobsAsync(string? status = null, int page = 1, int pageSize = 20, string sort = "created_at_desc", CancellationToken cancellationToken = default) =>
            Task.FromResult(new UploadJobListDto());
    }

    private sealed class BlockingJobApi(Guid expectedJobId) : IUploadJobApiRepository
    {
        public async Task<UploadJobStatusDto> GetStatusAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            Assert.Equal(expectedJobId, jobId);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable after infinite delay.");
        }

        public Task<UploadJobListDto> ListJobsAsync(string? status = null, int page = 1, int pageSize = 20, string sort = "created_at_desc", CancellationToken cancellationToken = default) =>
            Task.FromResult(new UploadJobListDto());
    }

    private sealed class RecordingSessionStore : IImportJobSessionStore
    {
        public List<ImportSessionJobDto> Jobs { get; set; } = [];
        public int SaveAttempts { get; private set; }
        public bool FailSave { get; init; }
        public TaskCompletionSource Cleared { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task SaveAsync(IReadOnlyList<ImportSessionJobDto> jobs, CancellationToken cancellationToken = default)
        {
            SaveAttempts++;
            if (FailSave)
            {
                throw new IOException("synthetic session write failure");
            }

            Jobs = jobs.ToList();
            return Task.CompletedTask;
        }

        public Task UpdateAsync(
            IReadOnlyList<ImportSessionJobDto> openJobs,
            IReadOnlyCollection<string> managedJobIds,
            CancellationToken cancellationToken = default)
        {
            SaveAttempts++;
            if (FailSave)
            {
                throw new IOException("synthetic session write failure");
            }

            var managed = managedJobIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            Jobs = Jobs.Where(job => !managed.Contains(job.JobId)).Concat(openJobs).ToList();
            if (Jobs.Count == 0)
            {
                Cleared.TrySetResult();
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ImportSessionJobDto>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ImportSessionJobDto>>(Jobs.ToList());

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            Jobs.Clear();
            Cleared.TrySetResult();
            return Task.CompletedTask;
        }
    }
}
