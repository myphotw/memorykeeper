namespace MemoryKeeper.Application.DTOs.Upload;

/// <summary>Upload job status snapshot for Import / future polling.</summary>
public sealed class UploadStatusDto
{
    public string JobId { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string? Message { get; init; }

    public static UploadStatusDto FromResponse(UploadResponseDto response) => new()
    {
        JobId = response.JobId,
        Status = response.Status,
        Message = response.Message ?? response.IncomingPath,
    };
}
