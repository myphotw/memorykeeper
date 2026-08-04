using MemoryKeeper.Application.Diagnostics;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.DTOs.Upload;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Options;
using MemoryKeeper.Application.Time;
using MemoryKeeper.Domain.Entities;
using MemoryKeeper.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemoryKeeper.Application.Services;

public sealed class MediaImportService
{
    private const string PhotoRegisterLogPrefix = "[Photo Register]";

    private readonly IFileScanner _fileScanner;
    private readonly IMetadataExtractor _metadataExtractor;
    private readonly IFileHasher _fileHasher;
    private readonly IFileStorageService _fileStorageService;
    private readonly IFileAccessService _fileAccessService;
    private readonly IMediaRepository _mediaRepository;
    private readonly IStorageRepository _storageRepository;
    private readonly PlaceAssignmentService _placeAssignmentService;
    private readonly IMediaLibraryPathSyncService _pathSyncService;
    private readonly IUploadApiRepository? _uploadApiRepository;
    private readonly IOptionsMonitor<BackendUploadOptions>? _backendUploadOptions;
    private readonly ILogger<MediaImportService> _logger;

    public MediaImportService(
        IFileScanner fileScanner,
        IMetadataExtractor metadataExtractor,
        IFileHasher fileHasher,
        IFileStorageService fileStorageService,
        IFileAccessService fileAccessService,
        IMediaRepository mediaRepository,
        IStorageRepository storageRepository,
        PlaceAssignmentService placeAssignmentService,
        IMediaLibraryPathSyncService pathSyncService,
        ILogger<MediaImportService> logger,
        IUploadApiRepository? uploadApiRepository = null,
        IOptionsMonitor<BackendUploadOptions>? backendUploadOptions = null)
    {
        _fileScanner = fileScanner;
        _metadataExtractor = metadataExtractor;
        _fileHasher = fileHasher;
        _fileStorageService = fileStorageService;
        _fileAccessService = fileAccessService;
        _mediaRepository = mediaRepository;
        _storageRepository = storageRepository;
        _placeAssignmentService = placeAssignmentService;
        _pathSyncService = pathSyncService;
        _logger = logger;
        _uploadApiRepository = uploadApiRepository;
        _backendUploadOptions = backendUploadOptions;
    }

    private bool UseBackendUpload =>
        _backendUploadOptions?.CurrentValue.UseBackendUpload == true
        && _uploadApiRepository is not null;

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

        if (UseBackendUpload)
        {
            return await ImportViaBackendUploadAsync(request, storage, progress, cancellationToken)
                .ConfigureAwait(false);
        }

        return await ImportViaSqliteAsync(request, storage, progress, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<MediaImportResult> ImportViaBackendUploadAsync(
        MediaImportRequest request,
        Storage storage,
        IProgress<ImportProgressDto>? progress,
        CancellationToken cancellationToken)
    {
        var uploadApi = _uploadApiRepository
            ?? throw new InvalidOperationException("IUploadApiRepository is not registered.");

        _logger.LogInformation(
            "{Prefix} Backend upload started. Folder={Folder}, StorageId={StorageId}",
            PhotoRegisterLogPrefix,
            request.SourceFolderPath,
            request.StorageId);

        var scannedFiles = await _fileScanner.ScanAsync(request.SourceFolderPath, cancellationToken);
        var itemResults = new List<MediaImportItemResult>(scannedFiles.Count);
        var importedCount = 0;
        var failedCount = 0;

        ReportProgress(
            progress,
            totalCount: scannedFiles.Count,
            processedCount: 0,
            importedCount,
            duplicateCount: 0,
            failedCount,
            currentFileName: null,
            currentStage: "Backend 업로드 중...",
            isCompleted: false);

        foreach (var filePath in scannedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(filePath);
            MediaImportItemResult itemResult;
            try
            {
                var mediaType = _fileScanner.ResolveMediaType(filePath);
                var upload = await uploadApi.UploadAsync(filePath, cancellationToken).ConfigureAwait(false);
                var status = UploadStatusDto.FromResponse(upload);

                _logger.LogInformation(
                    "{Prefix} Backend upload ok. File={File}, JobId={JobId}, Status={Status}",
                    PhotoRegisterLogPrefix,
                    fileName,
                    status.JobId,
                    status.Status);

                itemResult = new MediaImportItemResult
                {
                    OriginalPath = filePath,
                    FileName = fileName,
                    MediaType = mediaType,
                    Status = MediaStatus.Imported,
                    MediaId = null,
                    ContentHash = status.JobId,
                    RelativePath = upload.IncomingPath,
                    ErrorMessage = status.Message,
                };
                importedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Prefix} Backend upload failed. File={File}", PhotoRegisterLogPrefix, fileName);
                itemResult = new MediaImportItemResult
                {
                    OriginalPath = filePath,
                    FileName = fileName,
                    MediaType = _fileScanner.ResolveMediaType(filePath),
                    Status = MediaStatus.Failed,
                    ErrorMessage = ex.Message,
                };
                failedCount++;
            }

            itemResults.Add(itemResult);
            ReportProgress(
                progress,
                totalCount: scannedFiles.Count,
                processedCount: itemResults.Count,
                importedCount,
                duplicateCount: 0,
                failedCount,
                currentFileName: itemResult.FileName,
                currentStage: itemResult.Status == MediaStatus.Failed ? "업로드 오류" : "Backend 업로드",
                isCompleted: false);
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
            duplicateCount: 0,
            failedCount: result.FailedCount,
            currentFileName: null,
            currentStage: "완료",
            isCompleted: true);

        _logger.LogInformation(
            "{Prefix} Backend upload finished. Scanned={Scanned}, Uploaded={Uploaded}, Failed={Failed}",
            PhotoRegisterLogPrefix,
            result.ScannedCount,
            result.ImportedCount,
            result.FailedCount);

        return result;
    }

