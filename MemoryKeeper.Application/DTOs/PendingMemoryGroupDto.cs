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

    public static string GetEffectiveLocationSummary(IEnumerable<PendingMemoryItemDto> items)
    {
        var locations = items
            .Select(item => item.GeographyText.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return locations.Count switch
        {
            0 => string.Empty,
            1 => locations[0],
            _ => "여러 장소",
        };
    }
}
