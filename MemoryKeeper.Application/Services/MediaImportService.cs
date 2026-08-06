using System.Collections.Concurrent;
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
            };

            if (Guid.TryParse(item.ContentHash, out var jobId))
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
                    _logger.LogWarning(
                        ex,
                        "Retry job probe failed; will re-upload. JobId={JobId}",
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

    /// <summary>Re-query persisted open jobs after app restart.</summary>
    public async Task<int> ResumePersistedJobsAsync(CancellationToken cancellationToken = default)
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

        var active = new ConcurrentDictionary<Guid, byte>(
            byId.Keys.Select(id => new KeyValuePair<Guid, byte>(id, 0)));
        var stillOpen = new ConcurrentDictionary<Guid, ImportSessionJobDto>(
            byId.Select(kv => new KeyValuePair<Guid, ImportSessionJobDto>(kv.Key, kv.Value)));
        var terminalCount = 0;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var monitorTask = _bulkUploadMonitorService.MonitorAsync(
            active,
            isProducerComplete: () => true,
            status =>
            {
                if (!Guid.TryParse(status.JobId, out var id) || !byId.TryGetValue(id, out var session))
                {
                    return;
                }

                if (status.IsTerminal)
                {
                    Interlocked.Increment(ref terminalCount);
                    stillOpen.TryRemove(id, out _);
                    if (status.IsCompleted)
                    {
                        InvalidateCatalogDebounced(force: true);
                    }
                }
                else
                {
                    stillOpen[id] = new ImportSessionJobDto
                    {
                        JobId = session.JobId,
                        FileName = session.FileName,
                        LocalFilePath = session.LocalFilePath,
                        Status = status.Status,
                        UploadedAt = session.UploadedAt,
                    };
                }
            },
            cts.Token);

        try
        {
            await monitorTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // ignore
        }

        var open = stillOpen.Values.ToList();
        if (open.Count == 0)
        {
            await _sessionStore.ClearAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _sessionStore.SaveAsync(open, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "{Prefix} Resumed persisted jobs. Terminal={Terminal}, StillOpen={Open}",
            PhotoRegisterLogPrefix,
            terminalCount,
            open.Count);
        return terminalCount;
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
            status => OnJobStatus(status, jobToState, states, progress),
            monitorCts.Token);

        foreach (var state in states.Where(s => s.JobId is not null && !s.IsAnalysisTerminal))
        {
            var id = state.JobId!.Value;
            jobToState[id] = state;
            activeJobs[id] = 0;
        }

        await PersistOpenJobsAsync(states, cancellationToken).ConfigureAwait(false);

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
            await PersistOpenJobsAsync(states, CancellationToken.None).ConfigureAwait(false);
            ReportFromStates(
                progress,
                states,
                currentFileName: null,
                isCompleted: true,
                statusSummary: "취소됨. 이미 접수된 Job은 서버에서 계속 처리됩니다.");
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

        await PersistOpenJobsAsync(states, CancellationToken.None).ConfigureAwait(false);

        if (states.Any(s => s.Status is ImportFileStatus.Completed or ImportFileStatus.Duplicate))
        {
            InvalidateCatalogDebounced(force: true);
        }

        var result = BuildResult(states, storage, sourceFolderPath);
        ReportFromStates(progress, states, currentFileName: null, isCompleted: true);

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
        CancellationToken cancellationToken)
    {
        await uploadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            state.Status = ImportFileStatus.Uploading;
            ReportFromStates(progress, allStates, state.FileName, isCompleted: false);

            var upload = await _uploadApiRepository
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

            jobToState[jobId] = state;
            activeJobs[jobId] = 0;

            _logger.LogInformation(
                "{Prefix} Upload accepted (analysis deferred). File={File}, JobId={JobId}",
                PhotoRegisterLogPrefix,
                state.FileName,
                jobId);

            await PersistOpenJobsAsync(allStates, cancellationToken).ConfigureAwait(false);
            ReportFromStates(progress, allStates, state.FileName, isCompleted: false);
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
            state.ErrorMessage = ex.Message;
            state.CompletedAt = DateTimeOffset.UtcNow;
            ReportFromStates(progress, allStates, state.FileName, isCompleted: false, lastError: ex.Message);
        }
        finally
        {
            uploadGate.Release();
        }
    }

    private void OnJobStatus(
        UploadJobStatusDto status,
        ConcurrentDictionary<Guid, ImportFileState> jobToState,
        IReadOnlyList<ImportFileState> allStates,
        IProgress<ImportProgressDto>? progress)
    {
        if (!Guid.TryParse(status.JobId, out var jobId) || !jobToState.TryGetValue(jobId, out var state))
        {
            return;
        }

        var previousCompleted = allStates.Count(s =>
            s.Status is ImportFileStatus.Completed or ImportFileStatus.Duplicate);

        ApplyJobStatus(state, status);
        ReportFromStates(
            progress,
            allStates,
            state.FileName,
            isCompleted: false,
            backendStatus: status.Status,
            backendProgress: status.Progress,
            currentPlugin: status.CurrentPlugin,
            jobId: status.JobId,
            lastError: status.LastError);

        var completedNow = allStates.Count(s =>
            s.Status is ImportFileStatus.Completed or ImportFileStatus.Duplicate);
        if (completedNow > previousCompleted)
        {
            InvalidateCatalogDebounced(force: false);
            _ = PersistOpenJobsAsync(allStates, CancellationToken.None);
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
            state.ErrorMessage = string.IsNullOrWhiteSpace(status.LastError)
                ? $"Upload job FAILED (job_id={status.JobId})."
                : status.LastError;
            state.CompletedAt = status.CompletedAt ?? DateTimeOffset.UtcNow;
            return;
        }

        if (status.IsCompleted)
        {
            state.Status = ImportFileStatus.Completed;
            state.CompletedAt = status.CompletedAt ?? DateTimeOffset.UtcNow;
            state.ErrorMessage = null;
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

    private Task PersistOpenJobsAsync(
        IReadOnlyList<ImportFileState> states,
        CancellationToken cancellationToken)
    {
        var jobs = states
            .Where(s => s.JobId is not null && !s.IsAnalysisTerminal)
            .Select(s => new ImportSessionJobDto
            {
                JobId = s.JobId!.Value.ToString("D"),
                FileName = s.FileName,
                LocalFilePath = s.LocalFilePath,
                Status = s.Status.ToString(),
                UploadedAt = s.UploadedAt,
            })
            .ToList();

        return jobs.Count == 0
            ? _sessionStore.ClearAsync(cancellationToken)
            : _sessionStore.SaveAsync(jobs, cancellationToken);
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
            ContentHash = state.JobId?.ToString("D"),
            RelativePath = state.IncomingPath,
            ErrorMessage = state.ErrorMessage,
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
        string? statusSummary = null)
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
            UploadedCount = waiting + processing + completed + duplicate,
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
            IsFailed = failed > 0 && completed == 0 && duplicate == 0 && isCompleted,
            StatusSummary = summary,
        });
    }
}
