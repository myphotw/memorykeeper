using System.Text.Json.Serialization;

namespace MemoryKeeper.Application.DTOs.Gallery;

/// <summary>TC-Backend timeline year group.</summary>
public sealed class TimelineDto
{
    [JsonPropertyName("year")]
    public int Year { get; init; }

    [JsonPropertyName("count")]
    public int Count { get; init; }
}

/// <summary>TC-Backend <c>TimelineResponse</c>.</summary>
public sealed class TimelineResultDto
{
    [JsonPropertyName("items")]
    public IReadOnlyList<TimelineDto> Items { get; init; } = Array.Empty<TimelineDto>();

    [JsonPropertyName("total")]
    public int Total { get; init; }
}
