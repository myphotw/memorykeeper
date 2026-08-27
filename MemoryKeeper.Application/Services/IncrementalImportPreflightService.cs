using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;

namespace MemoryKeeper.Application.Services;

public sealed class IncrementalImportPreflightService
{
    private readonly IFileScanner _fileScanner;
    private readonly IImportFileIdentityStore _identityStore;
    private readonly IImportBackendIdentityProvider _backendIdentityProvider;

    public IncrementalImportPreflightService(
        IFileScanner fileScanner,
        IImportFileIdentityStore identityStore,
        IImportBackendIdentityProvider backendIdentityProvider)
    {
        _fileScanner = fileScanner;
        _identityStore = identityStore;
        _backendIdentityProvider = backendIdentityProvider;
    }

    public async Task<IncrementalImportPreflightResult> InspectAsync(
        string sourceFolderPath,
        IProgress<ImportPreflightProgressDto>? progress = null,
        bool forceRecheck = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFolderPath);
        progress?.Report(new ImportPreflightProgressDto { Stage = "사진 폴더 확인 중..." });
        var files = await _fileScanner.ScanAsync(sourceFolderPath, cancellationToken).ConfigureAwait(false);
        progress?.Report(new ImportPreflightProgressDto
        {
            TotalCount = files.Count,
            Stage = "사진 내용과 기존 등록 상태 확인 중...",
        });

        var identityTask = _identityStore.ResolveAsync(files, progress, forceRecheck, cancellationToken);
        var backendTask = _backendIdentityProvider.LoadAsync(cancellationToken);
        await Task.WhenAll(identityTask, backendTask).ConfigureAwait(false);
        var identities = await identityTask.ConfigureAwait(false);
        var backend = await backendTask.ConfigureAwait(false);
        var seenNewHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<IncrementalImportItemDto>(identities.Count);

        foreach (var identity in identities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalizedPath = NormalizePath(identity.FilePath);
            var hash = identity.ContentHash;
            IncrementalImportClassification classification;
            string? reason = null;
            string? jobId = null;

            if (!ImportBackendIdentityProvider.IsSha256(hash))
            {
                classification = IncrementalImportClassification.Uncertain;
                reason = identity.ErrorMessage ?? "파일 내용을 확인하지 못했습니다.";
            }
            else if (backend.SessionJobIdsByPath.TryGetValue(normalizedPath, out jobId)
                     && (!backend.SessionContentHashesByPath.TryGetValue(normalizedPath, out var sessionHash)
                         || string.Equals(sessionHash, hash, StringComparison.OrdinalIgnoreCase)))
            {
                classification = IncrementalImportClassification.InProgress;
                reason = "이 PC에 저장된 기존 NAS 작업이 있습니다.";
            }
            else if (backend.ExistingContentHashes.Contains(hash!))
            {
                classification = IncrementalImportClassification.Existing;
                reason = "이미 MemoryKeeper에 등록된 동일한 사진입니다.";
            }
            else if (backend.AcceptedContentHashes.Contains(hash!))
            {
                classification = IncrementalImportClassification.InProgress;
                reason = "동일한 사진이 NAS에서 처리 중이거나 기존 작업으로 접수되었습니다.";
            }
            else if (!backend.IsComplete)
            {
                classification = IncrementalImportClassification.Uncertain;
                reason = "NAS 상태 확인이 완료되지 않아 안전을 위해 전송하지 않습니다.";
            }
            else if (!seenNewHashes.Add(hash!))
            {
                classification = IncrementalImportClassification.Duplicate;
                reason = "선택한 폴더 안에 내용이 같은 사진이 있습니다.";
            }
            else
            {
                classification = IncrementalImportClassification.New;
            }

            items.Add(new IncrementalImportItemDto
            {
                FilePath = identity.FilePath,
                FileName = Path.GetFileName(identity.FilePath),
                ContentHash = hash,
                Classification = classification,
                ExistingJobId = jobId,
                Reason = reason,
            });
        }

        progress?.Report(new ImportPreflightProgressDto
        {
            TotalCount = files.Count,
            ProcessedCount = files.Count,
            Stage = "사진 확인 완료",
        });
        return new IncrementalImportPreflightResult
        {
            SourceFolderPath = sourceFolderPath,
            Items = items,
            BackendSnapshotComplete = backend.IsComplete,
            BackendWarning = backend.Warning,
        };
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }
}
