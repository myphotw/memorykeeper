using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.DTOs.Upload;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Domain.Entities;
using MemoryKeeper.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Application.Services;

/// <summary>
/// Imports photos via TC-Backend Upload + job monitoring (V2 — Backend Upload only).
/// </summary>
public sealed class MediaImportService
{
    private const string PhotoRegisterLogPrefix = "[Photo Register]";

    private readonly IFileScanner _fileScanner;
    private readonly IStorageRepository _storageRepository;
    private readonly IUploadApiRepository _uploadApiRepository;
    private readonly UploadMonitorService _uploadMonitorService;
    private readonly ICatalogInvalidation _catalogInvalidation;
    private readonly ILogger<MediaImportService> _logger;

    public MediaImportService(
        IFileScanner fileScanner,
        IStorageRepository storageRepository,
        IUploadApiRepository uploadApiRepository,
        UploadMonitorService uploadMonitorService,
        ICatalogInvalidation catalogInvalidation,
        ILogger<MediaImportService> logger)
    {
        _fileScanner = fileScanner;
        _storageRepository = storageRepository;
        _uploadApiRepository = uploadApiRepository
            ?? throw new ArgumentNullException(nameof(uploadApiRepository));
        _uploadMonitorService = uploadMonitorService
            ?? throw new ArgumentNullException(nameof(uploadMonitorService));
        _catalogInvalidation = catalogInvalidation
            ?? throw new ArgumentNullException(nameof(catalogInvalidation));
        _logger = logger;
    }

    public Task<MediaImportResult> ImportAsync(
        MediaImportRequest request,
        CancellationToken cancellationToken = default)
    {
        return ImportAsync(request, progress: null, cancellationToken);
    }

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

        var storage = await _storageRepository.GetByIdAsync(request.StorageId, cancellationToken);
        if (storage is null)
        {
            throw new InvalidOperationException($"Storage '{request.StorageId}' was not found.");
        }

        if (!storage.IsActive)
        {
            throw new InvalidOperationException($"Storage '{storage.Name}' is not active.");
        }