    private async Task<MediaImportResult> ImportViaSqliteAsync(
        MediaImportRequest request,
        Storage storage,
        IProgress<ImportProgressDto>? progress,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "{Prefix} Started. Folder={Folder}, StorageId={StorageId}",
            PhotoRegisterLogPrefix,
            request.SourceFolderPath,
            request.StorageId);

        var scannedFiles = await _fileScanner.ScanAsync(request.SourceFolderPath, cancellationToken);
        var itemResults = new List<MediaImportItemResult>(scannedFiles.Count);
        var importedCount = 0;
        var duplicateCount = 0;
        var failedCount = 0;

        ReportProgress(
            progress,
            totalCount: scannedFiles.Count,
            processedCount: 0,
            importedCount,
            duplicateCount,
            failedCount,
            currentFileName: null,
            currentStage: "사진 분석 중...",
            isCompleted: false);

        foreach (var filePath in scannedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var itemResult = await ImportFileAsync(filePath, storage, cancellationToken);
            itemResults.Add(itemResult);

            switch (itemResult.Status)
            {
                case MediaStatus.Imported:
                case MediaStatus.Pending:
                    importedCount++;
                    break;
                case MediaStatus.Duplicate:
                    duplicateCount++;
                    break;
                case MediaStatus.Failed:
                    failedCount++;
                    break;
            }

            ReportProgress(
                progress,
                totalCount: scannedFiles.Count,
                processedCount: itemResults.Count,
                importedCount,
                duplicateCount,
                failedCount,
                currentFileName: itemResult.FileName,
                currentStage: ResolveStageLabel(itemResult),
                isCompleted: false);
        }

        var result = new MediaImportResult
        {
            SourceFolderPath = request.SourceFolderPath,
            StorageId = request.StorageId,
            ScannedCount = scannedFiles.Count,
            ImportedCount = importedCount,
            DuplicateCount = duplicateCount,
            FailedCount = failedCount,
            Items = itemResults
        };

        ReportProgress(
            progress,
            totalCount: result.ScannedCount,
            processedCount: result.ScannedCount,
            importedCount: result.ImportedCount,
            duplicateCount: result.DuplicateCount,
            failedCount: result.FailedCount,
            currentFileName: null,
            currentStage: "완료",
            isCompleted: true);

        _logger.LogInformation(
            "{Prefix} Finished. Scanned={Scanned}, Registered={Registered}, Duplicate={Duplicate}, Failed={Failed}",
            PhotoRegisterLogPrefix,
            result.ScannedCount,
            result.ImportedCount,
            result.DuplicateCount,
            result.FailedCount);

