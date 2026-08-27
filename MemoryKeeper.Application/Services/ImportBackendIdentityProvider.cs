using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.DTOs.Upload;
using MemoryKeeper.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Application.Services;

/// <summary>Builds one paged Backend identity snapshot; never performs one HTTP request per source file.</summary>
public sealed class ImportBackendIdentityProvider : IImportBackendIdentityProvider
{
    private const int PageSize = 200;
    private readonly IGalleryApiRepository _galleryApiRepository;
    private readonly IUploadJobApiRepository _uploadJobApiRepository;
    private readonly IImportJobSessionStore _sessionStore;
    private readonly ILogger<ImportBackendIdentityProvider> _logger;

    public ImportBackendIdentityProvider(
        IGalleryApiRepository galleryApiRepository,
        IUploadJobApiRepository uploadJobApiRepository,
        IImportJobSessionStore sessionStore,
        ILogger<ImportBackendIdentityProvider> logger)
    {
        _galleryApiRepository = galleryApiRepository;
        _uploadJobApiRepository = uploadJobApiRepository;
        _sessionStore = sessionStore;
        _logger = logger;
    }

    public async Task<ImportBackendIdentitySnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var accepted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sessionJobsByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sessionHashesByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var complete = true;
        string? warning = null;
        var backendJobs = new List<UploadJobStatusDto>();

        try
        {
            var firstPage = await _galleryApiRepository.GetPhotosAsync(
                1,
                PageSize,
                serviceName: "MemoryKeeper",
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var galleryItems = firstPage.Items.ToList();
            var pageCount = Math.Max(1, (int)Math.Ceiling((double)firstPage.TotalCount / PageSize));
            if (pageCount > 1)
            {
                using var gate = new SemaphoreSlim(4, 4);
                var remaining = await Task.WhenAll(Enumerable.Range(2, pageCount - 1).Select(async pageNumber =>
                {
                    await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        return await _galleryApiRepository.GetPhotosAsync(
                            pageNumber,
                            PageSize,
                            serviceName: "MemoryKeeper",
                            cancellationToken: cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        gate.Release();
                    }
                })).ConfigureAwait(false);
                galleryItems.AddRange(remaining.SelectMany(page => page.Items));
            }

            foreach (var item in galleryItems)
            {
                if (IsSha256(item.FileId))
                {
                    existing.Add(item.FileId);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            complete = false;
            warning = "기존 사진 목록을 모두 확인하지 못했습니다.";
            _logger.LogWarning(ex, "Incremental import Gallery identity snapshot failed.");
        }

        try
        {
            using var gate = new SemaphoreSlim(4, 4);
            var statusPages = await Task.WhenAll(
                LoadJobsByStatusAsync(UploadJobStatusDto.Waiting, gate, cancellationToken),
                LoadJobsByStatusAsync(UploadJobStatusDto.Processing, gate, cancellationToken),
                LoadJobsByStatusAsync(UploadJobStatusDto.Failed, gate, cancellationToken)).ConfigureAwait(false);
            var jobs = statusPages.SelectMany(items => items).ToList();

            foreach (var job in jobs.Where(job =>
                         string.IsNullOrWhiteSpace(job.ServiceName)
                         || string.Equals(job.ServiceName, "MemoryKeeper", StringComparison.OrdinalIgnoreCase)))
            {
                if (IsSha256(job.ClientFileId))
                {
                    accepted.Add(job.ClientFileId!);
                }

                if (job.IsCompleted && IsSha256(job.BackendFileId))
                {
                    existing.Add(job.BackendFileId!);
                }
            }

            backendJobs = jobs;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            complete = false;
            warning = "NAS 처리 중인 사진 목록을 모두 확인하지 못했습니다.";
            _logger.LogWarning(ex, "Incremental import Upload Job identity snapshot failed.");
        }

        try
        {
            var session = await _sessionStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            foreach (var job in session.Where(job => !string.IsNullOrWhiteSpace(job.LocalFilePath)))
            {
                var path = NormalizePath(job.LocalFilePath);
                sessionJobsByPath[path] = job.JobId;
                if (IsSha256(job.ContentHash))
                {
                    sessionHashesByPath[path] = job.ContentHash!;
                    accepted.Add(job.ContentHash!);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            complete = false;
            warning = "이 PC에 저장된 이전 사진 등록 상태를 확인하지 못했습니다.";
            _logger.LogWarning(ex, "Incremental import session identity snapshot failed.");
        }


        var sessionJobIds = sessionJobsByPath.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unidentifiedAcceptedJobCount = backendJobs.Count(job =>
            (string.Equals(job.Status, UploadJobStatusDto.Waiting, StringComparison.OrdinalIgnoreCase)
             || string.Equals(job.Status, UploadJobStatusDto.Processing, StringComparison.OrdinalIgnoreCase)
             || string.Equals(job.Status, UploadJobStatusDto.Failed, StringComparison.OrdinalIgnoreCase))
            && !IsSha256(job.ClientFileId)
            && !sessionJobIds.Contains(job.JobId));
        if (unidentifiedAcceptedJobCount > 0)
        {
            complete = false;
            warning = $"파일과 연결되지 않은 기존 NAS 작업 {unidentifiedAcceptedJobCount:N0}건이 있어 일부 사진을 안전하게 확정할 수 없습니다.";
        }

        return new ImportBackendIdentitySnapshot
        {
            ExistingContentHashes = existing,
            AcceptedContentHashes = accepted,
            SessionJobIdsByPath = sessionJobsByPath,
            SessionContentHashesByPath = sessionHashesByPath,
            IsComplete = complete,
            UnidentifiedAcceptedJobCount = unidentifiedAcceptedJobCount,
            Warning = warning,
        };
    }

    public static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(character => char.IsAsciiHexDigit(character));

    private async Task<IReadOnlyList<UploadJobStatusDto>> LoadJobsByStatusAsync(
        string status,
        SemaphoreSlim gate,
        CancellationToken cancellationToken)
    {
        async Task<UploadJobListDto> LoadPageAsync(int page)
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await _uploadJobApiRepository.ListJobsAsync(
                    status,
                    page,
                    PageSize,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }

        var firstPage = await LoadPageAsync(1).ConfigureAwait(false);
        var items = firstPage.Items.ToList();
        var pageCount = Math.Max(1, (int)Math.Ceiling((double)firstPage.Total / PageSize));
        if (pageCount > 1)
        {
            var remaining = await Task.WhenAll(
                Enumerable.Range(2, pageCount - 1).Select(LoadPageAsync)).ConfigureAwait(false);
            items.AddRange(remaining.SelectMany(page => page.Items));
        }

        return items;
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
