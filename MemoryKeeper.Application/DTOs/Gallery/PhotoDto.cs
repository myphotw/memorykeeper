using System.Text.Json.Serialization;

namespace MemoryKeeper.Application.DTOs.Gallery;

/// <summary>TC-Backend Gallery list/search item.</summary>
public sealed class PhotoDto
{
    [JsonPropertyName("file_id")]
    public string FileId { get; init; } = string.Empty;

    [JsonPropertyName("filename")]
    public string Filename { get; init; } = string.Empty;

    [JsonPropertyName("preview_url")]
    public string? PreviewUrl { get; init; }

    [JsonPropertyName("thumbnail_url")]
    public string? ThumbnailUrl { get; init; }

    [JsonPropertyName("capture_datetime")]
    public DateTimeOffset? CaptureDatetime { get; init; }

    [JsonPropertyName("country")]
    public string? Country { get; init; }

    [JsonPropertyName("province")]
    public string? Province { get; init; }

    [JsonPropertyName("city")]
    public string? City { get; init; }

    [JsonPropertyName("district")]
    public string? District { get; init; }

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
    public int? PlaceRevision { get; init; }

    [JsonPropertyName("gps_lat")]
    public double? GpsLatitude { get; init; }

    [JsonPropertyName("gps_lon")]
    public double? GpsLongitude { get; init; }

    [JsonPropertyName("place_type")]
    public string? PlaceType { get; init; }

    [JsonPropertyName("camera_model")]
    public string? CameraModel { get; init; }

    [JsonPropertyName("favorite")]
    public bool Favorite { get; init; }

    [JsonPropertyName("memo")]
    public string? Memo { get; init; }

    [JsonPropertyName("metadata_revision")]
    public int MetadataRevision { get; init; }

    [JsonPropertyName("incomplete")]
    public bool Incomplete { get; init; }

    [JsonPropertyName("has_gps")]
    public bool HasGps { get; init; }

    [JsonPropertyName("has_ai_tag")]
    public bool HasAiTag { get; init; }

    [JsonPropertyName("service_name")]
    public string ServiceName { get; init; } = "MemoryKeeper";

    /// <summary>
    /// Optional Backend registration timestamps. Current V1 search rows may omit both;
    /// they preserve the former CapturedAt -&gt; ImportedAt year fallback when available.
    /// </summary>
    [JsonPropertyName("imported_at")]
    public DateTimeOffset? ImportedAt { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }
}
