namespace MemoryKeeper.Application.DTOs;

public sealed class AssignMediaPlaceRequest
{
    public required Guid PlaceId { get; init; }

    public required IReadOnlyList<Guid> MediaIds { get; init; }
}
