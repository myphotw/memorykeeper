namespace MemoryKeeper.Application.DTOs;

public sealed class AssignMediaPlaceResult
{
    public int UpdatedCount { get; init; }

    public Guid PlaceId { get; init; }

    public IReadOnlyList<Guid> UpdatedMediaIds { get; init; } = [];
}
