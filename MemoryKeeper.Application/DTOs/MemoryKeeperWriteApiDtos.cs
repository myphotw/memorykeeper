using System.Text.Json.Serialization;

namespace MemoryKeeper.Application.DTOs;

public sealed class MemoryKeeperFileMetadataPatchRequest
{
    public int ExpectedRevision { get; init; }
    public bool? Favorite { get; init; }
    public string? Memo { get; init; }
    public double? GpsLat { get; init; }
    public double? GpsLon { get; init; }
    public string? Country { get; init; }
    public string? Province { get; init; }
    public string? City { get; init; }
    public string? District { get; init; }
    public string? PlaceName { get; init; }

    [JsonIgnore]
    public IReadOnlySet<string> ChangedFields { get; init; } = new HashSet<string>(StringComparer.Ordinal);
}

public sealed class MemoryKeeperFileMetadataPatchResponse
{
    public string FileId { get; init; } = string.Empty;
    public bool Favorite { get; init; }
    public string? Memo { get; init; }
    public int Revision { get; init; }
    public double? GpsLat { get; init; }
    public double? GpsLon { get; init; }
    public string? Country { get; init; }
    public string? Province { get; init; }
    public string? City { get; init; }
    public string? District { get; init; }
    public string? PlaceName { get; init; }
    public Guid? MemorykeeperPlaceId { get; init; }
    public string? PlaceMatchSource { get; init; }
    public double? PlaceMatchDistanceM { get; init; }
    public int PlaceRevision { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}

public sealed class MemoryKeeperDeleteResultDto
{
    public string FileId { get; init; } = string.Empty;
    public string CleanupStatus { get; init; } = string.Empty;
    public bool PhysicalFileDeleted { get; init; }
}

public sealed class MemoryKeeperTagDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string TagType { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public bool Favorite { get; init; }
    public int UsageCount { get; init; }
    public int Revision { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }

    public bool IsPinned => Favorite;
}

public sealed class MemoryKeeperTagListDto
{
    public IReadOnlyList<MemoryKeeperTagDto> Items { get; init; } = [];
    public int Total { get; init; }
}

public sealed class MemoryKeeperTagCreateRequest
{
    public string Name { get; init; } = string.Empty;
    public bool Favorite { get; init; }
}

public sealed class MemoryKeeperTagUpdateRequest
{
    public int Revision { get; init; }
    public string? Name { get; init; }
    public bool? Favorite { get; init; }
}

public sealed class MemoryKeeperTagMergeRequest
{
    public int SourceRevision { get; init; }
    public int TargetTagId { get; init; }
    public int TargetRevision { get; init; }
}

public sealed class MemoryKeeperFileTagMutationRequest
{
    public int ExpectedRevision { get; init; }
}

public sealed class MemoryKeeperFileTagMutationResponse
{
    public string FileId { get; init; } = string.Empty;
    public int TagId { get; init; }
    public bool Assigned { get; init; }
    public int Revision { get; init; }
}

public sealed class MemoryKeeperPendingItemDto
{
    public string FileId { get; init; } = string.Empty;
    public string? ThumbnailUrl { get; init; }
    public DateTimeOffset? CaptureDatetime { get; init; }
    public double? GpsLat { get; init; }
    public double? GpsLon { get; init; }
    public string? Country { get; init; }
    public string? Province { get; init; }
    public string? City { get; init; }
    public string? District { get; init; }
    public string? PlaceName { get; init; }
    public Guid? MemorykeeperPlaceId { get; init; }
    public int PlaceRevision { get; init; }
    public Guid? SuggestedPlaceId { get; init; }
    public string? SuggestedPlaceName { get; init; }
    public string? SuggestedMatchSource { get; init; }
}

public sealed class MemoryKeeperPendingListDto
{
    public IReadOnlyList<MemoryKeeperPendingItemDto> Items { get; init; } = [];
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}

public sealed class MemoryKeeperPendingAssignRequest
{
    public IReadOnlyList<string> FileIds { get; init; } = [];
    public Guid MemorykeeperPlaceId { get; init; }
    public IReadOnlyDictionary<string, int> ExpectedRevisions { get; init; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
}

public sealed class MemoryKeeperPendingAssignResponse
{
    public IReadOnlyList<MemoryKeeperFilePlaceUpdateApiResult> Items { get; init; } = [];
    public int AssignedCount { get; init; }
}
