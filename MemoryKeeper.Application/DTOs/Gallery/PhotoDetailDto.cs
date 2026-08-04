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

    [JsonPropertyName("service_name")]
    public string ServiceName { get; init; } = "MemoryKeeper";

    [JsonPropertyName("storage_path")]
    public string? StoragePath { get; init; }

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
}
