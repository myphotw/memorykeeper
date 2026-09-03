using System.Text.Json.Serialization;

namespace MemoryKeeper.Application.DTOs;

/// <summary>MemoryKeeper fast-read gallery contracts. These are deliberately separate from the common Gallery API.</summary>
public sealed class FastGalleryPhotoDto
{
    [JsonPropertyName("common_file_id")] public long CommonFileId { get; init; }
    [JsonPropertyName("file_id")] public string FileId { get; init; } = string.Empty;
    [JsonPropertyName("filename")] public string Filename { get; init; } = string.Empty;
    [JsonPropertyName("extension")] public string? Extension { get; init; }
    [JsonPropertyName("mime_type")] public string? MimeType { get; init; }
    [JsonPropertyName("preview_url")] public string? PreviewUrl { get; init; }
    [JsonPropertyName("thumbnail_url")] public string? ThumbnailUrl { get; init; }
    [JsonPropertyName("favorite")] public bool Favorite { get; init; }
    [JsonPropertyName("has_gps")] public bool HasGps { get; init; }
    [JsonPropertyName("effective_capture_datetime")] public DateTimeOffset EffectiveCaptureDatetime { get; init; }
    [JsonPropertyName("effective_capture_date")] public DateOnly EffectiveCaptureDate { get; init; }
    [JsonPropertyName("effective_capture_year")] public int EffectiveCaptureYear { get; init; }
    [JsonPropertyName("date_basis")] public string DateBasis { get; init; } = string.Empty;
    [JsonPropertyName("memorykeeper_place_id")] public Guid? MemorykeeperPlaceId { get; init; }
    [JsonPropertyName("place_display_name")] public string? PlaceDisplayName { get; init; }
    [JsonPropertyName("country")] public string? Country { get; init; }
    [JsonPropertyName("region")] public string? Region { get; init; }
}

public sealed class FastGalleryPhotoPageDto
{
    [JsonPropertyName("items")] public IReadOnlyList<FastGalleryPhotoDto> Items { get; init; } = [];
    [JsonPropertyName("next_cursor")] public string? NextCursor { get; init; }
    [JsonPropertyName("has_more")] public bool HasMore { get; init; }
    [JsonPropertyName("sync_cursor")] public string? SyncCursor { get; init; }
}

public sealed class FastGalleryCountDto
{
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("count")] public int Count { get; init; }
}

public sealed class FastGallerySummaryDto
{
    [JsonPropertyName("total_photos")] public int TotalPhotos { get; init; }
    [JsonPropertyName("favorite_count")] public int FavoriteCount { get; init; }
    [JsonPropertyName("gps_count")] public int GpsCount { get; init; }
    [JsonPropertyName("effective_date_min")] public DateOnly? EffectiveDateMin { get; init; }
    [JsonPropertyName("effective_date_max")] public DateOnly? EffectiveDateMax { get; init; }
    [JsonPropertyName("by_year")] public IReadOnlyList<FastGalleryCountDto> ByYear { get; init; } = [];
    [JsonPropertyName("by_country")] public IReadOnlyList<FastGalleryCountDto> ByCountry { get; init; } = [];
}

public sealed class FastGalleryHierarchyNodeDto
{
    [JsonPropertyName("year")] public int? Year { get; init; }
    [JsonPropertyName("country")] public string? Country { get; init; }
    [JsonPropertyName("region")] public string? Region { get; init; }
    [JsonPropertyName("place_id")] public Guid? PlaceId { get; init; }
    [JsonPropertyName("memorykeeper_place_id")] public Guid? MemorykeeperPlaceId { get; init; }
    [JsonPropertyName("display_name")] public string? DisplayName { get; init; }
    [JsonPropertyName("count")] public int Count { get; init; }
    [JsonPropertyName("children")] public IReadOnlyList<FastGalleryHierarchyNodeDto> Children { get; init; } = [];
    [JsonPropertyName("countries")] public IReadOnlyList<FastGalleryHierarchyNodeDto> Countries { get; init; } = [];
    [JsonPropertyName("regions")] public IReadOnlyList<FastGalleryHierarchyNodeDto> Regions { get; init; } = [];
    [JsonPropertyName("places")] public IReadOnlyList<FastGalleryHierarchyNodeDto> Places { get; init; } = [];
    public IReadOnlyList<FastGalleryHierarchyNodeDto> ChildNodes => Children.Count > 0 ? Children
        : Countries.Count > 0 ? Countries : Regions.Count > 0 ? Regions : Places;
}

public sealed class FastGalleryHierarchyDto
{
    [JsonPropertyName("items")] public IReadOnlyList<FastGalleryHierarchyNodeDto> Items { get; init; } = [];
    // The deployed endpoint has used both names during rollout; accepting years keeps the client additive.
    [JsonPropertyName("years")] public IReadOnlyList<FastGalleryHierarchyNodeDto> Years { get; init; } = [];
    public IReadOnlyList<FastGalleryHierarchyNodeDto> Roots => Items.Count > 0 ? Items : Years;
}

public sealed class FastGalleryPhotoQuery
{
    public int Limit { get; init; } = 50;
    public string? Cursor { get; init; }
    public int? Year { get; init; }
    public string? Country { get; init; }
    public string? Region { get; init; }
    public Guid? PlaceId { get; init; }
    public bool? Favorite { get; init; }
    public bool? HasGps { get; init; }
    public DateOnly? DateFrom { get; init; }
    public DateOnly? DateTo { get; init; }
}
