using System.Text.Json.Serialization;

namespace MemoryKeeper.Application.DTOs.Gallery;

/// <summary>TC-Backend map marker item.</summary>
public sealed class MapMarkerDto
{
    [JsonPropertyName("file_id")]
    public string FileId { get; init; } = string.Empty;

    [JsonPropertyName("latitude")]
    public double Latitude { get; init; }

    [JsonPropertyName("longitude")]
    public double Longitude { get; init; }

    [JsonPropertyName("place_name")]
    public string? PlaceName { get; init; }

    [JsonPropertyName("thumbnail")]
    public string? Thumbnail { get; init; }

    [JsonPropertyName("year")]
    public int? Year { get; init; }

    [JsonPropertyName("service_name")]
    public string ServiceName { get; init; } = "MemoryKeeper";
}

/// <summary>TC-Backend <c>MapMarkerListResponse</c>.</summary>
public sealed class MapResultDto
{
    [JsonPropertyName("items")]
    public IReadOnlyList<MapMarkerDto> Items { get; init; } = Array.Empty<MapMarkerDto>();

    [JsonPropertyName("total")]
    public int Total { get; init; }
}
