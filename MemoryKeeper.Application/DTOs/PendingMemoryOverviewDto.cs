namespace MemoryKeeper.Application.DTOs;

public sealed class PendingMemoryOverviewDto
{
    public IReadOnlyList<PendingMemoryItemDto> Items { get; init; } = [];

    public IReadOnlyList<PendingMemoryGroupDto> Groups { get; init; } = [];

    /// <summary>
    /// GPS exists but Place assignment failed / missing. Auto-reclassification candidates.
    /// </summary>
    public IReadOnlyList<PendingMemoryItemDto> ReclassificationCandidates { get; init; } = [];

    public int Total { get; init; }

    public int Page { get; init; }

    public int PageSize { get; init; }

    public bool HasMore => Items.Count < Total;
}