        return result;
    }

    private static void ReportProgress(
        IProgress<ImportProgressDto>? progress,
        int totalCount,
        int processedCount,
        int importedCount,
        int duplicateCount,
        int failedCount,
        string? currentFileName,
        string? currentStage,
        bool isCompleted)
    {
        progress?.Report(new ImportProgressDto
        {
            TotalCount = totalCount,
            ProcessedCount = processedCount,
            ImportedCount = importedCount,
            DuplicateCount = duplicateCount,
            FailedCount = failedCount,
            CurrentFileName = currentFileName,
            CurrentStage = currentStage,
            IsCompleted = isCompleted
        });
    }

    private static string ResolveStageLabel(MediaImportItemResult itemResult)
    {
        return itemResult.Status switch
        {
            MediaStatus.Duplicate => "중복 확인",
            MediaStatus.Failed => "오류 처리",
            MediaStatus.Pending => "GPS/장소 분석",
            _ => "DB 저장"
        };
    }

    private async Task<MediaImportItemResult> ImportFileAsync(
        string filePath,
        Storage storage,
        CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(filePath);
        string? relativePath = null;

        try
        {
            var mediaType = _fileScanner.ResolveMediaType(filePath)
                ?? throw new InvalidOperationException($"Unsupported media type for '{filePath}'.");

            var metadata = await _metadataExtractor.ExtractAsync(filePath, cancellationToken);
            _logger.LogInformation(
                "{Prefix} EXIF Loaded. Path={Path}, CapturedAt={CapturedAt}, DateSource={DateSource}, DateTimeOriginal={DateTimeOriginal}",
                PhotoRegisterLogPrefix,
                filePath,
                metadata.CapturedAt,
                metadata.CaptureDateSource,
                metadata.DateTimeOriginal);
            _logger.LogInformation(
                "{Prefix} Date Resolved. Path={Path}, Source={Source}, CapturedAt={CapturedAt}",
                PhotoRegisterLogPrefix,
                filePath,
                metadata.CaptureDateSource,
                metadata.CapturedAt);

            if (metadata.Latitude is double latitude && metadata.Longitude is double longitude)
            {
                _logger.LogInformation(
                    "{Prefix} GPS Parsed. Path={Path}, Latitude={Latitude}, Longitude={Longitude}, Format={Format}",
                    PhotoRegisterLogPrefix,
                    filePath,
                    latitude,
                    longitude,
                    metadata.GpsFormat);
                _logger.LogInformation(
                    "[STEP1] 파일={FileName} GPS Lat={Latitude} Lng={Longitude}",
                    fileName,
                    latitude,
                    longitude);
            }
            else
            {
                _logger.LogInformation(
                    "{Prefix} GPS Parsed. Path={Path}, HasGps=False, Format={Format}",
                    PhotoRegisterLogPrefix,
                    filePath,
                    metadata.GpsFormat);
                _logger.LogInformation(
                    "[STEP1] 파일={FileName} GPS=없음 → 미완성 추억 경로로 등록",
                    fileName);
            }

            var contentHash = await _fileHasher.ComputeSha256Async(filePath, cancellationToken);
            ImportPipelineLog.Write($"Hash {contentHash}");

            var existing = await _mediaRepository.GetByContentHashAsync(contentHash, cancellationToken);
            if (existing is not null)
            {
                ImportPipelineLog.Write("Media 발견 YES");
                var reusedPath = await ReuseExistingLibraryCopyAsync(
                    existing,
                    storage,
                    filePath,
                    fileName,
                    metadata,
                    contentHash,
                    cancellationToken);

                ImportPipelineLog.Write("Copy Skip");
                ImportPipelineLog.Write("Reuse Existing File");
                ImportPipelineLog.Write("Duplicate Skip");

                _logger.LogInformation(
                    "{Prefix} Duplicate skipped — reuse existing copy. Path={Path}, Hash={Hash}, RelativePath={RelativePath}",
                    PhotoRegisterLogPrefix,
                    filePath,
                    contentHash,
                    reusedPath);

                return new MediaImportItemResult
                {
                    OriginalPath = filePath,
                    FileName = fileName,
                    MediaType = mediaType,
                    Status = MediaStatus.Duplicate,
                    MediaId = existing.Id,
                    ContentHash = contentHash,
                    RelativePath = reusedPath
                };
            }

            ImportPipelineLog.Write("Media 발견 NO");
            ImportPipelineLog.Write("Copy 수행");

            var libraryRelativePath = _fileStorageService.BuildLibraryRelativePath(metadata.CapturedAt, fileName);
            _logger.LogInformation(
                "{Prefix} RelativePath Created. Path={Path}, RelativePath={RelativePath}",
                PhotoRegisterLogPrefix,
                filePath,
                libraryRelativePath);

            var destinationDirectory = Path.GetDirectoryName(
                Path.Combine(storage.PhotoRoot, libraryRelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
                _logger.LogInformation(
                    "{Prefix} Directory Created. Path={DirectoryPath}, Exists={Exists}",
                    PhotoRegisterLogPrefix,
                    destinationDirectory,
                    Directory.Exists(destinationDirectory));
            }

            libraryRelativePath = await _fileStorageService.CopyToLibraryAsync(
                filePath,
                storage.PhotoRoot,
                libraryRelativePath,
                cancellationToken);
            relativePath = libraryRelativePath;
            ImportPipelineLog.Write($"Copy Success RelativePath={libraryRelativePath}");
            _logger.LogInformation(
                "{Prefix} Copy Success. Path={Path}, RelativePath={RelativePath}",
                PhotoRegisterLogPrefix,
                filePath,
                libraryRelativePath);

            Guid? placeId = null;
            Place? matchedPlace = null;
            if (metadata.Latitude is double lat && metadata.Longitude is double lon)
            {
                try
                {
                    matchedPlace = await _placeAssignmentService.AssignAsync(lat, lon, cancellationToken);
                    placeId = matchedPlace.Id;
                    ImportPipelineLog.Write($"TB_MEDIA.PlaceID={placeId}");
                    _logger.LogInformation(
                        "[STEP6] 사진과 장소 연결 MediaId pending PlaceId={PlaceId}, Canonical={Canonical}",
                        placeId,
                        matchedPlace.CanonicalName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "[STEP2] Place 배정 실패 Path={Path}, Message={Message}",
                        filePath,
                        ex.Message);
                    ImportPipelineLog.Write($"Place 배정 예외 Message={ex.Message}");
                    throw;
                }
            }

            var status = placeId.HasValue ? MediaStatus.Imported : MediaStatus.Pending;

            var now = DateTime.UtcNow;
            var media = new Media
            {
                Id = Guid.NewGuid(),
                FileName = fileName,
                MediaType = metadata.MediaType == default ? mediaType : metadata.MediaType,
                Status = status,
                OriginalPath = filePath,
                RelativePath = libraryRelativePath,
                ContentHash = contentHash,
                CapturedAt = DateTimeHelper.ToUtc(metadata.CapturedAt),
                DateTimeOriginal = metadata.DateTimeOriginal,
                ImportedAt = DateTimeHelper.UtcNow,
                Latitude = metadata.Latitude,
                Longitude = metadata.Longitude,
                Altitude = metadata.Altitude,
                Orientation = metadata.Orientation,
                Width = metadata.Width,
                Height = metadata.Height,
                CameraMaker = metadata.CameraMaker,
                CameraModel = metadata.CameraModel,
                Lens = metadata.Lens,
                Iso = metadata.Iso,
                Exposure = metadata.Exposure,
                FNumber = metadata.FNumber,
                FocalLength = metadata.FocalLength,
                StorageId = storage.Id,
                PlaceId = placeId,
                CreatedAt = now,
                UpdatedAt = now
            };

            await _mediaRepository.AddAsync(media, cancellationToken);
            if (matchedPlace is not null)
            {
                _logger.LogInformation(
                    "[STEP6] 사진과 장소 연결 성공 MediaId={MediaId}, PlaceId={PlaceId}, DisplayName={DisplayName}",
                    media.Id,
                    matchedPlace.Id,
                    matchedPlace.DisplayName);

                var previousRelativePath = media.RelativePath;
                await _pathSyncService.SyncMediaPathAsync(media, matchedPlace, cancellationToken);
                libraryRelativePath = media.RelativePath;
                relativePath = libraryRelativePath;
                ImportPipelineLog.Write($"OldPath {previousRelativePath}");
                ImportPipelineLog.Write($"NewPath {media.RelativePath}");
                ImportPipelineLog.Write("Move Success");
                _logger.LogInformation(
                    "[STEP7] 저장 폴더 동기화 From={From} To={To}",
                    previousRelativePath,
                    media.RelativePath);
            }

            _logger.LogInformation(
                "{Prefix} DB Save Success. MediaId={MediaId}, RelativePath={RelativePath}, Status={Status}",
                PhotoRegisterLogPrefix,
                media.Id,
                libraryRelativePath,
                status);

            _logger.LogInformation(
                "{Prefix} Gallery Refresh. MediaId={MediaId}, RelativePath={RelativePath}",
                PhotoRegisterLogPrefix,
                media.Id,
                libraryRelativePath);
            _logger.LogInformation(
                "{Prefix} Home Refresh. MediaId={MediaId}, Status={Status}, Pending={Pending}",
                PhotoRegisterLogPrefix,
                media.Id,
                status,
                status == MediaStatus.Pending);
            if (placeId.HasValue)
            {
                _logger.LogInformation(
                    "{Prefix} VisitMap Refresh. MediaId={MediaId}, PlaceId={PlaceId}",
                    PhotoRegisterLogPrefix,
                    media.Id,
                    placeId);
            }

            return new MediaImportItemResult
            {
                OriginalPath = filePath,
                FileName = fileName,
                MediaType = media.MediaType,
                Status = status,
                MediaId = media.Id,
                ContentHash = contentHash,
                RelativePath = libraryRelativePath
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "{Prefix} Failed. Path={Path}, FileName={FileName}, Message={Message}, InnerException={InnerException}, StackTrace={StackTrace}, RelativePath={RelativePath}",
                PhotoRegisterLogPrefix,
                filePath,
                fileName,
                ex.Message,
                ex.InnerException?.Message,
                ex.StackTrace,
                relativePath);
            return new MediaImportItemResult
            {
                OriginalPath = filePath,
                FileName = fileName,
                MediaType = _fileScanner.ResolveMediaType(filePath),
                Status = MediaStatus.Failed,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// MK-042P: same hash → never create a second library copy; update metadata only.
    /// Restores the library file once if RelativePath is missing on disk.
    /// </summary>
    private async Task<string> ReuseExistingLibraryCopyAsync(
        Media existing,
        Storage storage,
        string sourceFilePath,
        string fileName,
        MediaMetadataDto metadata,
        string contentHash,
        CancellationToken cancellationToken)
    {
        var relative = _fileAccessService.ToRelativePath(existing.RelativePath, storage.PhotoRoot);
        var absolute = _fileAccessService.ResolveAbsolutePath(storage.PhotoRoot, relative);
        var copyPerformed = false;

        if (!_fileAccessService.FileExists(absolute))
        {
            ImportPipelineLog.Write("사본 없음 → 복구 Copy 1회");
            _logger.LogWarning(
                "{Prefix} Existing media missing library file — restoring once. MediaId={MediaId}, Path={Path}",
                PhotoRegisterLogPrefix,
                existing.Id,
                absolute);

            var restoreRelative = string.IsNullOrWhiteSpace(relative)
                ? _fileStorageService.BuildLibraryRelativePath(metadata.CapturedAt, fileName)
                : relative;
            relative = await _fileStorageService.CopyToLibraryAsync(
                sourceFilePath,
                storage.PhotoRoot,
                restoreRelative,
                cancellationToken);
            existing.RelativePath = relative;
            copyPerformed = true;
        }
        else
        {
            ImportPipelineLog.Write($"사본 존재 RelativePath={relative}");
        }

        existing.OriginalPath = sourceFilePath;
        existing.FileName = string.IsNullOrWhiteSpace(existing.FileName) ? fileName : existing.FileName;
        existing.CapturedAt = DateTimeHelper.ToUtc(metadata.CapturedAt) ?? existing.CapturedAt;
        existing.DateTimeOriginal = metadata.DateTimeOriginal ?? existing.DateTimeOriginal;
        existing.Latitude = metadata.Latitude ?? existing.Latitude;
        existing.Longitude = metadata.Longitude ?? existing.Longitude;
        existing.Altitude = metadata.Altitude ?? existing.Altitude;
        existing.Orientation = metadata.Orientation ?? existing.Orientation;
        existing.Width = metadata.Width ?? existing.Width;
        existing.Height = metadata.Height ?? existing.Height;
        existing.CameraMaker = metadata.CameraMaker ?? existing.CameraMaker;
        existing.CameraModel = metadata.CameraModel ?? existing.CameraModel;
        existing.Lens = metadata.Lens ?? existing.Lens;
        existing.Iso = metadata.Iso ?? existing.Iso;
        existing.Exposure = metadata.Exposure ?? existing.Exposure;
        existing.FNumber = metadata.FNumber ?? existing.FNumber;
        existing.FocalLength = metadata.FocalLength ?? existing.FocalLength;
        existing.ContentHash = contentHash;
        existing.UpdatedAt = DateTime.UtcNow;

        await _mediaRepository.UpdateAsync(existing, cancellationToken);
        ImportPipelineLog.Write(copyPerformed
            ? $"Metadata Update + Restore Copy MediaId={existing.Id}"
            : $"Metadata Update MediaId={existing.Id}");

        return existing.RelativePath;
    }
}
