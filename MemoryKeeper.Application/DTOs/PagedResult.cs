using System.Text.Json.Serialization;

namespace MemoryKeeper.Application.DTOs;

/// <summary>
/// Generic paged result for TC-Backend gallery list/search style responses.
/// </summary>
public sealed class PagedResult<T>
{
    [JsonPropertyName("items")]
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();

    [JsonPropertyName("page")]
    public int Page { get; init; }

    [JsonPropertyName("page_size")]
    public int PageSize { get; init; }

    /// <summary>Maps TC-Backend field <c>total</c>.</summary>
    [JsonPropertyName("total")]
    public int TotalCount { get; init; }

    [JsonPropertyName("sort")]
    public string? Sort { get; init; }
}
