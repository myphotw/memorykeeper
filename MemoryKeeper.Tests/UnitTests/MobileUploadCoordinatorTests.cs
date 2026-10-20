using System.Collections.Concurrent;
using MemoryKeeper.Application.DTOs.Upload;
using MemoryKeeper.Mobile.Uploads;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class MobileUploadCoordinatorTests
{
    private const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task Selection_AcceptsAtMostOneHundredItems()
    {
        var api = new FakeUploadApi { PreflightIsDuplicate = true };
        var files = Enumerable.Range(1, 101)
            .Select(index => File($"photo-{index}.jpg"))
            .ToArray();
        var coordinator = CreateCoordinator(picker: new FakePicker(files), api: api);

        await coordinator.SelectAndQueueAsync();
        await WaitUntilAsync(() => coordinator.Items.Count == 100
                                   && coordinator.Items.All(item =>
                                       item.State == MobileUploadItemState.Duplicate));

        Assert.Equal(MobileUploadCoordinator.MaximumSelectionCount, coordinator.Items.Count);
        Assert.All(coordinator.Items, item =>
            Assert.Equal(MobileUploadItemState.Duplicate, item.State));
        Assert.Equal(0, api.UploadCalls);
        Assert.Contains("최대 100개", coordinator.NoticeMessage ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UploadPipeline_NeverRunsMoreThanTwoTransfersConcurrently()
    {
        var api = new FakeUploadApi { BlockUploads = true };
        var coordinator = CreateCoordinator(api: api);
        var batch = coordinator.QueueSelectedAsync(
            [File("one.jpg"), File("two.jpg"), File("three.mp4", "video/mp4")]);

        await WaitUntilAsync(() => api.ActiveUploads == 2);

        Assert.Equal(MobileUploadCoordinator.MaximumConcurrentUploads, api.MaximumActiveUploads);
        Assert.Equal(2, api.ActiveUploads);

        api.ReleaseUploads();
        await batch;

        Assert.Equal(3, api.UploadCalls);
        Assert.Equal(2, api.MaximumActiveUploads);
        Assert.All(coordinator.Items, item =>
            Assert.Equal(MobileUploadItemState.Completed, item.State));
    }

    [Fact]
    public async Task DuplicatePreflight_NeverOpensUploadRequest()
    {
        var api = new FakeUploadApi { PreflightIsDuplicate = true };
        var coordinator = CreateCoordinator(api: api);
        var released = 0;
        var source = new MobileSelectedMediaFile(
            "duplicate.jpg",
            "image/jpeg",
            1,
            _ => Task.FromResult<Stream>(new MemoryStream([1], writable: false)),
            () =>
            {
                Interlocked.Increment(ref released);
                return ValueTask.CompletedTask;
            });

        await coordinator.QueueSelectedAsync([source]);

        var item = Assert.Single(coordinator.Items);
        Assert.Equal(MobileUploadItemState.Duplicate, item.State);
        Assert.Equal(MobileUploadErrorCodes.DuplicateContent, item.FailureCode);
        Assert.Equal(0, api.UploadCalls);
        Assert.Equal(1, released);
    }

    [Fact]
    public async Task OriginalAccessFailure_IsPerItemAndDoesNotBlockOtherSelectedFiles()
    {
        var unavailable = new MobileSelectedMediaFile(
            "cloud-photo.jpg",
            "image/jpeg",
            0,
            _ => Task.FromException<Stream>(new MobileUploadException(
                MobileUploadErrorCodes.OriginalMediaUnavailable,
                "original unavailable")));
        var coordinator = CreateCoordinator(
            hasher: new MobileContentHasher(),
            api: new FakeUploadApi());

        await coordinator.QueueSelectedAsync([unavailable, File("available.jpg")]);

        var failed = Assert.Single(coordinator.Items, item => item.FileName == "cloud-photo.jpg");
        var completed = Assert.Single(coordinator.Items, item => item.FileName == "available.jpg");
        Assert.Equal(MobileUploadItemState.Failed, failed.State);
        Assert.Equal(MobileUploadErrorCodes.OriginalMediaUnavailable, failed.FailureCode);
        Assert.False(failed.CanRetry);
        Assert.Equal(MobileUploadItemState.Completed, completed.State);
    }

    [Fact]
    public async Task Item_ReportsRealPipelineStatesWithoutSyntheticHashProgress()
    {
        var scheduler = new ControlledScheduler();
        var api = new FakeUploadApi { ReturnProcessingBeforeCompleted = true };
        var coordinator = CreateCoordinator(api: api, scheduler: scheduler);
        var batch = coordinator.QueueSelectedAsync([File("state-photo.jpg")]);
        var item = await WaitForSingleItemAsync(coordinator);

        await scheduler.DelayEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(MobileUploadItemState.ServerProcessing, item.State);
        Assert.False(item.IsTransferProgressVisible);
        Assert.InRange(item.ServerProgress, 1, 99);

        scheduler.Release();
        await batch;

        Assert.Equal(MobileUploadItemState.Completed, item.State);
    }

    [Fact]
    public async Task CancelBeforeServerAcceptance_CancelsWorkWithoutRecoveryRecord()
    {
        var hasher = new BlockingHasher();
        var store = new InMemoryRecoveryStore();
        var coordinator = CreateCoordinator(hasher: hasher, store: store);
        var batch = coordinator.QueueSelectedAsync([File("cancel.jpg")]);
        var item = await WaitForSingleItemAsync(coordinator);
        await hasher.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        coordinator.Cancel(item);
        await batch;

        Assert.Equal(MobileUploadItemState.Canceled, item.State);
        Assert.Null(item.JobId);
        Assert.Empty(await store.LoadAsync());
    }

    [Fact]
    public async Task CancelWhileQueued_PreventsHashAndUploadAfterBatchCheckCompletes()
    {
        var api = new FakeUploadApi { BlockCapabilities = true };
        var hasher = new CountingHasher();
        var coordinator = CreateCoordinator(api: api, hasher: hasher);
        var batch = coordinator.QueueSelectedAsync([File("queued.jpg")]);
        var item = await WaitForSingleItemAsync(coordinator);

        coordinator.Cancel(item);
        api.ReleaseCapabilities();
        await batch;

        Assert.Equal(MobileUploadItemState.Canceled, item.State);
        Assert.Equal(0, hasher.Calls);
        Assert.Equal(0, api.UploadCalls);
    }

    [Fact]
    public async Task CancelAfterServerAcceptance_StopsTrackingButKeepsServerJobRecovery()
    {
        var api = new FakeUploadApi { BlockJobPolling = true };
        var store = new InMemoryRecoveryStore();
        var coordinator = CreateCoordinator(api: api, store: store);
        var batch = coordinator.QueueSelectedAsync([File("accepted.mp4", "video/mp4")]);
        var item = await WaitForSingleItemAsync(coordinator);
        await api.JobPollingEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        coordinator.Cancel(item);
        await batch;

        Assert.Equal(MobileUploadItemState.TrackingStopped, item.State);
        Assert.NotNull(item.JobId);
        var recovery = Assert.Single(await store.LoadAsync());
        Assert.Equal(item.JobId!.Value, recovery.JobId);
        Assert.Contains("상태 확인만 중단", item.Message ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RetryBeforeAcceptance_ReusesClientFileId()
    {
        var api = new FakeUploadApi { FailFirstUpload = true };
        var coordinator = CreateCoordinator(api: api);
        var released = 0;
        var source = new MobileSelectedMediaFile(
            "retry.jpg",
            "image/jpeg",
            1,
            _ => Task.FromResult<Stream>(new MemoryStream([1], writable: false)),
            () =>
            {
                Interlocked.Increment(ref released);
                return ValueTask.CompletedTask;
            });
        await coordinator.QueueSelectedAsync([source]);
        var item = Assert.Single(coordinator.Items);
        var originalClientFileId = item.ClientFileId;

        Assert.Equal(MobileUploadItemState.Failed, item.State);
        Assert.True(item.CanRetry);
        Assert.Equal(0, released);

        await coordinator.RetryAsync(item);

        Assert.Equal(MobileUploadItemState.Completed, item.State);
        Assert.Equal(2, api.UploadedClientFileIds.Count);
        Assert.All(api.UploadedClientFileIds, id => Assert.Equal(originalClientFileId, id));
        Assert.Equal(1, released);
    }

    [Fact]
    public async Task Restore_QueriesAcceptedJobCleansRecoveryAndRefreshesGallery()
    {
        var jobId = Guid.NewGuid();
        var store = new InMemoryRecoveryStore(
            [new MobileUploadRecoveryRecord(jobId, Guid.NewGuid().ToString("D"), "restored.jpg", DateTimeOffset.UtcNow)]);
        var api = new FakeUploadApi();
        var coordinator = CreateCoordinator(api: api, store: store);
        var refreshCount = 0;
        coordinator.GalleryRefreshRequested += (_, _) => refreshCount++;

        await coordinator.RestoreAsync();

        var item = Assert.Single(coordinator.Items);
        Assert.Equal(MobileUploadItemState.Completed, item.State);
        Assert.Equal(jobId, item.JobId);
        Assert.Empty(await store.LoadAsync());
        Assert.Equal(1, refreshCount);
        Assert.Equal(1, api.JobCalls);
    }

    [Fact]
    public async Task JobFailure_UsesFailureCodeInsteadOfLastErrorText()
    {
        var jobId = Guid.NewGuid();
        var store = new InMemoryRecoveryStore(
            [new MobileUploadRecoveryRecord(jobId, Guid.NewGuid().ToString("D"), "mismatch.jpg", DateTimeOffset.UtcNow)]);
        var api = new FakeUploadApi
        {
            JobResult = FailedJob(
                jobId,
                MobileUploadErrorCodes.ContentSha256Mismatch,
                "arbitrary localized worker message"),
        };
        var coordinator = CreateCoordinator(api: api, store: store);

        await coordinator.RestoreAsync();

        var item = Assert.Single(coordinator.Items);
        Assert.Equal(MobileUploadItemState.Failed, item.State);
        Assert.Equal(MobileUploadErrorCodes.ContentSha256Mismatch, item.FailureCode);
        Assert.False(item.CanRetry);
        Assert.Contains("다시 선택", item.Message ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("worker", item.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AcceptedFailedJob_RetryRechecksJobWithoutUploadingOriginalAgain()
    {
        var jobId = Guid.NewGuid();
        var store = new InMemoryRecoveryStore(
            [new MobileUploadRecoveryRecord(jobId, Guid.NewGuid().ToString("D"), "failed.mp4", DateTimeOffset.UtcNow)]);
        var api = new FakeUploadApi
        {
            JobResult = FailedJob(jobId, "TRANSCODE_FAILED", "worker details"),
        };
        var coordinator = CreateCoordinator(api: api, store: store);

        await coordinator.RestoreAsync();
        var item = Assert.Single(coordinator.Items);

        Assert.True(item.CanRetry);
        await coordinator.RetryAsync(item);

        Assert.Equal(MobileUploadItemState.Failed, item.State);
        Assert.Equal(2, api.JobCalls);
        Assert.Equal(0, api.UploadCalls);
    }

    [Fact]
    public void CoordinatorSource_DoesNotPersistOriginalBytesPathsOrCredentials()
    {
        var source = ReadSource("MemoryKeeper.Mobile", "Uploads", "MobileUploadCoordinator.cs");
        var store = ReadSource("MemoryKeeper.Mobile", "Uploads", "MauiMobileUploadRecoveryStore.cs");

        Assert.DoesNotContain("File.Copy", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteAllBytes", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CacheDirectory", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BearerToken", store, StringComparison.Ordinal);
        Assert.DoesNotContain("FullPath", store, StringComparison.Ordinal);
        Assert.Contains("JobId", store, StringComparison.Ordinal);
        Assert.Contains("ClientFileId", store, StringComparison.Ordinal);

        string[] expectedRecoveryFields = ["AcceptedAt", "ClientFileId", "FileName", "JobId"];
        Assert.Equal(
            expectedRecoveryFields,
            typeof(MobileUploadRecoveryRecord)
                .GetProperties()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void HomePage_ShowsUploadSummaryItemsProgressCancelAndRetry()
    {
        var xaml = ReadSource("MemoryKeeper.Mobile", "Views", "HomePage.xaml");
        var viewModel = ReadSource("MemoryKeeper.Mobile", "ViewModels", "HomeViewModel.cs");
        var startup = ReadSource("MemoryKeeper.Mobile", "MauiProgram.cs");

        Assert.Contains("IsVisible=\"{Binding IsUploadPanelOpen}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Uploads.Items}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding FileName}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding FileSizeText}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding StatusText}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Progress=\"{Binding TransferProgress}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Path=CancelUploadCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("Path=RetryUploadCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("_uploads.SelectAndQueueAsync()", viewModel, StringComparison.Ordinal);
        Assert.Contains("await RefreshAsync();", viewModel, StringComparison.Ordinal);
        Assert.Contains("CurrentContext.Scope", viewModel, StringComparison.Ordinal);
        Assert.Contains("AddSingleton<MobileUploadCoordinator>()", startup, StringComparison.Ordinal);
    }

    private static MobileUploadCoordinator CreateCoordinator(
        IMobileMediaPicker? picker = null,
        IMobileContentHasher? hasher = null,
        FakeUploadApi? api = null,
        InMemoryRecoveryStore? store = null,
        IMobileUploadScheduler? scheduler = null) => new(
        picker ?? new FakePicker([]),
        hasher ?? new FixedHasher(),
        api ?? new FakeUploadApi(),
        store ?? new InMemoryRecoveryStore(),
        scheduler ?? new ImmediateScheduler());

    private static MobileSelectedMediaFile File(
        string name,
        string contentType = "image/jpeg")
    {
        var bytes = Enumerable.Range(0, 4096).Select(index => (byte)(index % 251)).ToArray();
        return new MobileSelectedMediaFile(
            name,
            contentType,
            bytes.Length,
            _ => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false)));
    }

    private static MobileUploadJobResult CompletedJob(Guid jobId) => MobileUploadJobResult.From(new UploadJobStatusDto
    {
        JobId = jobId.ToString("D"),
        Status = UploadJobStatusDto.Completed,
        Progress = 100,
    });

    private static MobileUploadJobResult ProcessingJob(Guid jobId) => MobileUploadJobResult.From(new UploadJobStatusDto
    {
        JobId = jobId.ToString("D"),
        Status = UploadJobStatusDto.Processing,
        Progress = 50,
    });

    private static MobileUploadJobResult FailedJob(Guid jobId, string code, string lastError) =>
        MobileUploadJobResult.From(new UploadJobStatusDto
        {
            JobId = jobId.ToString("D"),
            Status = UploadJobStatusDto.Failed,
            Progress = 50,
            FailureCode = code,
            LastError = lastError,
        });

    private static async Task<MobileUploadItem> WaitForSingleItemAsync(MobileUploadCoordinator coordinator)
    {
        await WaitUntilAsync(() => coordinator.Items.Count == 1);
        return Assert.Single(coordinator.Items);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static string ReadSource(params string[] parts) =>
        System.IO.File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. parts]));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !System.IO.File.Exists(Path.Combine(directory.FullName, "MemoryKeeper.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
               ?? throw new DirectoryNotFoundException("MemoryKeeper repository root was not found.");
    }

    private sealed class FakePicker(IReadOnlyList<MobileSelectedMediaFile> files) : IMobileMediaPicker
    {
        public Task<IReadOnlyList<MobileSelectedMediaFile>> PickMultipleAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(files);
    }

    private sealed class FixedHasher : IMobileContentHasher
    {
        public Task<MobileContentHashResult> ComputeSha256Async(
            MobileSelectedMediaFile file,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MobileContentHashResult(Sha, file.FileSize));
    }

    private sealed class BlockingHasher : IMobileContentHasher
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<MobileContentHashResult> ComputeSha256Async(
            MobileSelectedMediaFile file,
            CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new MobileContentHashResult(Sha, file.FileSize);
        }
    }

    private sealed class CountingHasher : IMobileContentHasher
    {
        public int Calls { get; private set; }

        public Task<MobileContentHashResult> ComputeSha256Async(
            MobileSelectedMediaFile file,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new MobileContentHashResult(Sha, file.FileSize));
        }
    }

    private sealed class FakeUploadApi : IMobileUploadApiClient
    {
        private readonly TaskCompletionSource _uploadRelease =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _capabilitiesRelease =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _activeUploads;
        private int _uploadCalls;
        private int _jobCalls;
        private int _maximumActiveUploads;

        public bool PreflightIsDuplicate { get; init; }
        public bool BlockCapabilities { get; init; }
        public bool BlockUploads { get; init; }
        public bool FailFirstUpload { get; init; }
        public bool BlockJobPolling { get; init; }
        public bool ReturnProcessingBeforeCompleted { get; init; }
        public int ActiveUploads => Volatile.Read(ref _activeUploads);
        public int MaximumActiveUploads => Volatile.Read(ref _maximumActiveUploads);
        public int UploadCalls => Volatile.Read(ref _uploadCalls);
        public int JobCalls => Volatile.Read(ref _jobCalls);
        public ConcurrentQueue<string> UploadedClientFileIds { get; } = new();
        public TaskCompletionSource JobPollingEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public MobileUploadJobResult? JobResult { get; init; }

        public async Task<MobileUploadBatchCapabilities> GetCapabilitiesAsync(
            CancellationToken cancellationToken = default)
        {
            if (BlockCapabilities)
            {
                await _capabilitiesRelease.Task.WaitAsync(cancellationToken);
            }

            return new MobileUploadBatchCapabilities(
                CanUpload: true,
                SupportsPreflight: true,
                PreflightPath: "/api/common/upload/preflight",
                IsLegacyUnknown: false,
                ResultCode: null,
                FailureCodes: new HashSet<string>(StringComparer.Ordinal));
        }

        public Task<MobileUploadPreflightResult> PreflightAsync(
            MobileUploadBatchCapabilities capabilities,
            string contentSha256,
            CancellationToken cancellationToken = default) => Task.FromResult(
            PreflightIsDuplicate
                ? new MobileUploadPreflightResult(
                    MobileUploadPreflightDisposition.Duplicate,
                    FailureCode: MobileUploadErrorCodes.DuplicateContent)
                : new MobileUploadPreflightResult(MobileUploadPreflightDisposition.UploadAllowed));

        public async Task<UploadResponseDto> UploadAsync(
            MobileUploadFile file,
            CancellationToken cancellationToken = default,
            IProgress<MobileUploadProgress>? progress = null)
        {
            var call = Interlocked.Increment(ref _uploadCalls);
            UploadedClientFileIds.Enqueue(file.ClientFileId);
            if (FailFirstUpload && call == 1)
            {
                throw new MobileUploadException(MobileUploadErrorCodes.Network, "offline");
            }

            var active = Interlocked.Increment(ref _activeUploads);
            UpdateMaximumActiveUploads(active);
            try
            {
                progress?.Report(new MobileUploadProgress(file.Source.FileSize, file.Source.FileSize));
                if (BlockUploads)
                {
                    await _uploadRelease.Task.WaitAsync(cancellationToken);
                }

                return new UploadResponseDto
                {
                    JobId = Guid.NewGuid().ToString("D"),
                    Status = UploadJobStatusDto.Waiting,
                };
            }
            finally
            {
                Interlocked.Decrement(ref _activeUploads);
            }
        }

        public async Task<MobileUploadJobResult> GetJobAsync(
            Guid jobId,
            CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref _jobCalls);
            JobPollingEntered.TrySetResult();
            if (BlockJobPolling)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            if (JobResult is not null)
            {
                return JobResult;
            }

            return ReturnProcessingBeforeCompleted && call == 1
                ? ProcessingJob(jobId)
                : CompletedJob(jobId);
        }

        public void ReleaseUploads() => _uploadRelease.TrySetResult();

        public void ReleaseCapabilities() => _capabilitiesRelease.TrySetResult();

        private void UpdateMaximumActiveUploads(int active)
        {
            while (true)
            {
                var current = MaximumActiveUploads;
                if (active <= current
                    || Interlocked.CompareExchange(ref _maximumActiveUploads, active, current) == current)
                {
                    return;
                }
            }
        }
    }

    private sealed class InMemoryRecoveryStore : IMobileUploadRecoveryStore
    {
        private IReadOnlyList<MobileUploadRecoveryRecord> _records;

        public InMemoryRecoveryStore(IReadOnlyList<MobileUploadRecoveryRecord>? records = null)
        {
            _records = records?.ToArray() ?? [];
        }

        public Task<IReadOnlyList<MobileUploadRecoveryRecord>> LoadAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(_records);

        public Task SaveAsync(
            IReadOnlyList<MobileUploadRecoveryRecord> records,
            CancellationToken cancellationToken = default)
        {
            _records = records.ToArray();
            return Task.CompletedTask;
        }
    }

    private sealed class ImmediateScheduler : IMobileUploadScheduler
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

        public Task DelayBeforeNextJobPollAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class ControlledScheduler : IMobileUploadScheduler
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource DelayEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

        public async Task DelayBeforeNextJobPollAsync(CancellationToken cancellationToken)
        {
            DelayEntered.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
        }

        public void Release() => _release.TrySetResult();
    }
}
