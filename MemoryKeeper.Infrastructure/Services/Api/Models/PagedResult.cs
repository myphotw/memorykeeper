using System.Text.Json.Serialization;

namespace MemoryKeeper.Infrastructure.Services.Api.Models;

/// <summary>
/// Paged gallery-style result aligned with TC-Backend Gallery list/search responses.
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
}