        return await ImportViaBackendUploadAsync(request, storage, progress, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<MediaImportResult> ImportViaBackendUploadAsync(
        MediaImportRequest request,
        Storage storage,
        IProgress<ImportProgressDto>? progress,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "{Prefix} Backend upload started. Folder={Folder}, StorageId={StorageId}",
            PhotoRegisterLogPrefix,
            request.SourceFolderPath,
            request.StorageId);

        var scannedFiles = await _fileScanner.ScanAsync(request.SourceFolderPath, cancellationToken);
        var itemResults = new List<MediaImportItemResult>(scannedFiles.Count);
        var importedCount = 0;
        var failedCount = 0;
        var anyCompleted = false;

        ReportProgress(
            progress,
            totalCount: scannedFiles.Count,
            processedCount: 0,
            importedCount,
            failedCount,
            currentFileName: null,
            currentStage: UploadJobStatusDto.Waiting,
            isCompleted: false,
            backendStatus: UploadJobStatusDto.Waiting,
            backendProgress: 0);

        foreach (var filePath in scannedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(filePath);
            MediaImportItemResult itemResult;
            try
            {
                var mediaType = _fileScanner.ResolveMediaType(filePath);
                var upload = await _uploadApiRepository.UploadAsync(filePath, cancellationToken)
                    .ConfigureAwait(false);
                var accepted = UploadStatusDto.FromResponse(upload);

                _logger.LogInformation(
                    "{Prefix} Backend upload accepted. File={File}, JobId={JobId}, Status={Status}",
                    PhotoRegisterLogPrefix,
                    fileName,
                    accepted.JobId,
                    accepted.Status);

                if (!Guid.TryParse(accepted.JobId, out var jobId))
                {
                    throw new InvalidOperationException(
                        $"Upload API returned an invalid job_id '{accepted.JobId}'.");
                }

                ReportProgress(
                    progress,
                    totalCount: scannedFiles.Count,
                    processedCount: itemResults.Count,
                    importedCount,
                    failedCount,
                    currentFileName: fileName,
                    currentStage: accepted.Status,
                    isCompleted: false,
                    backendStatus: accepted.Status,
                    backendProgress: 0,
                    jobId: accepted.JobId);

                var jobProgress = new Progress<UploadJobStatusDto>(job =>
                    ReportProgress(
                        progress,
                        totalCount: scannedFiles.Count,
                        processedCount: itemResults.Count,
                        importedCount,
                        failedCount,
                        currentFileName: fileName,
                        currentStage: job.Status,
                        isCompleted: false,
                        backendStatus: job.Status,
                        backendProgress: job.Progress,
                        currentPlugin: job.CurrentPlugin,
                        jobId: job.JobId,
                        lastError: job.LastError,
                        isFailed: job.IsFailed));

                var finalStatus = await _uploadMonitorService
                    .MonitorAsync(jobId, jobProgress, cancellationToken)
                    .ConfigureAwait(false);

                if (finalStatus.IsFailed)
                {
                    failedCount++;
                    itemResult = new MediaImportItemResult
                    {
                        OriginalPath = filePath,
                        FileName = fileName,
                        MediaType = mediaType,
                        Status = MediaStatus.Failed,
                        ContentHash = accepted.JobId,
                        RelativePath = upload.IncomingPath,
                        ErrorMessage = string.IsNullOrWhiteSpace(finalStatus.LastError)
                            ? $"Upload job FAILED (job_id={accepted.JobId})."
                            : finalStatus.LastError,
                    };

                    ReportProgress(
                        progress,
                        totalCount: scannedFiles.Count,
                        processedCount: itemResults.Count + 1,
                        importedCount,
                        failedCount,
                        currentFileName: fileName,
                        currentStage: UploadJobStatusDto.Failed,
                        isCompleted: false,
                        backendStatus: UploadJobStatusDto.Failed,
                        backendProgress: finalStatus.Progress,
                        currentPlugin: finalStatus.CurrentPlugin,
                        jobId: accepted.JobId,
                        lastError: itemResult.ErrorMessage,
                        isFailed: true);
                }
                else
                {
                    importedCount++;
                    anyCompleted = true;
                    itemResult = new MediaImportItemResult
                    {
                        OriginalPath = filePath,
                        FileName = fileName,
                        MediaType = mediaType,
                        Status = MediaStatus.Imported,
                        ContentHash = accepted.JobId,
                        RelativePath = upload.IncomingPath,
                        ErrorMessage = accepted.Message,
                    };

                    ReportProgress(
                        progress,
                        totalCount: scannedFiles.Count,
                        processedCount: itemResults.Count + 1,
                        importedCount,
                        failedCount,
                        currentFileName: fileName,
                        currentStage: UploadJobStatusDto.Completed,
                        isCompleted: false,
                        backendStatus: UploadJobStatusDto.Completed,
                        backendProgress: finalStatus.Progress,
                        currentPlugin: finalStatus.CurrentPlugin,
                        jobId: accepted.JobId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Prefix} Backend upload failed. File={File}", PhotoRegisterLogPrefix, fileName);
                failedCount++;
                itemResult = new MediaImportItemResult
                {
                    OriginalPath = filePath,
                    FileName = fileName,
                    MediaType = _fileScanner.ResolveMediaType(filePath),
                    Status = MediaStatus.Failed,
                    ErrorMessage = ex.Message,
                };

                ReportProgress(
                    progress,
                    totalCount: scannedFiles.Count,
                    processedCount: itemResults.Count + 1,
                    importedCount,
                    failedCount,
                    currentFileName: fileName,
                    currentStage: UploadJobStatusDto.Failed,
                    isCompleted: false,
                    backendStatus: UploadJobStatusDto.Failed,
                    lastError: ex.Message,
                    isFailed: true);
            }

            itemResults.Add(itemResult);
        }

        if (anyCompleted)
        {
            _catalogInvalidation.Invalidate(
                CatalogSurface.Gallery | CatalogSurface.Home | CatalogSurface.Visits | CatalogSurface.Pending);
            _logger.LogInformation(
                "{Prefix} Gallery Reload requested after COMPLETED upload job(s).",
                PhotoRegisterLogPrefix);
        }

        var result = new MediaImportResult
        {
            SourceFolderPath = request.SourceFolderPath,
            StorageId = storage.Id,
            ScannedCount = scannedFiles.Count,
            ImportedCount = importedCount,
            DuplicateCount = 0,
            FailedCount = failedCount,
            Items = itemResults,
        };

        ReportProgress(
            progress,
            totalCount: result.ScannedCount,
            processedCount: result.ScannedCount,
            importedCount: result.ImportedCount,
            failedCount: result.FailedCount,
            currentFileName: null,
            currentStage: result.FailedCount > 0 && result.ImportedCount == 0
                ? UploadJobStatusDto.Failed
                : UploadJobStatusDto.Completed,
            isCompleted: true,
            backendStatus: result.FailedCount > 0 && result.ImportedCount == 0
                ? UploadJobStatusDto.Failed
                : UploadJobStatusDto.Completed,
            backendProgress: 100,
            isFailed: result.FailedCount > 0 && result.ImportedCount == 0,
            lastError: result.Items.LastOrDefault(i => i.Status == MediaStatus.Failed)?.ErrorMessage);

        _logger.LogInformation(
            "{Prefix} Backend upload finished. Scanned={Scanned}, Uploaded={Uploaded}, Failed={Failed}",
            PhotoRegisterLogPrefix,
            result.ScannedCount,
            result.ImportedCount,
            result.FailedCount);

        return result;
    }

    private static void ReportProgress(
        IProgress<ImportProgressDto>? progress,
        int totalCount,
        int processedCount,
        int importedCount,
        int failedCount,
        string? currentFileName,
        string? currentStage,
        bool isCompleted,
        string? backendStatus = null,
        int? backendProgress = null,
        string? currentPlugin = null,
        string? jobId = null,
        string? lastError = null,
        bool isFailed = false)
    {
        progress?.Report(new ImportProgressDto
        {
            TotalCount = totalCount,
            ProcessedCount = processedCount,
            ImportedCount = importedCount,
            DuplicateCount = 0,
            FailedCount = failedCount,
            CurrentFileName = currentFileName,
            CurrentStage = currentStage,
            IsCompleted = isCompleted,
            BackendStatus = backendStatus,
            BackendProgress = backendProgress,
            CurrentPlugin = currentPlugin,
            JobId = jobId,
            LastError = lastError,
            IsFailed = isFailed,
        });
    }
}
