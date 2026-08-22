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
