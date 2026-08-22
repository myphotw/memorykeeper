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

    [JsonPropertyName("memorykeeper_place_id")]
    public Guid? MemorykeeperPlaceId { get; init; }

    [JsonPropertyName("place_display_name")]
    public string? PlaceDisplayName { get; init; }

    [JsonPropertyName("place_canonical_name")]
    public string? PlaceCanonicalName { get; init; }

    [JsonPropertyName("geocoded_place_name")]
    public string? GeocodedPlaceName { get; init; }

    [JsonPropertyName("place_match_source")]
    public string? PlaceMatchSource { get; init; }

    [JsonPropertyName("place_match_distance_m")]
    public double? PlaceMatchDistanceM { get; init; }

    [JsonPropertyName("place_revision")]
    public int PlaceRevision { get; init; }

    [JsonPropertyName("country")]
    public string? Country { get; init; }

    [JsonPropertyName("province")]
    public string? Province { get; init; }

    [JsonPropertyName("city")]
    public string? City { get; init; }

    [JsonPropertyName("district")]
    public string? District { get; init; }

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
