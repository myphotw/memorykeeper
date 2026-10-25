using System.Text.Json;
using System.Text.Json.Serialization;

namespace MemoryKeeper.Application.DTOs;

/// <summary>Raw MemoryKeeper Place row returned by tc-backend.</summary>
public sealed class MemoryKeeperPlaceApiDto
{
    public Guid Id { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string? CanonicalName { get; init; }
    public string? Address { get; init; }
    public string? PostalCode { get; init; }
    public string? Country { get; init; }
    public string? Province { get; init; }
    public string? City { get; init; }
    public string? District { get; init; }
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public double RadiusM { get; init; }
    public string? ProviderPlaceId { get; init; }
    public string? Category { get; init; }
    public bool Active { get; init; }
    public bool Favorite { get; init; }
    public int UsageCount { get; init; }
    public DateTimeOffset? LastUsedAt { get; init; }
    public int Revision { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}

public sealed class MemoryKeeperPlaceListApiDto
{
    public IReadOnlyList<MemoryKeeperPlaceApiDto> Items { get; init; } = [];
    public int Total { get; init; }
    public int Limit { get; init; }
    public int Offset { get; init; }
}

/// <summary>Canonical registered-place path returned by tc-backend.</summary>
public sealed class MemoryKeeperPlaceHierarchyPathApiDto
{
    [JsonPropertyName("memorykeeper_place_id")]
    public Guid MemorykeeperPlaceId { get; init; }

    [JsonPropertyName("location_key")]
    public string? LocationKey { get; init; }

    [JsonPropertyName("country")]
    public string? Country { get; init; }

    [JsonPropertyName("region")]
    public string? Region { get; init; }

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("location_path")]
    public IReadOnlyList<string>? LocationPath { get; init; }

    [JsonPropertyName("full_path")]
    public string? FullPath { get; init; }

    [JsonPropertyName("photo_count")]
    public int PhotoCount { get; init; }

    [JsonPropertyName("location_hierarchy_complete")]
    public bool LocationHierarchyComplete { get; init; }

    [JsonPropertyName("location_cleanup_reason")]
    public string? LocationCleanupReason { get; init; }
}

[JsonConverter(typeof(MemoryKeeperPlaceHierarchyListApiDtoJsonConverter))]
public sealed class MemoryKeeperPlaceHierarchyListApiDto
{
    [JsonPropertyName("items")]
    public IReadOnlyList<MemoryKeeperPlaceHierarchyPathApiDto> Items { get; init; } = [];

    [JsonPropertyName("total_places")]
    public int TotalPlaces { get; init; }

    [JsonPropertyName("total_paths")]
    public int TotalPaths { get; init; }
}

/// <summary>
/// Accepts the deployed flat-array shape as well as the standard MemoryKeeper { items: [] }
/// envelope. Both shapes describe the same flat canonical path contract.
/// </summary>
public sealed class MemoryKeeperPlaceHierarchyListApiDtoJsonConverter
    : JsonConverter<MemoryKeeperPlaceHierarchyListApiDto>
{
    public override MemoryKeeperPlaceHierarchyListApiDto Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var itemsElement = root.ValueKind switch
        {
            JsonValueKind.Array => root,
            JsonValueKind.Object when root.TryGetProperty("items", out var envelopeItems) => envelopeItems,
            _ => throw new JsonException("Place hierarchy response must be a flat array or an items envelope."),
        };
        if (itemsElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Place hierarchy items must be an array.");
        }

        var items = JsonSerializer.Deserialize<List<MemoryKeeperPlaceHierarchyPathApiDto>>(
            itemsElement.GetRawText(),
            options) ?? [];
        var totalPlaces = root.ValueKind == JsonValueKind.Object
                          && root.TryGetProperty("total_places", out var totalPlacesElement)
                          && totalPlacesElement.TryGetInt32(out var parsedTotalPlaces)
            ? parsedTotalPlaces
            : items.Select(item => item.MemorykeeperPlaceId).Where(id => id != Guid.Empty).Distinct().Count();
        var totalPaths = root.ValueKind == JsonValueKind.Object
                         && root.TryGetProperty("total_paths", out var totalPathsElement)
                         && totalPathsElement.TryGetInt32(out var parsedTotalPaths)
            ? parsedTotalPaths
            : items.Count;
        return new MemoryKeeperPlaceHierarchyListApiDto
        {
            Items = items,
            TotalPlaces = totalPlaces,
            TotalPaths = totalPaths,
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        MemoryKeeperPlaceHierarchyListApiDto value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("items");
        JsonSerializer.Serialize(writer, value.Items, options);
        writer.WriteNumber("total_places", value.TotalPlaces);
        writer.WriteNumber("total_paths", value.TotalPaths);
        writer.WriteEndObject();
    }
}

public sealed class MemoryKeeperPlacePhotoApiDto
{
    [JsonPropertyName("file_id")]
    public string FileId { get; init; } = string.Empty;

