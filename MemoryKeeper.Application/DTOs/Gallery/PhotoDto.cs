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

    [JsonPropertyName("city")]
    public string? City { get; init; }

    [JsonPropertyName("place_name")]
    public string? PlaceName { get; init; }

    [JsonPropertyName("camera_model")]
    public string? CameraModel { get; init; }

    [JsonPropertyName("favorite")]
    public bool Favorite { get; init; }

    [JsonPropertyName("has_gps")]
    public bool HasGps { get; init; }

    [JsonPropertyName("has_ai_tag")]
    public bool HasAiTag { get; init; }

    [JsonPropertyName("service_name")]
    public string ServiceName { get; init; } = "MemoryKeeper";
}
