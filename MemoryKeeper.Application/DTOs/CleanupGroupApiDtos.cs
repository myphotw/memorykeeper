using System.Text.Json.Serialization;

namespace MemoryKeeper.Application.DTOs;

public sealed class PlaceCleanupGroupDto
{
    [JsonPropertyName("group_id")]
    public string GroupId { get; init; } = string.Empty;

    [JsonPropertyName("issue_type")]
    public string IssueType { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("media_count")]
    public int MediaCount { get; init; }

    [JsonPropertyName("first_effective_capture_datetime")]
    public DateTimeOffset? FirstEffectiveCaptureDatetime { get; init; }

    [JsonPropertyName("last_effective_capture_datetime")]
    public DateTimeOffset? LastEffectiveCaptureDatetime { get; init; }

    [JsonPropertyName("estimated_location")]
    public string? EstimatedLocation { get; init; }

    [JsonPropertyName("processing_status")]
    public string? ProcessingStatus { get; init; }

    [JsonPropertyName("representative_file_id")]
    public string? RepresentativeFileId { get; init; }

    [JsonPropertyName("representative_thumbnail_url")]
    public string? RepresentativeThumbnailUrl { get; init; }
}

public sealed class CaptureDateCleanupGroupDto
{
    [JsonPropertyName("group_id")]
    public string GroupId { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("media_count")]
    public int MediaCount { get; init; }

    [JsonPropertyName("first_effective_capture_datetime")]
    public DateTimeOffset? FirstEffectiveCaptureDatetime { get; init; }

    [JsonPropertyName("last_effective_capture_datetime")]
    public DateTimeOffset? LastEffectiveCaptureDatetime { get; init; }

    [JsonPropertyName("cleanup_reason")]
    public string? CleanupReason { get; init; }

    [JsonPropertyName("date_basis")]
    public string? DateBasis { get; init; }

    [JsonPropertyName("effective_capture_date")]
    public string? EffectiveCaptureDate { get; init; }

    [JsonPropertyName("processing_status")]
    public string? ProcessingStatus { get; init; }

    [JsonPropertyName("representative_file_id")]
    public string? RepresentativeFileId { get; init; }

    [JsonPropertyName("representative_thumbnail_url")]
    public string? RepresentativeThumbnailUrl { get; init; }
}

public sealed class PlaceCleanupGroupListDto
{
    [JsonPropertyName("items")]
    public IReadOnlyList<PlaceCleanupGroupDto> Items { get; init; } = [];

    [JsonPropertyName("next_cursor")]
    public string? NextCursor { get; init; }

    [JsonPropertyName("has_more")]
    public bool HasMore { get; init; }

    [JsonPropertyName("total_groups")]
    public int TotalGroups { get; init; }

    [JsonPropertyName("total_photos")]
    public int TotalPhotos { get; init; }
}

public sealed class CaptureDateCleanupGroupListDto
{
    [JsonPropertyName("items")]
    public IReadOnlyList<CaptureDateCleanupGroupDto> Items { get; init; } = [];

    [JsonPropertyName("next_cursor")]
    public string? NextCursor { get; init; }

    [JsonPropertyName("has_more")]
    public bool HasMore { get; init; }

    [JsonPropertyName("total_groups")]
    public int TotalGroups { get; init; }

    [JsonPropertyName("total_photos")]
    public int TotalPhotos { get; init; }
}

public sealed class CleanupGroupPhotoListDto
{
    [JsonPropertyName("items")]
    public IReadOnlyList<MemoryKeeperPendingItemDto> Items { get; init; } = [];

    [JsonPropertyName("next_cursor")]
    public string? NextCursor { get; init; }

    [JsonPropertyName("has_more")]
    public bool HasMore { get; init; }

    [JsonPropertyName("total_photos")]
    public int TotalPhotos { get; init; }
}

public sealed class MemoryKeeperCaptureDateMutationRequest
{
    [JsonPropertyName("file_ids")]
    public IReadOnlyList<string> FileIds { get; init; } = [];

    [JsonPropertyName("user_capture_date")]
    public string? UserCaptureDate { get; init; }

    [JsonPropertyName("expected_date_revisions")]
    public IReadOnlyDictionary<string, int> ExpectedDateRevisions { get; init; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
}

public sealed class MemoryKeeperCaptureDateMutationItemDto
{
    [JsonPropertyName("file_id")]
    public string FileId { get; init; } = string.Empty;

    [JsonPropertyName("user_capture_datetime")]
    public DateTimeOffset? UserCaptureDatetime { get; init; }

    [JsonPropertyName("user_capture_precision")]
    public string? UserCapturePrecision { get; init; }

    [JsonPropertyName("effective_capture_datetime")]
    public DateTimeOffset? EffectiveCaptureDatetime { get; init; }

    [JsonPropertyName("effective_capture_date")]
    public string? EffectiveCaptureDate { get; init; }

    [JsonPropertyName("effective_capture_year")]
    public int? EffectiveCaptureYear { get; init; }

    [JsonPropertyName("date_basis")]
    public string? DateBasis { get; init; }

    [JsonPropertyName("date_revision")]
    public int DateRevision { get; init; }
}

public sealed class MemoryKeeperCaptureDateMutationResponse
{
    [JsonPropertyName("items")]
    public IReadOnlyList<MemoryKeeperCaptureDateMutationItemDto> Items { get; init; } = [];

    [JsonPropertyName("updated_count")]
    public int UpdatedCount { get; init; }
}