    [JsonPropertyName("filename")]
    public string Filename { get; init; } = string.Empty;

    [JsonPropertyName("thumbnail_url")]
    public string? ThumbnailUrl { get; init; }

    [JsonPropertyName("preview_url")]
    public string? PreviewUrl { get; init; }

    [JsonPropertyName("effective_capture_datetime")]
    public DateTimeOffset? EffectiveCaptureDatetime { get; init; }

    [JsonPropertyName("effective_capture_date")]
    public DateOnly? EffectiveCaptureDate { get; init; }

    [JsonPropertyName("effective_capture_year")]
    public int? EffectiveCaptureYear { get; init; }

    [JsonPropertyName("effective_capture_precision")]
    public string? EffectiveCapturePrecision { get; init; }
}

public sealed class MemoryKeeperPlacePhotoPageApiDto
{
    [JsonPropertyName("items")]
    public IReadOnlyList<MemoryKeeperPlacePhotoApiDto> Items { get; init; } = [];

    [JsonPropertyName("total")]
    public int Total { get; init; }

    [JsonPropertyName("linked_photo_count")]
    public int LinkedPhotoCount { get; init; }

    [JsonPropertyName("limit")]
    public int Limit { get; init; }

    [JsonPropertyName("offset")]
    public int Offset { get; init; }

    [JsonPropertyName("has_more")]
    public bool HasMore { get; init; }

    [JsonPropertyName("next_offset")]
    public int? NextOffset { get; init; }

    [JsonIgnore]
    public int TotalCount => Math.Max(Total, LinkedPhotoCount);
}

public sealed class MemoryKeeperPlaceDuplicateCandidateApiDto
{
    [JsonPropertyName("source_place")]
    public MemoryKeeperPlaceApiDto? SourcePlace { get; init; }

    [JsonPropertyName("target_place")]
    public MemoryKeeperPlaceApiDto? TargetPlace { get; init; }

    [JsonPropertyName("source_place_id")]
    public Guid SourcePlaceId { get; init; }

    [JsonPropertyName("target_place_id")]
    public Guid TargetPlaceId { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    [JsonPropertyName("distance_m")]
    public double? DistanceM { get; init; }
}

[JsonConverter(typeof(MemoryKeeperPlaceDuplicateCandidateListApiDtoJsonConverter))]
public sealed class MemoryKeeperPlaceDuplicateCandidateListApiDto
{
    [JsonPropertyName("items")]
    public IReadOnlyList<MemoryKeeperPlaceDuplicateCandidateApiDto> Items { get; init; } = [];
}

public sealed class MemoryKeeperPlaceDuplicateCandidateListApiDtoJsonConverter
    : JsonConverter<MemoryKeeperPlaceDuplicateCandidateListApiDto>
{
    public override MemoryKeeperPlaceDuplicateCandidateListApiDto Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var itemsElement = root.ValueKind switch
        {
            JsonValueKind.Array => root,
            JsonValueKind.Object when root.TryGetProperty("items", out var envelopeItems) => envelopeItems,
            _ => throw new JsonException("Duplicate candidates must be an array or an items envelope."),
        };
        var items = JsonSerializer.Deserialize<List<MemoryKeeperPlaceDuplicateCandidateApiDto>>(
            itemsElement.GetRawText(), options) ?? [];
        return new MemoryKeeperPlaceDuplicateCandidateListApiDto { Items = items };
    }

    public override void Write(
        Utf8JsonWriter writer,
        MemoryKeeperPlaceDuplicateCandidateListApiDto value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("items");
        JsonSerializer.Serialize(writer, value.Items, options);
        writer.WriteEndObject();
    }
}

public sealed class MemoryKeeperPlaceMergePreviewApiDto
{
    [JsonPropertyName("source_place")]
    public MemoryKeeperPlaceApiDto? SourcePlace { get; init; }

    [JsonPropertyName("target_place")]
    public MemoryKeeperPlaceApiDto? TargetPlace { get; init; }

    [JsonPropertyName("source_path")]
    public string? SourcePath { get; init; }

    [JsonPropertyName("target_path")]
    public string? TargetPath { get; init; }

    [JsonPropertyName("source_linked_photo_count")]
    public int SourceLinkedPhotoCount { get; init; }

    [JsonPropertyName("target_linked_photo_count")]
    public int TargetLinkedPhotoCount { get; init; }

    [JsonPropertyName("moved_photo_count")]
    public int MovedPhotoCount { get; init; }

    [JsonPropertyName("source_revision")]
    public int SourceRevision { get; init; }

    [JsonPropertyName("target_revision")]
    public int TargetRevision { get; init; }
}

public sealed class MemoryKeeperPlaceMergeApiRequest
{
    [JsonPropertyName("source_place_id")]
    public required Guid SourcePlaceId { get; init; }

