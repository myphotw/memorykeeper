namespace MemoryKeeper.Application.DTOs;

public sealed class PendingMemoryGroupDto
{
    /// <summary>
    /// Temporary query-only id. Not persisted.
    /// </summary>
    public Guid GroupId { get; init; }

    public string GroupName { get; init; } = string.Empty;

    public int MediaCount { get; init; }

    /// <summary>
    /// True when the group has no capture date (CapturedAt is null for all items).
    /// </summary>
    public bool HasUnknownDate { get; init; }

    public DateTimeOffset? FirstCapturedDate { get; init; }

    public DateTimeOffset? LastCapturedDate { get; init; }

    public string EstimatedCountry { get; init; } = string.Empty;

    public string EstimatedCity { get; init; } = string.Empty;

    public string EstimatedAddress { get; init; } = string.Empty;

    public string EstimatedLocationSummary { get; init; } = string.Empty;

    public string ProcessingStatus { get; init; } = "미처리";

    public IReadOnlyList<PendingMemoryItemDto> MediaItems { get; init; } = [];
}
