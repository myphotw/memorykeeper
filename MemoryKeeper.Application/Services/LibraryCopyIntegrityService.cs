using MemoryKeeper.Application.Diagnostics;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Application.Services;

/// <summary>
/// Scans MemoryKeeper library copies for hash duplicates and RelativePath mismatches (MK-042P).
/// </summary>
public sealed class LibraryCopyIntegrityService
{
    private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".heic", ".heif", ".webp",
        ".mp4", ".mov", ".m4v", ".avi"
    };

    private readonly IMediaRepository _mediaRepository;
    private readonly IStorageRepository _storageRepository;
    private readonly IFileAccessService _fileAccessService;
    private readonly IFileStorageService _fileStorageService;
    private readonly IFileHasher _fileHasher;
    private readonly ILogger<LibraryCopyIntegrityService> _logger;

    public LibraryCopyIntegrityService(
        IMediaRepository mediaRepository,
        IStorageRepository storageRepository,
        IFileAccessService fileAccessService,
        IFileStorageService fileStorageService,
        IFileHasher fileHasher,
        ILogger<LibraryCopyIntegrityService> logger)
    {
        _mediaRepository = mediaRepository;
        _storageRepository = storageRepository;
        _fileAccessService = fileAccessService;
        _fileStorageService = fileStorageService;
        _fileHasher = fileHasher;
        _logger = logger;
    }

    public Task<LibraryCopyIntegrityResultDto> InspectAsync(CancellationToken cancellationToken = default) =>
        RunAsync(repair: false, cancellationToken);

    public Task<LibraryCopyIntegrityResultDto> InspectAndRepairAsync(CancellationToken cancellationToken = default) =>
        RunAsync(repair: true, cancellationToken);

    private async Task<LibraryCopyIntegrityResultDto> RunAsync(bool repair, CancellationToken cancellationToken)
    {
        ImportPipelineLog.Write($"사본 무결성 검사 시작 Repair={repair}");

        var storages = await _storageRepository.GetAllAsync(cancellationToken);
        var activeStorages = storages
            .Where(storage => storage.IsActive && !string.IsNullOrWhiteSpace(storage.PhotoRoot))
            .Where(storage => _fileAccessService.PhotoRootExists(storage.PhotoRoot))
            .ToList();

        if (activeStorages.Count == 0)
        {
            return new LibraryCopyIntegrityResultDto
            {
                Succeeded = false,
                Message = "활성 MemoryKeeper 저장소를 찾을 수 없습니다.",
                RepairApplied = repair
            };
        }

        var allMedia = await _mediaRepository.GetAllAsync(cancellationToken);
        var mediaByStorage = allMedia.ToLookup(media => media.StorageId);

        var missingFiles = 0;
        var pathMismatches = 0;
        var duplicateGroups = 0;
        var orphanFiles = 0;
        var deletedDuplicates = 0;
        var repairedPaths = 0;
        var deletedEmptyFolders = 0;

        foreach (var storage in activeStorages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mediaItems = mediaByStorage[storage.Id].ToList();
            var claimedAbsolutePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var media in mediaItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = _fileAccessService.ToRelativePath(media.RelativePath, storage.PhotoRoot);
                var absolute = _fileAccessService.ResolveAbsolutePath(storage.PhotoRoot, relative);
                claimedAbsolutePaths.Add(Path.GetFullPath(absolute));

                if (!_fileAccessService.FileExists(absolute))
                {
                    missingFiles++;
                    ImportPipelineLog.Write($"MissingFile MediaId={media.Id} RelativePath={relative}");
                }
            }

            var filesOnDisk = EnumerateMediaFiles(storage.PhotoRoot).ToList();
            var hashToFiles = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var filePath in filesOnDisk)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var hash = await _fileHasher.ComputeSha256Async(filePath, cancellationToken);
                    if (!hashToFiles.TryGetValue(hash, out var list))
                    {
                        list = [];
                        hashToFiles[hash] = list;
                    }

                    list.Add(filePath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Integrity hash failed. Path={Path}", filePath);
                }
            }

            foreach (var (hash, files) in hashToFiles)
            {
                if (files.Count <= 1)
                {
                    continue;
                }

                duplicateGroups++;
                ImportPipelineLog.Write($"Duplicate Hash={hash} Count={files.Count}");

                if (!repair)
                {
                    continue;
                }

                var mediaForHash = mediaItems
                    .Where(media => string.Equals(media.ContentHash, hash, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var preferred = ChooseCanonicalFile(files, mediaForHash, storage.PhotoRoot);
                foreach (var duplicate in files.Where(path =>
                             !string.Equals(path, preferred, StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        File.Delete(duplicate);
                        deletedDuplicates++;
                        ImportPipelineLog.Write($"DeletedDuplicate {duplicate}");
                        var parent = Path.GetDirectoryName(duplicate);
                        var before = CountEmptyCapable(parent);
                        _fileStorageService.DeleteEmptyDirectoriesUpward(parent, storage.PhotoRoot);
                        deletedEmptyFolders += Math.Max(0, before);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed deleting duplicate copy. Path={Path}", duplicate);
                    }
                }

                if (mediaForHash.Count > 0 && preferred is not null)
                {
                    var newRelative = _fileAccessService.ToRelativePath(
                        Path.GetRelativePath(storage.PhotoRoot, preferred),
                        storage.PhotoRoot);

                    foreach (var media in mediaForHash)
                    {
                        var currentRelative = _fileAccessService.ToRelativePath(media.RelativePath, storage.PhotoRoot);
                        if (string.Equals(currentRelative, newRelative, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        media.RelativePath = newRelative;
                        media.UpdatedAt = DateTime.UtcNow;
                        await _mediaRepository.UpdateAsync(media, cancellationToken);
                        repairedPaths++;
                        pathMismatches++;
                        ImportPipelineLog.Write(
                            $"RelativePath 복구 MediaId={media.Id} Old={currentRelative} New={newRelative}");
                    }
                }
            }

            // Orphans: on-disk files not referenced by any RelativePath.
            foreach (var filePath in filesOnDisk)
            {
                var full = Path.GetFullPath(filePath);
                if (claimedAbsolutePaths.Contains(full))
                {
                    continue;
                }

                // After repair, preferred files may have become the new claimed path — re-check existence.
                if (!File.Exists(full))
                {
                    continue;
                }

                // If this file's hash matches a media row, it was handled as duplicate (deleted or kept).
                // Remaining orphans: no DB hash match.
                string? hash = null;
                try
                {
                    hash = await _fileHasher.ComputeSha256Async(full, cancellationToken);
                }
                catch
                {
                    continue;
                }

                var hasMedia = mediaItems.Any(media =>
                    string.Equals(media.ContentHash, hash, StringComparison.OrdinalIgnoreCase));
                if (hasMedia)
                {
                    continue;
                }

                orphanFiles++;
                ImportPipelineLog.Write($"OrphanFile {full}");
            }

            // Path mismatch: DB RelativePath file missing but another file with same hash exists.
            foreach (var media in mediaItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = _fileAccessService.ToRelativePath(media.RelativePath, storage.PhotoRoot);
                var absolute = _fileAccessService.ResolveAbsolutePath(storage.PhotoRoot, relative);
                if (_fileAccessService.FileExists(absolute))
                {
                    continue;
                }

                pathMismatches++;
                if (!repair || string.IsNullOrWhiteSpace(media.ContentHash))
                {
                    continue;
                }

                if (!hashToFiles.TryGetValue(media.ContentHash, out var candidates) || candidates.Count == 0)
                {
                    continue;
                }

                var surviving = candidates.FirstOrDefault(File.Exists);
                if (surviving is null)
                {
                    continue;
                }

                var newRelative = _fileAccessService.ToRelativePath(
                    Path.GetRelativePath(storage.PhotoRoot, surviving),
                    storage.PhotoRoot);
                media.RelativePath = newRelative;
                media.UpdatedAt = DateTime.UtcNow;
                await _mediaRepository.UpdateAsync(media, cancellationToken);
                repairedPaths++;
                ImportPipelineLog.Write(
                    $"RelativePath 자동복구 MediaId={media.Id} New={newRelative}");
            }
        }

        var message = repair
            ? $"사본 무결성 검사·복구 완료. 미디어 {allMedia.Count}건, 중복그룹 {duplicateGroups}, 삭제 {deletedDuplicates}, 경로복구 {repairedPaths}, 누락 {missingFiles}, 고아파일 {orphanFiles}."
            : $"사본 무결성 검사 완료. 미디어 {allMedia.Count}건, 중복그룹 {duplicateGroups}, 경로불일치 {pathMismatches}, 누락 {missingFiles}, 고아파일 {orphanFiles}.";

        ImportPipelineLog.Write(message);
        _logger.LogInformation("{Message}", message);

        return new LibraryCopyIntegrityResultDto
        {
            Succeeded = true,
            Message = message,
            MediaChecked = allMedia.Count,
            MissingFiles = missingFiles,
            PathMismatches = pathMismatches,
            DuplicateFileGroups = duplicateGroups,
            OrphanFiles = orphanFiles,
            DeletedDuplicateFiles = deletedDuplicates,
            RepairedRelativePaths = repairedPaths,
            DeletedEmptyFolders = deletedEmptyFolders,
            RepairApplied = repair
        };
    }

    private string? ChooseCanonicalFile(
        IReadOnlyList<string> files,
        IReadOnlyList<Media> mediaForHash,
        string photoRoot)
    {
        foreach (var media in mediaForHash)
        {
            var relative = _fileAccessService.ToRelativePath(media.RelativePath, photoRoot);
            var absolute = Path.GetFullPath(_fileAccessService.ResolveAbsolutePath(photoRoot, relative));
            var match = files.FirstOrDefault(path =>
                string.Equals(Path.GetFullPath(path), absolute, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        // Prefer classified path over pending folder.
        return files
            .OrderBy(path => path.Contains(FileStorageServicePendingMarker, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(path => path.Length)
            .FirstOrDefault();
    }

    // Avoid referencing Infrastructure constant from Application — mirror folder name.
    private const string FileStorageServicePendingMarker = "미완성 추억";

    private static IEnumerable<string> EnumerateMediaFiles(string photoRoot)
    {
        if (!Directory.Exists(photoRoot))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(photoRoot, "*", SearchOption.AllDirectories))
        {
            if (MediaExtensions.Contains(Path.GetExtension(file)))
            {
                yield return file;
            }
        }
    }

    private static int CountEmptyCapable(string? directoryPath) =>
        string.IsNullOrWhiteSpace(directoryPath) ? 0 : 1;
}
