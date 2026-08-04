using System.Text.Json.Serialization;

namespace MemoryKeeper.Application.DTOs.Gallery;

/// <summary>TC-Backend <c>StatisticsResponse</c>.</summary>
public sealed class StatisticsDto
{
    [JsonPropertyName("total_photos")]
    public int TotalPhotos { get; init; }

    [JsonPropertyName("gps_count")]
    public int GpsCount { get; init; }

    [JsonPropertyName("ai_tag_count")]
    public int AiTagCount { get; init; }

    [JsonPropertyName("by_camera")]
    public IReadOnlyList<StatisticsCountItemDto> ByCamera { get; init; } = Array.Empty<StatisticsCountItemDto>();

    [JsonPropertyName("by_country")]
    public IReadOnlyList<StatisticsCountItemDto> ByCountry { get; init; } = Array.Empty<StatisticsCountItemDto>();

    [JsonPropertyName("by_year")]
    public IReadOnlyList<StatisticsCountItemDto> ByYear { get; init; } = Array.Empty<StatisticsCountItemDto>();

    [JsonPropertyName("by_service")]
    public IReadOnlyList<StatisticsCountItemDto> ByService { get; init; } = Array.Empty<StatisticsCountItemDto>();
}

public sealed class StatisticsCountItemDto
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("count")]
    public int Count { get; init; }
}
