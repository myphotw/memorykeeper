using System.Text.Json.Serialization;

namespace MemoryKeeper.Application.DTOs;

public sealed class FastTravelAggregateDto
{
    [JsonPropertyName("memorykeeper_place_id")] public Guid? MemorykeeperPlaceId { get; init; }
    [JsonPropertyName("place_display_name")] public string? PlaceDisplayName { get; init; }
    [JsonPropertyName("country")] public string? Country { get; init; }
    [JsonPropertyName("region")] public string? Region { get; init; }
    [JsonPropertyName("photo_count")] public int PhotoCount { get; init; }
    [JsonPropertyName("capture_dates")] public IReadOnlyList<DateOnly> CaptureDates { get; init; } = [];
    [JsonPropertyName("visit_count")] public int VisitCount { get; init; }
    [JsonPropertyName("representative_common_file_id")] public long? RepresentativeCommonFileId { get; init; }
    [JsonPropertyName("representative_file_id")] public string? RepresentativeFileId { get; init; }
    [JsonPropertyName("representative_capture_date")] public DateOnly? RepresentativeCaptureDate { get; init; }
    [JsonPropertyName("representative_preview_url")] public string? RepresentativePreviewUrl { get; init; }
    [JsonPropertyName("representative_thumbnail_url")] public string? RepresentativeThumbnailUrl { get; init; }
}

public sealed class FastTravelAggregatesDto
{
    [JsonPropertyName("places")] public IReadOnlyList<FastTravelAggregateDto> Places { get; init; } = [];
    [JsonPropertyName("countries")] public IReadOnlyList<FastTravelAggregateDto> Countries { get; init; } = [];
}

public sealed class FastTravelMemoryCandidateDto
{
    [JsonPropertyName("common_file_id")] public long CommonFileId { get; init; }
    [JsonPropertyName("file_id")] public string FileId { get; init; } = string.Empty;
    [JsonPropertyName("effective_capture_date")] public DateOnly EffectiveCaptureDate { get; init; }
    [JsonPropertyName("place_id")] public Guid? PlaceId { get; init; }
    [JsonPropertyName("memorykeeper_place_id")] public Guid? MemorykeeperPlaceId { get; init; }
    [JsonPropertyName("place_display_name")] public string? PlaceDisplayName { get; init; }
    [JsonPropertyName("country")] public string? Country { get; init; }
    [JsonPropertyName("thumbnail_url")] public string? ThumbnailUrl { get; init; }
    [JsonPropertyName("preview_url")] public string? PreviewUrl { get; init; }
    [JsonPropertyName("candidate")] public string? Candidate { get; init; }
    [JsonPropertyName("category")] public string? Category { get; init; }
}

public sealed class FastTravelMemoriesDto
{
    [JsonPropertyName("items")] public IReadOnlyList<FastTravelMemoryCandidateDto> Items { get; init; } = [];
}
