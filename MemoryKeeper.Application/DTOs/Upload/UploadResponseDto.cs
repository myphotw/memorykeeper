using System.Text.Json.Serialization;

namespace MemoryKeeper.Application.DTOs.Upload;

/// <summary>TC-Backend <c>POST /api/common/upload</c> response (client view).</summary>
public sealed class UploadResponseDto
{
    [JsonPropertyName("job_id")]
    public string JobId { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    /// <summary>Optional client/server message. Backend may omit; filled from error or incoming_path.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("id")]
    public int? Id { get; init; }

    [JsonPropertyName("incoming_path")]
    public string? IncomingPath { get; init; }
}
