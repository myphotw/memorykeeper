namespace MemoryKeeper.Application.DTOs;

public sealed class AssignMediaPlaceRequest
{
    public required Guid PlaceId { get; init; }

    public required IReadOnlyList<Guid> MediaIds { get; init; }

    /// <summary>
    /// Optional authoritative revisions captured after automatic place reclassification.
    /// When supplied, every requested media ID must have a value; stale cached revisions
    /// are never used as a fallback.
    /// </summary>
    public IReadOnlyDictionary<Guid, int>? ExpectedPlaceRevisions { get; init; }
}