    [JsonPropertyName("target_place_id")]
    public required Guid TargetPlaceId { get; init; }

    [JsonPropertyName("expected_source_revision")]
    public required int ExpectedSourceRevision { get; init; }

    [JsonPropertyName("expected_target_revision")]
    public required int ExpectedTargetRevision { get; init; }
}

public sealed class MemoryKeeperPlaceMergeApiResult
{
    [JsonPropertyName("source_place_id")]
    public Guid SourcePlaceId { get; init; }

    [JsonPropertyName("target_place_id")]
    public Guid TargetPlaceId { get; init; }

    [JsonPropertyName("moved_photo_count")]
    public int MovedPhotoCount { get; init; }

    [JsonPropertyName("target_place")]
    public MemoryKeeperPlaceApiDto? TargetPlace { get; init; }
}

public sealed class MemoryKeeperPlaceCreateApiRequest
{
    public required string DisplayName { get; init; }
    public string? CanonicalName { get; init; }
    public string? Address { get; init; }
    public string? PostalCode { get; init; }
    public string? Country { get; init; }
    public string? Province { get; init; }
    public string? City { get; init; }
    public string? District { get; init; }
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }
    public required double RadiusM { get; init; }
    public string? ProviderPlaceId { get; init; }
    public string? Category { get; init; }
    public bool Active { get; init; } = true;
    public bool Favorite { get; init; }
}

public sealed class MemoryKeeperPlaceUpdateApiRequest
{
    public required int Revision { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; init; }
    public string? CanonicalName { get; init; }
    public string? Address { get; init; }
    public string? PostalCode { get; init; }
    public string? Country { get; init; }
    public string? Province { get; init; }
    public string? City { get; init; }
    public string? District { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Latitude { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Longitude { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? RadiusM { get; init; }
    public string? ProviderPlaceId { get; init; }
    public string? Category { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Active { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Favorite { get; init; }
}

public sealed class MemoryKeeperPlaceReclassifyApiRequest
{
    public bool ReassignFromOtherPlaces { get; init; }
}

public sealed class MemoryKeeperPlaceMatchApiRequest
{
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }
    public string? ProviderPlaceId { get; init; }
    public string? CanonicalName { get; init; }
}

public sealed class MemoryKeeperPlaceMatchApiResult
{
    public bool Matched { get; init; }
    public MemoryKeeperPlaceApiDto? Place { get; init; }
    public double? DistanceM { get; init; }
    public string? MatchSource { get; init; }
}

public sealed class MemoryKeeperPlaceReclassifyApiResult
{
    public Guid PlaceId { get; init; }
    public int Scanned { get; init; }
    public int Assigned { get; init; }
    public int Reassigned { get; init; }
    public int UnassignedOutsideRadius { get; init; }
    public int Unchanged { get; init; }
}

public sealed class MemoryKeeperRadiusImpactApiRequest
{
    public Guid? PlaceId { get; init; }
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }
    public required double RadiusM { get; init; }
}

public sealed class MemoryKeeperRadiusOverlapApiDto
{
    public required MemoryKeeperPlaceApiDto Place { get; init; }
    public double CenterDistanceM { get; init; }
}

public sealed class MemoryKeeperRadiusImpactApiResult
{
    public int MatchedFileCount { get; init; }
    public IReadOnlyList<string> AffectedFileIds { get; init; } = [];
    public IReadOnlyList<MemoryKeeperRadiusOverlapApiDto> OverlappingPlaces { get; init; } = [];
}

public sealed class MemoryKeeperFilePlaceUpdateApiRequest
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public Guid? MemorykeeperPlaceId { get; init; }
    public required int ExpectedRevision { get; init; }
}

public sealed class MemoryKeeperFilePlaceUpdateApiResult
{
    public string FileId { get; init; } = string.Empty;
    public Guid? MemorykeeperPlaceId { get; init; }
    public string? PlaceDisplayName { get; init; }
    public string? PlaceCanonicalName { get; init; }
    public string? GeocodedPlaceName { get; init; }
    public string? PlaceMatchSource { get; init; }
    public double? PlaceMatchDistanceM { get; init; }
    public int PlaceRevision { get; init; }
}

public sealed class MemoryKeeperPlaceRevisionConflictException : Exception
{
    public MemoryKeeperPlaceRevisionConflictException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class MemoryKeeperPlaceUpdateOperationResult
{
    public bool Cancelled { get; init; }

    public bool GeometryChanged { get; init; }

    public bool ReclassificationSkippedBecauseInactive { get; init; }

    public PlaceDto? UpdatedPlace { get; init; }

    public MemoryKeeperRadiusImpactApiResult? RadiusImpact { get; init; }

    public PlaceReclassificationResult Reclassification { get; init; } = new();
}
