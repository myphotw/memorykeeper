using System.Text.Json;
using System.Text.Json.Serialization;

namespace MemoryKeeper.Application.DTOs.Gallery;

/// <summary>TC-Backend Gallery detail response.</summary>
public sealed class PhotoDetailDto
{
    [JsonPropertyName("file_id")]
    public string FileId { get; init; } = string.Empty;

    [JsonPropertyName("filename")]
    public string Filename { get; init; } = string.Empty;

    [JsonPropertyName("extension")]
    public string? Extension { get; init; }

    [JsonPropertyName("mime_type")]
    public string? MimeType { get; init; }

    [JsonPropertyName("file_size")]
    public long? FileSize { get; init; }

    [JsonPropertyName("width")]
    public int? Width { get; init; }

    [JsonPropertyName("height")]
    public int? Height { get; init; }

    [JsonPropertyName("favorite")]
    public bool Favorite { get; init; }

    [JsonPropertyName("memo")]
    public string? Memo { get; init; }

    [JsonPropertyName("metadata_revision")]
    public int MetadataRevision { get; init; }

    [JsonPropertyName("incomplete")]
    public bool Incomplete { get; init; }

    [JsonPropertyName("service_name")]
    public string ServiceName { get; init; } = "MemoryKeeper";

    [JsonPropertyName("storage_path")]
    public string? StoragePath { get; init; }

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

    [JsonPropertyName("preview_url")]
    public string? PreviewUrl { get; init; }

    [JsonPropertyName("thumbnail_url")]
    public string? ThumbnailUrl { get; init; }

    [JsonPropertyName("original_url")]
    public string? OriginalUrl { get; init; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, JsonElement> Metadata { get; init; } = new();

    [JsonPropertyName("ai_tags")]
    public IReadOnlyList<GalleryTagDto> AiTags { get; init; } = Array.Empty<GalleryTagDto>();

    [JsonPropertyName("user_tags")]
    public IReadOnlyList<GalleryTagDto> UserTags { get; init; } = Array.Empty<GalleryTagDto>();

    [JsonPropertyName("tags")]
    public IReadOnlyList<GalleryTagDto> Tags { get; init; } = Array.Empty<GalleryTagDto>();

    [JsonPropertyName("history_count")]
    public int HistoryCount { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; init; }
}

public sealed class GalleryTagDto
{
    [JsonPropertyName("tag")]
    public string Tag { get; init; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; init; } = string.Empty;

    [JsonPropertyName("tag_type")]
    public string TagType { get; init; } = string.Empty;

    [JsonPropertyName("confidence")]
    public double? Confidence { get; init; }

    [JsonPropertyName("tag_id")]
    public int? TagId { get; init; }

    [JsonPropertyName("canonical")]
    public string? Canonical { get; init; }

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("aliases")]
    public IReadOnlyList<string> Aliases { get; init; } = Array.Empty<string>();

    [JsonPropertyName("curation_version")]
    public int? CurationVersion { get; init; }

    [JsonPropertyName("identity")]
    public string? Identity { get; init; }

    [JsonPropertyName("editable")]
    public bool Editable { get; init; }

    [JsonPropertyName("revision")]
    public int? Revision { get; init; }
}
