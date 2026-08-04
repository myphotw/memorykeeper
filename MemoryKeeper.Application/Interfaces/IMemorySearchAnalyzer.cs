using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Application.Interfaces;

/// <summary>
/// Parses natural-language search text into structured memory filters.
/// Swap implementations (rule-based → AI) without changing MemorySearchService.
/// </summary>
public interface IMemorySearchAnalyzer
{
    Task<MemorySearchAnalysis> AnalyzeAsync(
        string searchText,
        CancellationToken cancellationToken = default);
}

public sealed class MemorySearchAnalysis
{
    public MemorySearchRequest Request { get; init; } = new();

    public IReadOnlyList<MemorySearchChipDto> Chips { get; init; } = [];
}
