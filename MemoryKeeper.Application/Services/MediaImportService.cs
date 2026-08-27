using System.Collections.Concurrent;
using MemoryKeeper.Application.Diagnostics;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.DTOs.Upload;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Domain.Entities;
using MemoryKeeper.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemoryKeeper.Application.Services;

/// <summary>
/// Imports photos via TC-Backend Upload + bulk job monitoring (Phase 3B).
/// Upload acceptance (max 3 concurrent) is decoupled from server analysis completion.
/// </summary>
public sealed class MediaImportService
{
    private const string PhotoRegisterLogPrefix = "[Photo Register]";
    private static readonly TimeSpan CatalogDebounce = TimeSpan.FromSeconds(2);

    private readonly IFileScanner _fileScanner;
    private readonly IStorageRepository _storageRepository;
    private readonly IUploadApiRepository _uploadApiRepository;
    private readonly IUploadJobApiRepository _uploadJobApiRepository;
    private readonly BulkUploadMonitorService _bulkUploadMonitorService;
    private readonly IImportJobSessionStore _sessionStore;
    private readonly ICatalogInvalidation _catalogInvalidation;
    private readonly ImportUploadOptions _options;
    private readonly ILogger<MediaImportService> _logger;
    private readonly object _catalogGate = new();
    private DateTime _lastCatalogInvalidateUtc = DateTime.MinValue;

    public MediaImportService(
        IFileScanner fileScanner,
        IStorageRepository storageRepository,
        IUploadApiRepository uploadApiRepository,
        IUploadJobApiRepository uploadJobApiRepository,
        BulkUploadMonitorService bulkUploadMonitorService,
        IImportJobSessionStore sessionStore,
        ICatalogInvalidation catalogInvalidation,
        IOptions<ImportUploadOptions> options,
        ILogger<MediaImportService> logger)
    {
        _fileScanner = fileScanner;
        _storageRepository = storageRepository;
        _uploadApiRepository = uploadApiRepository
            ?? throw new ArgumentNullException(nameof(uploadApiRepository));
        _uploadJobApiRepository = uploadJobApiRepository
            ?? throw new ArgumentNullException(nameof(uploadJobApiRepository));
        _bulkUploadMonitorService = bulkUploadMonitorService
            ?? throw new ArgumentNullException(nameof(bulkUploadMonitorService));
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        _catalogInvalidation = catalogInvalidation
            ?? throw new ArgumentNullException(nameof(catalogInvalidation));
        _options = options?.Value ?? new ImportUploadOptions();
        _logger = logger;
    }

    public Task<MediaImportResult> ImportAsync(
        MediaImportRequest request,
        CancellationToken cancellationToken = default) =>
        ImportAsync(request, progress: null, cancellationToken);

