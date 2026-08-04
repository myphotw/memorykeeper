namespace MemoryKeeper.Application.DTOs;

public sealed class PlaceReclassificationResult
{
    public Guid PlaceId { get; init; }

    public int AssignedCount { get; init; }

    /// <summary>Subset of AssignedCount that previously belonged to another place.</summary>
    public int ReassignedFromOtherCount { get; init; }

    public int UnassignedCount { get; init; }
}
