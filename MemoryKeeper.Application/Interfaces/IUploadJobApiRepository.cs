using MemoryKeeper.Application.DTOs.Upload;

namespace MemoryKeeper.Application.Interfaces;

/// <summary>
/// TC-Backend Upload Job status API port.
/// </summary>
public interface IUploadJobApiRepository
{
    Task<UploadJobStatusDto> GetStatusAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>GET /api/common/upload/jobs</c> — existing contract (status/page/page_size/sort).
    /// </summary>
    Task<UploadJobListDto> ListJobsAsync(
        string? status = null,
        int page = 1,
        int pageSize = 20,
        string sort = "created_at_desc",
        CancellationToken cancellationToken = default);
}