    public async Task<MediaImportResult> ImportAsync(
        MediaImportRequest request,
        IProgress<ImportProgressDto>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.SourceFolderPath))
        {
            throw new ArgumentException("Source folder path is required.", nameof(request));
        }

        var storage = await RequireStorageAsync(request.StorageId, cancellationToken).ConfigureAwait(false);
        var scannedFiles = await _fileScanner.ScanAsync(request.SourceFolderPath, cancellationToken)
            .ConfigureAwait(false);
        var states = scannedFiles
            .Select(path => new ImportFileState
            {
                LocalFilePath = path,
                FileName = Path.GetFileName(path),
                Status = ImportFileStatus.Pending,
            })
            .ToList();

        return await RunParallelImportAsync(
                states,
                storage,
                request.SourceFolderPath,
                progress,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Uploads only the preflight items classified as new.</summary>
    public async Task<MediaImportResult> ImportPreparedAsync(
        MediaImportRequest request,
        IncrementalImportPreflightResult preflight,
        IProgress<ImportProgressDto>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(preflight);
        if (!string.Equals(
                Path.GetFullPath(request.SourceFolderPath),
                Path.GetFullPath(preflight.SourceFolderPath),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("사진 폴더가 변경되어 등록 사전 검사가 다시 필요합니다.");
        }

        var storage = await RequireStorageAsync(request.StorageId, cancellationToken).ConfigureAwait(false);
        var states = preflight.UploadTargets.Select(item => new ImportFileState
        {
            LocalFilePath = item.FilePath,
            FileName = item.FileName,
            ContentHash = item.ContentHash,
            Status = ImportFileStatus.Pending,
        }).ToList();

        return await RunParallelImportAsync(
            states,
            storage,
            request.SourceFolderPath,
            progress,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retries failed items only. Existing job_ids are re-checked before any re-upload.
    /// </summary>
    public async Task<MediaImportResult> RetryFailedAsync(
        Guid storageId,
        string sourceFolderPath,
        IReadOnlyList<MediaImportItemResult> failedItems,
        IProgress<ImportProgressDto>? progress,
        CancellationToken cancellationToken = default)
    {
        var storage = await RequireStorageAsync(storageId, cancellationToken).ConfigureAwait(false);
        var states = new List<ImportFileState>();

        foreach (var item in failedItems.Where(i =>
                     i.Status is MediaStatus.Failed
                     && !string.IsNullOrWhiteSpace(i.OriginalPath)))
        {
            var state = new ImportFileState
            {
                LocalFilePath = item.OriginalPath,
                FileName = item.FileName,
                Status = ImportFileStatus.Pending,
                IncomingPath = item.RelativePath,
                ContentHash = ImportBackendIdentityProvider.IsSha256(item.ContentHash)
                    ? item.ContentHash
                    : null,
            };

            var savedJobId = item.JobId ?? item.ContentHash;
            if (Guid.TryParse(savedJobId, out var jobId))
            {
                try
                {
                    var status = await _uploadJobApiRepository
                        .GetStatusAsync(jobId, cancellationToken)
                        .ConfigureAwait(false);
                    ApplyJobStatus(state, status);
                    if (state.IsAnalysisTerminal)
                    {
                        states.Add(state);
                        continue;
                    }

                    state.JobId = jobId;
                    state.Status = MapNonTerminal(status.Status);
                    state.UploadedAt = DateTimeOffset.UtcNow;
                    states.Add(state);
                    continue;
                }
                catch (Exception ex)
                {
                    if (!BulkUploadMonitorService.IsNotFound(ex))
                    {
                        state.JobId = jobId;
                        state.Status = ImportFileStatus.Waiting;
                        state.ErrorCategory = PhotoRegisterLog.GetCategory(ex);
                        state.ErrorMessage = "기존 서버 작업 상태를 확인하지 못했습니다. 재업로드하지 않고 다음 확인을 기다립니다.";
                        states.Add(state);
                        PhotoRegisterLog.WriteWarning(state.FileName, "RETRY_RECONCILE", ex, jobId.ToString("D"));
                        continue;
                    }

                    _logger.LogInformation(
                        "Retry reconciliation found no Backend job. JobId={JobId}; upload may proceed.",
                        jobId);
                }
            }

            states.Add(state);
        }

        return await RunParallelImportAsync(
                states,
                storage,
                sourceFolderPath,
                progress,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<int> ResumePersistedJobsAsync(CancellationToken cancellationToken = default) =>
        ResumePersistedJobsCoreAsync(progress: null, cancellationToken);

    /// <summary>Restores the saved session immediately, then reconciles it in the background.</summary>
    public Task<int> ResumePersistedJobsAsync(
        IProgress<ImportProgressDto> progress,
        CancellationToken cancellationToken = default) =>
        ResumePersistedJobsCoreAsync(progress, cancellationToken);

    private async Task<int> ResumePersistedJobsCoreAsync(
        IProgress<ImportProgressDto>? progress,
        CancellationToken cancellationToken)
    {
        var saved = await _sessionStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (saved.Count == 0)
        {
            return 0;
        }

        var byId = new Dictionary<Guid, ImportSessionJobDto>();
        foreach (var job in saved)
        {
            if (Guid.TryParse(job.JobId, out var id))
            {
                byId[id] = job;
            }
        }

        if (byId.Count == 0)
        {
            await _sessionStore.ClearAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        }

        var states = byId.Select(pair => new ImportFileState
        {
            JobId = pair.Key,
            LocalFilePath = pair.Value.LocalFilePath,
            FileName = pair.Value.FileName,
            UploadedAt = pair.Value.UploadedAt,
            ContentHash = pair.Value.ContentHash,
            Status = ParsePersistedStatus(pair.Value.Status),
        }).ToList();

        ReportFromStates(
            progress,
            states,
            currentFileName: null,
            isCompleted: false,
            statusSummary: BuildResumeSummary(states, isStalled: false),
            isResumedSession: true,
            lastStatusCheckedAt: DateTimeOffset.Now);

        _ = MonitorResumedJobsAsync(states, progress, cancellationToken);
        _logger.LogInformation(
            "{Prefix} Restored persisted session immediately. Jobs={Count}",
            PhotoRegisterLogPrefix,
            states.Count);
        return states.Count;
    }

    private async Task MonitorResumedJobsAsync(
        IReadOnlyList<ImportFileState> states,
        IProgress<ImportProgressDto>? progress,
        CancellationToken cancellationToken)
    {
        var active = new ConcurrentDictionary<Guid, byte>(states
            .Where(state => state.JobId is not null && !state.IsAnalysisTerminal)
            .Select(state => new KeyValuePair<Guid, byte>(state.JobId!.Value, 0)));
        var byId = new ConcurrentDictionary<Guid, ImportFileState>(states
            .Where(state => state.JobId is not null)
            .Select(state => new KeyValuePair<Guid, ImportFileState>(state.JobId!.Value, state)));
        var stallPolicy = new ImportStallPolicy(_options.GetStalledThreshold());
        var checkpoint = CreateCheckpoint(states);

        try
        {
            await _bulkUploadMonitorService.MonitorAsync(
                active,
                isProducerComplete: () => true,
                status =>
                {
                    if (!Guid.TryParse(status.JobId, out var id) || !byId.TryGetValue(id, out var state))
                    {
                        return;
                    }

                    ApplyJobStatus(state, status);
                    var now = DateTimeOffset.Now;
                    var stalled = stallPolicy.Observe(status, active.Count, now);
                    if (status.IsTerminal)
                    {
                        _ = checkpoint.RequestAsync();
                        if (status.IsCompleted)
                        {
                            InvalidateCatalogDebounced(force: false);
                        }
                    }

                    ReportFromStates(
                        progress,
                        states,
                        state.FileName,
                        isCompleted: active.Count <= 1 && status.IsTerminal,
                        backendStatus: status.Status,
                        backendProgress: status.Progress,
                        currentPlugin: status.CurrentPlugin,
                        jobId: status.JobId,
                        lastError: status.LastError,
                        statusSummary: BuildResumeSummary(states, stalled),
                        lastFailureFileName: status.IsFailed ? state.FileName : null,
                        lastErrorCategory: status.IsFailed ? "BackendJob" : null,
                        isResumedSession: true,
                        isStalled: stalled,
                        lastStatusCheckedAt: now);
                },
                (jobId, exception) =>
                {
                    if (!BulkUploadMonitorService.IsNotFound(exception) || !byId.TryGetValue(jobId, out var state))
                    {
                        return;
                    }

                    state.Status = ImportFileStatus.Failed;
                    state.ErrorCategory = "BackendJobMissing";
                    state.ErrorMessage = "서버에서 기존 사진 등록 작업을 찾을 수 없습니다.";
                    state.CompletedAt = DateTimeOffset.UtcNow;
                    _ = checkpoint.RequestAsync();
                    PhotoRegisterLog.WriteWarning(state.FileName, "RESUME_RECONCILE", exception, jobId.ToString("D"));
                    ReportFromStates(
                        progress,
                        states,
                        state.FileName,
                        isCompleted: active.Count <= 1,
                        lastError: state.ErrorMessage,
                        statusSummary: BuildResumeSummary(states, isStalled: false),
                        lastFailureFileName: state.FileName,
                        lastErrorCategory: state.ErrorCategory,
                        isResumedSession: true,
                        lastStatusCheckedAt: DateTimeOffset.Now);
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The persisted checkpoint keeps all non-terminal jobs resumable.
        }
        finally
        {
            await checkpoint.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            await checkpoint.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<Storage> RequireStorageAsync(Guid storageId, CancellationToken cancellationToken)
    {
        var storage = await _storageRepository.GetByIdAsync(storageId, cancellationToken)
            .ConfigureAwait(false);
        if (storage is null)
        {
            throw new InvalidOperationException($"Storage '{storageId}' was not found.");
        }

        if (!storage.IsActive)
        {
            throw new InvalidOperationException($"Storage '{storage.Name}' is not active.");
        }

        return storage;
    }

    private async Task<MediaImportResult> RunParallelImportAsync(
        IReadOnlyList<ImportFileState> sourceStates,
        Storage storage,
        string sourceFolderPath,
        IProgress<ImportProgressDto>? progress,
        CancellationToken cancellationToken)
    {
        var maxConcurrent = _options.ClampMaxConcurrentUploads();
        var states = sourceStates.ToList();
        await using var checkpoint = CreateCheckpoint(states);

        _logger.LogInformation(
            "{Prefix} Parallel import started. Files={Count}, MaxConcurrentUploads={Max}",
            PhotoRegisterLogPrefix,
            states.Count,
            maxConcurrent);

        ReportFromStates(progress, states, currentFileName: null, isCompleted: false);

        var activeJobs = new ConcurrentDictionary<Guid, byte>();
        var jobToState = new ConcurrentDictionary<Guid, ImportFileState>();
        var uploadsComplete = 0;
        using var monitorCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var monitorTask = _bulkUploadMonitorService.MonitorAsync(
            activeJobs,
            isProducerComplete: () => Volatile.Read(ref uploadsComplete) == 1,
            status => OnJobStatus(status, jobToState, states, progress, checkpoint),
            cancellationToken: monitorCts.Token);

        foreach (var state in states.Where(s => s.JobId is not null && !s.IsAnalysisTerminal))
        {
            var id = state.JobId!.Value;
            jobToState[id] = state;
            activeJobs[id] = 0;
        }

        await checkpoint.RequestAsync().ConfigureAwait(false);
        await checkpoint.FlushAsync(cancellationToken).ConfigureAwait(false);

        using var uploadGate = new SemaphoreSlim(maxConcurrent, maxConcurrent);
        var uploadTargets = states
            .Where(s => s.Status == ImportFileStatus.Pending && s.JobId is null)
            .ToList();

        var uploadTasks = uploadTargets.Select(state => UploadOneAsync(
            state,
            uploadGate,
            activeJobs,
            jobToState,
            states,
            progress,
            checkpoint,
            cancellationToken));

        var cancelled = false;
        try
        {
            await Task.WhenAll(uploadTasks).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsCancellation(ex))
        {
            cancelled = true;
            MarkCancelledPendingAndUploading(states);
            await checkpoint.RequestAsync().ConfigureAwait(false);
            await checkpoint.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            var acceptedCount = states.Count(state => state.UploadAccepted);
            ReportFromStates(
                progress,
                states,
                currentFileName: null,
                isCompleted: true,
                statusSummary: $"추가 파일 전송을 중단했습니다. 이미 NAS에 접수된 {acceptedCount:N0}건은 서버에서 계속 처리됩니다.");
        }
        finally
        {
            Volatile.Write(ref uploadsComplete, 1);
        }

        if (!cancelled)
        {
            try
            {
                await monitorTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                MarkCancelledPendingAndUploading(states);
            }
        }
        else
        {
            monitorCts.Cancel();
            try
            {
                await monitorTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected
            }
        }

        await checkpoint.RequestAsync().ConfigureAwait(false);
        await checkpoint.FlushAsync(CancellationToken.None).ConfigureAwait(false);

        if (states.Any(s => s.Status is ImportFileStatus.Completed or ImportFileStatus.Duplicate))
        {
            InvalidateCatalogDebounced(force: true);
        }

        var result = BuildResult(states, storage, sourceFolderPath);
        ReportFromStates(
            progress,
            states,
            currentFileName: null,
            isCompleted: true,
            hasPersistenceWarning: checkpoint.HasWarning,
            statusSummary: checkpoint.HasWarning
                ? "사진은 NAS에 접수되었지만 이 PC의 진행 정보 저장이 지연되고 있습니다. 앱을 종료하지 말고 잠시 후 다시 확인해 주세요."
                : null);

        _logger.LogInformation(
            "{Prefix} Parallel import finished. Scanned={Scanned}, Completed={Completed}, Duplicate={Dup}, Failed={Failed}, Cancelled={Cancelled}",
            PhotoRegisterLogPrefix,
            result.ScannedCount,
            result.ImportedCount,
            result.DuplicateCount,
            result.FailedCount,
            states.Count(s => s.Status == ImportFileStatus.Cancelled));

        return result;
    }

    private static bool IsCancellation(Exception ex) =>
        ex is OperationCanceledException
        || (ex is AggregateException aggregate
            && aggregate.Flatten().InnerExceptions.All(inner => inner is OperationCanceledException));

    private static void MarkCancelledPendingAndUploading(IEnumerable<ImportFileState> states)
    {
        foreach (var state in states)
        {
            if (state.Status is ImportFileStatus.Pending or ImportFileStatus.Uploading)
            {
                state.Status = ImportFileStatus.Cancelled;
                state.ErrorMessage = "사용자 취소";
                state.CompletedAt = DateTimeOffset.UtcNow;
            }
        }
    }

    private async Task UploadOneAsync(
        ImportFileState state,
        SemaphoreSlim uploadGate,
        ConcurrentDictionary<Guid, byte> activeJobs,
        ConcurrentDictionary<Guid, ImportFileState> jobToState,
        IReadOnlyList<ImportFileState> allStates,
        IProgress<ImportProgressDto>? progress,
        ImportSessionCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        await uploadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            state.Status = ImportFileStatus.Uploading;
            ReportFromStates(progress, allStates, state.FileName, isCompleted: false);

            var upload = ImportBackendIdentityProvider.IsSha256(state.ContentHash)
                ? await _uploadApiRepository.UploadWithIdentityAsync(
                        state.LocalFilePath,
                        state.ContentHash!,
                        state.ContentHash!,
                        cancellationToken)
                    .ConfigureAwait(false)
                : await _uploadApiRepository
                    .UploadAsync(state.LocalFilePath, cancellationToken)
                    .ConfigureAwait(false);
            var accepted = UploadStatusDto.FromResponse(upload);

            if (!Guid.TryParse(accepted.JobId, out var jobId))
            {
                throw new InvalidOperationException(
                    $"Upload API returned an invalid job_id '{accepted.JobId}'.");
            }

            state.JobId = jobId;
            state.IncomingPath = upload.IncomingPath;
            state.UploadedAt = DateTimeOffset.UtcNow;
            state.Status = ImportFileStatus.Waiting;
            state.Progress = 0;
        }
        catch (OperationCanceledException)
        {
            state.Status = ImportFileStatus.Cancelled;
            state.ErrorMessage = "사용자 취소";
            state.CompletedAt = DateTimeOffset.UtcNow;
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Prefix} Upload failed. File={File}", PhotoRegisterLogPrefix, state.FileName);
            state.Status = ImportFileStatus.Failed;
            state.ErrorMessage = PhotoRegisterLog.SanitizeMessage(ex.Message);
            state.ErrorCategory = PhotoRegisterLog.GetCategory(ex);
            state.CompletedAt = DateTimeOffset.UtcNow;
            PhotoRegisterLog.WriteFailure(state.FileName, "BACKEND_ACCEPTANCE", ex);
            ReportFromStates(
                progress,
                allStates,
                state.FileName,
                isCompleted: false,
                lastError: state.ErrorMessage,
                lastFailureFileName: state.FileName,
                lastErrorCategory: state.ErrorCategory);
            return;
        }
        finally
        {
            uploadGate.Release();
        }

        var acceptedJobId = state.JobId!.Value;
        jobToState[acceptedJobId] = state;
        activeJobs[acceptedJobId] = 0;
        _logger.LogInformation(
            "{Prefix} Upload accepted (analysis deferred). File={File}, JobId={JobId}",
            PhotoRegisterLogPrefix,
            state.FileName,
            acceptedJobId);

        await checkpoint.RequestAsync().ConfigureAwait(false);
        state.HasPersistenceWarning = checkpoint.HasWarning;
        ReportFromStates(
            progress,
            allStates,
            state.FileName,
            isCompleted: false,
            hasPersistenceWarning: state.HasPersistenceWarning,
            statusSummary: state.HasPersistenceWarning
                ? "사진은 NAS에 접수되었습니다. 이 PC의 진행 정보 저장이 지연되어 복구를 다시 시도합니다."
                : null);
    }

    private void OnJobStatus(
        UploadJobStatusDto status,
        ConcurrentDictionary<Guid, ImportFileState> jobToState,
        IReadOnlyList<ImportFileState> allStates,
        IProgress<ImportProgressDto>? progress,
        ImportSessionCheckpoint checkpoint)
    {
        if (!Guid.TryParse(status.JobId, out var jobId) || !jobToState.TryGetValue(jobId, out var state))
        {
            return;
        }

        var previousCompleted = allStates.Count(s =>
            s.Status is ImportFileStatus.Completed or ImportFileStatus.Duplicate);

        ApplyJobStatus(state, status);
        if (status.IsTerminal)
        {
            _ = checkpoint.RequestAsync();
        }
        ReportFromStates(
            progress,
            allStates,
            state.FileName,
            isCompleted: false,
            backendStatus: status.Status,
            backendProgress: status.Progress,
            currentPlugin: status.CurrentPlugin,
            jobId: status.JobId,
            lastError: status.LastError,
            lastFailureFileName: status.IsFailed ? state.FileName : null,
            lastErrorCategory: status.IsFailed ? "BackendJob" : null,
            lastStatusCheckedAt: DateTimeOffset.Now);

        var completedNow = allStates.Count(s =>
            s.Status is ImportFileStatus.Completed or ImportFileStatus.Duplicate);
        if (completedNow > previousCompleted)
        {
            InvalidateCatalogDebounced(force: false);
        }
    }

    private static ImportFileStatus MapNonTerminal(string status) =>
        string.Equals(status, UploadJobStatusDto.Processing, StringComparison.OrdinalIgnoreCase)
            ? ImportFileStatus.Processing
            : ImportFileStatus.Waiting;

    private static void ApplyJobStatus(ImportFileState state, UploadJobStatusDto status)
    {
        state.Progress = status.Progress;
        state.CurrentPlugin = status.CurrentPlugin;
        if (state.JobId is null && Guid.TryParse(status.JobId, out var id))
        {
            state.JobId = id;
        }

        if (BulkUploadMonitorService.IsDuplicateCompleted(status))
        {
            state.Status = ImportFileStatus.Duplicate;
            state.ErrorMessage = "이미 등록된 사진입니다.";
            state.CompletedAt = status.CompletedAt ?? DateTimeOffset.UtcNow;
            return;
        }

        if (status.IsFailed)
        {
            state.Status = ImportFileStatus.Failed;
            state.ErrorCategory = "BackendJob";
            state.ErrorMessage = string.IsNullOrWhiteSpace(status.LastError)
                ? $"Upload job FAILED (job_id={status.JobId})."
                : status.LastError;
            state.CompletedAt = status.CompletedAt ?? DateTimeOffset.UtcNow;
            PhotoRegisterLog.WriteFailure(
                state.FileName,
                "BACKEND_ANALYSIS",
                new InvalidOperationException(state.ErrorMessage),
                status.JobId);
            return;
        }

        if (status.IsCompleted)
        {
            state.Status = ImportFileStatus.Completed;
            state.CompletedAt = status.CompletedAt ?? DateTimeOffset.UtcNow;
            state.ErrorMessage = null;
            state.ErrorCategory = null;
            return;
        }

        state.Status = MapNonTerminal(status.Status);
    }

    private void InvalidateCatalogDebounced(bool force)
    {
        lock (_catalogGate)
        {
            var now = DateTime.UtcNow;
            if (!force && now - _lastCatalogInvalidateUtc < CatalogDebounce)
            {
                return;
            }

            _lastCatalogInvalidateUtc = now;
            _catalogInvalidation.Invalidate(
                CatalogSurface.Gallery | CatalogSurface.Home | CatalogSurface.Visits | CatalogSurface.Pending);
            _logger.LogInformation(
                "{Prefix} Gallery Reload requested (debounced).",
                PhotoRegisterLogPrefix);
        }
    }

    private ImportSessionCheckpoint CreateCheckpoint(IReadOnlyList<ImportFileState> states) =>
        new(
            _sessionStore,
            () => BuildSessionJobs(states),
            () => states
                .Where(state => state.JobId is not null)
                .Select(state => state.JobId!.Value.ToString("D"))
                .ToArray(),
            _options.ClampSessionCheckpointBatchSize(),
            _options.GetSessionCheckpointInterval(),
            _logger);

    private static IReadOnlyList<ImportSessionJobDto> BuildSessionJobs(
        IReadOnlyList<ImportFileState> states) =>
        states
            .Where(s => s.JobId is not null && !s.IsAnalysisTerminal)
            .Select(s => new ImportSessionJobDto
            {
                JobId = s.JobId!.Value.ToString("D"),
                FileName = s.FileName,
                LocalFilePath = s.LocalFilePath,
                Status = s.Status.ToString(),
                UploadedAt = s.UploadedAt,
                ContentHash = s.ContentHash,
            })
            .ToList();

    private static ImportFileStatus ParsePersistedStatus(string? status) =>
        Enum.TryParse<ImportFileStatus>(status, ignoreCase: true, out var parsed)
            && parsed is ImportFileStatus.Waiting or ImportFileStatus.Processing
                ? parsed
                : ImportFileStatus.Waiting;

    private static string BuildResumeSummary(IReadOnlyList<ImportFileState> states, bool isStalled)
    {
        var completed = states.Count(state => state.Status == ImportFileStatus.Completed);
        var duplicate = states.Count(state => state.Status == ImportFileStatus.Duplicate);
        var waiting = states.Count(state => state.Status == ImportFileStatus.Waiting);
        var processing = states.Count(state => state.Status == ImportFileStatus.Processing);
        var failed = states.Count(state => state.Status == ImportFileStatus.Failed);
        var prefix = isStalled
            ? "서버 작업이 장시간 진행되지 않고 있습니다. "
            : "진행 중인 기존 사진 등록 작업 · ";
        return $"{prefix}NAS 완료 {completed + duplicate:N0} · 대기 {waiting:N0} · 처리 중 {processing:N0} · 실패 {failed:N0}";
    }

    private static MediaImportResult BuildResult(
        IReadOnlyList<ImportFileState> states,
        Storage storage,
        string sourceFolderPath) =>
        new()
        {
            SourceFolderPath = sourceFolderPath,
            StorageId = storage.Id,
            ScannedCount = states.Count,
            ImportedCount = states.Count(s => s.Status == ImportFileStatus.Completed),
            DuplicateCount = states.Count(s => s.Status == ImportFileStatus.Duplicate),
            FailedCount = states.Count(s => s.Status == ImportFileStatus.Failed),
            Items = states.Select(ToItemResult).ToList(),
        };

    private static MediaImportItemResult ToItemResult(ImportFileState state)
    {
        var mediaStatus = state.Status switch
        {
            ImportFileStatus.Completed => MediaStatus.Imported,
            ImportFileStatus.Duplicate => MediaStatus.Duplicate,
            ImportFileStatus.Cancelled => MediaStatus.Cancelled,
            ImportFileStatus.Failed => MediaStatus.Failed,
            _ => MediaStatus.Pending,
        };

        return new MediaImportItemResult
        {
            OriginalPath = state.LocalFilePath,
            FileName = state.FileName,
            MediaType = MediaType.Photo,
            Status = mediaStatus,
            ContentHash = state.ContentHash ?? state.JobId?.ToString("D"),
            JobId = state.JobId?.ToString("D"),
            RelativePath = state.IncomingPath,
            ErrorMessage = state.ErrorMessage,
            ErrorCategory = state.ErrorCategory,
        };
    }

    private static void ReportFromStates(
        IProgress<ImportProgressDto>? progress,
        IReadOnlyList<ImportFileState> states,
        string? currentFileName,
        bool isCompleted,
        string? backendStatus = null,
        int? backendProgress = null,
        string? currentPlugin = null,
        string? jobId = null,
        string? lastError = null,
        string? statusSummary = null,
        string? lastFailureFileName = null,
        string? lastErrorCategory = null,
        bool isResumedSession = false,
        bool isStalled = false,
        bool hasPersistenceWarning = false,
        DateTimeOffset? lastStatusCheckedAt = null)
    {
        if (progress is null)
        {
            return;
        }

        var total = states.Count;
        var pending = states.Count(s => s.Status == ImportFileStatus.Pending);
        var uploading = states.Count(s => s.Status == ImportFileStatus.Uploading);
        var waiting = states.Count(s => s.Status == ImportFileStatus.Waiting);
        var processing = states.Count(s => s.Status == ImportFileStatus.Processing);
        var completed = states.Count(s => s.Status == ImportFileStatus.Completed);
        var duplicate = states.Count(s => s.Status == ImportFileStatus.Duplicate);
        var failed = states.Count(s => s.Status == ImportFileStatus.Failed);
        var cancelled = states.Count(s => s.Status == ImportFileStatus.Cancelled);
        var uploadFinished = states.Count(s => s.IsUploadFinished);
        var analysisFinished = completed + duplicate + failed;

        var summary = statusSummary
            ?? $"전송 {uploadFinished} / {total} · 분석 {completed + duplicate} / {total} · 대기 {waiting} · 처리 중 {processing} · 실패 {failed}";

        progress.Report(new ImportProgressDto
        {
            TotalCount = total,
            ProcessedCount = analysisFinished + cancelled,
            ImportedCount = completed,
            DuplicateCount = duplicate,
            FailedCount = failed,
            PendingCount = pending,
            UploadingCount = uploading,
            UploadedCount = states.Count(state => state.UploadAccepted),
            WaitingCount = waiting,
            ProcessingCount = processing,
            CompletedCount = completed,
            CancelledCount = cancelled,
            UploadFinishedCount = uploadFinished,
            AnalysisFinishedCount = analysisFinished,
            CurrentFileName = currentFileName,
            CurrentStage = uploading > 0
                ? "UPLOADING"
                : processing > 0
                    ? UploadJobStatusDto.Processing
                    : waiting > 0
                        ? UploadJobStatusDto.Waiting
                        : isCompleted
                            ? UploadJobStatusDto.Completed
                            : "QUEUED",
            IsCompleted = isCompleted,
            BackendStatus = backendStatus,
            BackendProgress = backendProgress,
            CurrentPlugin = currentPlugin,
            JobId = jobId,
            LastError = lastError,
            LastFailureFileName = lastFailureFileName,
            LastErrorCategory = lastErrorCategory,
            IsResumedSession = isResumedSession,
            IsStalled = isStalled,
            HasPersistenceWarning = hasPersistenceWarning,
            LastStatusCheckedAt = lastStatusCheckedAt,
            IsFailed = failed > 0 && completed == 0 && duplicate == 0 && isCompleted,
            StatusSummary = summary,
        });
    }
}
