using MemoryKeeper.Application.DTOs.Upload;

namespace MemoryKeeper.Application.Interfaces;

/// <summary>
/// TC-Backend Upload Job status API port.
/// </summary>
public interface IUploadJobApiRepository
{
    Task<UploadJobStatusDto> GetStatusAsync(Guid jobId, CancellationToken cancellationToken = default);
}
