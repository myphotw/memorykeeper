using MemoryKeeper.Application.DTOs.Upload;

namespace MemoryKeeper.Application.Interfaces;

/// <summary>
/// TC-Backend Upload API port (V1.0). Sends files only; no local SQLite writes.
/// </summary>
public interface IUploadApiRepository
{
    /// <summary>
    /// Uploads a local file via multipart/form-data field <c>file</c>.
    /// </summary>
    Task<UploadResponseDto> UploadAsync(string filePath, CancellationToken cancellationToken = default);
}
