using MemoryKeeper.Application.Diagnostics;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Application.Services;

public sealed class MediaLibraryPathSyncService : IMediaLibraryPathSyncService
{
    private readonly IMediaRepository _mediaRepository;
    private readonly IPlaceRepository _placeRepository;
    private readonly IStorageRepository _storageRepository;
    private readonly IFileStorageService _fileStorageService;
    private readonly IFileAccessService _fileAccessService;
    private readonly ILogger<MediaLibraryPathSyncService> _logger;

    public MediaLibraryPathSyncService(
        IMediaRepository mediaRepository,
        IPlaceRepository placeRepository,
        IStorageRepository storageRepository,
        IFileStorageService fileStorageService,
        IFileAccessService fileAccessService,
        ILogger<MediaLibraryPathSyncService> logger)
    {
        _mediaRepository = mediaRepository;
        _placeRepository = placeRepository;
        _storageRepository = storageRepository;
        _fileStorageService = fileStorageService;
        _fileAccessService = fileAccessService;
        _logger = logger;
    }

    public async Task<bool> SyncMediaPathAsync(
        Media media,
        Place? place,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(media);

        var storage = await _storageRepository.GetByIdAsync(media.StorageId, cancellationToken);
        if (storage is null || string.IsNullOrWhiteSpace(storage.PhotoRoot))
        {
            _logger.LogWarning(
                "Skip path sync — storage missing. MediaId={MediaId}, StorageId={StorageId}",
                media.Id,
                media.StorageId);
            return false;
        }

        if (!_fileAccessService.PhotoRootExists(storage.PhotoRoot))
        {
            _logger.LogWarning(
                "Skip path sync — PhotoRoot unreachable. MediaId={MediaId}, PhotoRoot={PhotoRoot}",
                media.Id,
                storage.PhotoRoot);
            return false;
        }

        var fileName = string.IsNullOrWhiteSpace(media.FileName)
            ? Path.GetFileName(media.RelativePath)
            : media.FileName;
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var capturedAt = media.CapturedAt.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(media.CapturedAt.Value, DateTimeKind.Utc))
            : (DateTimeOffset?)null;

        var targetRelative = place is null
            ? _fileStorageService.BuildLibraryRelativePath(capturedAt, fileName)
            : _fileStorageService.BuildClassifiedRelativePath(capturedAt, place.DisplayName, fileName);

        var currentRelative = _fileAccessService.ToRelativePath(media.RelativePath, storage.PhotoRoot);
        if (string.Equals(currentRelative, targetRelative, StringComparison.OrdinalIgnoreCase))
        {
            // Still verify DB path matches a real file (MK-042P §8).
            var currentAbsolute = _fileAccessService.ResolveAbsolutePath(storage.PhotoRoot, currentRelative);
            if (!_fileAccessService.FileExists(currentAbsolute))
            {
                _logger.LogWarning(
                    "RelativePath matches target but file missing. MediaId={MediaId}, Path={Path}",
                    media.Id,
                    currentAbsolute);
            }

            return false;
        }

        var sourceAbsolute = _fileAccessService.ResolveAbsolutePath(storage.PhotoRoot, currentRelative);
        var targetAbsolute = _fileAccessService.ResolveAbsolutePath(storage.PhotoRoot, targetRelative);

        if (!_fileAccessService.FileExists(sourceAbsolute))
        {
            // Auto-repair: file already at target (orphan move / previous Copy leftover).
            if (_fileAccessService.FileExists(targetAbsolute))
            {
                ImportPipelineLog.Write($"OldPath {currentRelative}");
                ImportPipelineLog.Write($"NewPath {targetRelative}");
                ImportPipelineLog.Write("DB RelativePath 복구 (파일은 목적 경로에 존재)");
                media.RelativePath = targetRelative;
                media.UpdatedAt = DateTime.UtcNow;
                await _mediaRepository.UpdateAsync(media, cancellationToken);
                _fileStorageService.DeleteEmptyDirectoriesUpward(
                    Path.GetDirectoryName(sourceAbsolute),
                    storage.PhotoRoot);
                return true;
            }

            _logger.LogWarning(
                "Skip path sync — source file missing. MediaId={MediaId}, Path={Path}",
                media.Id,
                sourceAbsolute);
            ImportPipelineLog.Write($"Move Skip source missing MediaId={media.Id} Path={currentRelative}");
            return false;
        }

        ImportPipelineLog.Write($"OldPath {currentRelative}");
        ImportPipelineLog.Write($"NewPath {targetRelative}");
        ImportPipelineLog.Write("Move 수행");

        var finalRelative = await _fileStorageService.MoveWithinLibraryAsync(
            storage.PhotoRoot,
            currentRelative,
            targetRelative,
            cancellationToken);

        media.RelativePath = finalRelative;
        media.UpdatedAt = DateTime.UtcNow;
        await _mediaRepository.UpdateAsync(media, cancellationToken);

        ImportPipelineLog.Write("Move Success");
        _logger.LogInformation(
            "Library path synced. MediaId={MediaId}, From={From}, To={To}",
            media.Id,
            currentRelative,
            finalRelative);

        return true;
    }

    public async Task<int> SyncAllAsync(CancellationToken cancellationToken = default)
    {
        var allMedia = await _mediaRepository.GetAllAsync(cancellationToken);
        if (allMedia.Count == 0)
        {
            return 0;
        }

        var placeIds = allMedia
            .Where(media => media.PlaceId.HasValue)
            .Select(media => media.PlaceId!.Value)
            .Distinct()
            .ToList();
        var places = placeIds.Count == 0
            ? []
            : await _placeRepository.GetByIdsAsync(placeIds, cancellationToken);
        var placesById = places.ToDictionary(place => place.Id);

        var moved = 0;
        foreach (var media in allMedia)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Place? place = null;
            if (media.PlaceId is Guid placeId)
            {
                placesById.TryGetValue(placeId, out place);
            }

            if (await SyncMediaPathAsync(media, place, cancellationToken))
            {
                moved++;
            }
        }

        _logger.LogInformation("Library path sync-all finished. Moved={Moved}, Total={Total}", moved, allMedia.Count);
        return moved;
    }

    public async Task<int> SyncPlaceMediaAsync(Guid placeId, CancellationToken cancellationToken = default)
    {
        var place = await _placeRepository.GetByIdAsync(placeId, cancellationToken);
        if (place is null)
        {
            return 0;
        }

        var mediaItems = await _mediaRepository.GetByPlaceIdAsync(placeId, cancellationToken);
        var moved = 0;
        foreach (var media in mediaItems)
        {
            if (await SyncMediaPathAsync(media, place, cancellationToken))
            {
                moved++;
            }
        }

        return moved;
    }
}
