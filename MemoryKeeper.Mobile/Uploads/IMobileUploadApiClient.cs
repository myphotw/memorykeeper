using MemoryKeeper.Application.DTOs.Upload;

namespace MemoryKeeper.Mobile.Uploads;

public interface IMobileUploadApiClient
{
    Task<MobileUploadBatchCapabilities> GetCapabilitiesAsync(
        CancellationToken cancellationToken = default);

    Task<MobileUploadPreflightResult> PreflightAsync(
        MobileUploadBatchCapabilities capabilities,
        string contentSha256,
        CancellationToken cancellationToken = default);

    Task<UploadResponseDto> UploadAsync(
        MobileUploadFile file,
        CancellationToken cancellationToken = default);

    Task<MobileUploadJobResult> GetJobAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);
}
