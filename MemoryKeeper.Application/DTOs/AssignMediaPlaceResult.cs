namespace MemoryKeeper.Application.DTOs;

public sealed class AssignMediaPlaceResult
{
    public int UpdatedCount { get; init; }

    public Guid PlaceId { get; init; }

    public IReadOnlyList<Guid> UpdatedMediaIds { get; init; } = [];

    public int ConflictCount { get; init; }

    public int RevisionRefreshFailureCount { get; init; }
}
