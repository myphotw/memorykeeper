using System.Text.Json.Serialization;

namespace MemoryKeeper.Application.DTOs.Upload;

/// <summary>TC-Backend <c>GET /api/common/upload/jobs/{job_id}</c> response.</summary>
public sealed class UploadJobStatusDto
{
    public const string Waiting = "WAITING";
    public const string Processing = "PROCESSING";
    public const string Completed = "COMPLETED";
    public const string Failed = "FAILED";

    [JsonPropertyName("job_id")]
    public string JobId { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    /// <summary>Backend progress: 0 / 16 / 33 / 50 / 66 / 83 / 100.</summary>
    [JsonPropertyName("progress")]
    public int Progress { get; init; }

    [JsonPropertyName("current_plugin")]
    public string? CurrentPlugin { get; init; }

    [JsonPropertyName("processing_log")]
    public string? ProcessingLog { get; init; }

    [JsonPropertyName("retry_count")]
    public int RetryCount { get; init; }

    [JsonPropertyName("requested_at")]
    public DateTimeOffset? RequestedAt { get; init; }

    [JsonPropertyName("started_at")]
    public DateTimeOffset? StartedAt { get; init; }

    [JsonPropertyName("completed_at")]
    public DateTimeOffset? CompletedAt { get; init; }

    [JsonPropertyName("last_error")]
    public string? LastError { get; init; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; init; }

    [JsonPropertyName("service_name")]
    public string? ServiceName { get; init; }

    [JsonPropertyName("client_file_id")]
    public string? ClientFileId { get; init; }

    [JsonPropertyName("backend_file_id")]
    public string? BackendFileId { get; init; }

    [JsonPropertyName("common_file_id")]
    public int? CommonFileId { get; init; }

    public bool IsTerminal =>
        string.Equals(Status, Completed, StringComparison.OrdinalIgnoreCase)
        || string.Equals(Status, Failed, StringComparison.OrdinalIgnoreCase);

    public bool IsCompleted =>
        string.Equals(Status, Completed, StringComparison.OrdinalIgnoreCase);

    public bool IsFailed =>
        string.Equals(Status, Failed, StringComparison.OrdinalIgnoreCase);
}
