namespace MemoryKeeper.Application.DTOs;

public sealed class MemorySearchQueryResult
{
    public IReadOnlyList<MemorySearchResult> Items { get; init; } = [];

    public IReadOnlyList<MemorySearchChipDto> Chips { get; init; } = [];

    public MemorySearchRequest ResolvedRequest { get; init; } = new();
}
