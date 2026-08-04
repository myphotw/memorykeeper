namespace MemoryKeeper.Application.DTOs;

public sealed class PendingMemoryOverviewDto
{
    public IReadOnlyList<PendingMemoryGroupDto> Groups { get; init; } = [];

    /// <summary>
    /// GPS exists but Place assignment failed / missing. Auto-reclassification candidates.
    /// </summary>
    public IReadOnlyList<PendingMemoryItemDto> ReclassificationCandidates { get; init; } = [];
}
