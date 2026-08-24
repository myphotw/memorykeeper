using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Application.Interfaces;

/// <summary>Authenticated NAS operations required by MemoryKeeper Settings.</summary>
public interface IMemoryKeeperOperationsApiRepository
{
    Task<AutoTagStatusDto> GetAutoTagStatusAsync(CancellationToken cancellationToken = default);
    Task<AutoTagFailedJobListDto> GetFailedAutoTagsAsync(int page = 1, int pageSize = 50, CancellationToken cancellationToken = default);
    Task<AutoTagRetryResultDto> RetryFailedAutoTagsAsync(int limit = 100, CancellationToken cancellationToken = default);
    Task<AutoTagRetryResultDto> RetryAutoTagJobAsync(int jobId, CancellationToken cancellationToken = default);
    Task<MemoryKeeperResetPreviewDto> PreviewResetAsync(CancellationToken cancellationToken = default);
    Task<MemoryKeeperResetExecuteResultDto> ExecuteResetAsync(MemoryKeeperResetExecuteRequest request, CancellationToken cancellationToken = default);
}
