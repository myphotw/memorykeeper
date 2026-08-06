using System.Text.Json.Serialization;

namespace MemoryKeeper.Application.DTOs.Upload;

/// <summary>TC-Backend <c>GET /api/common/upload/jobs</c> paged list.</summary>
public sealed class UploadJobListDto
{
    [JsonPropertyName("items")]
    public IReadOnlyList<UploadJobStatusDto> Items { get; init; } = [];

    [JsonPropertyName("page")]
    public int Page { get; init; }

    [JsonPropertyName("page_size")]
    public int PageSize { get; init; }

    [JsonPropertyName("total")]
    public int Total { get; init; }

    [JsonPropertyName("sort")]
    public string? Sort { get; init; }
}
