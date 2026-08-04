namespace MemoryKeeper.Application.DTOs;

public sealed class MemorySearchRequest
{
    /// <summary>
    /// Natural-language memory search text. Analyzed via IMemorySearchAnalyzer.
    /// </summary>
    public string? SearchText { get; init; }

    public int? Year { get; init; }

    public Guid? PlaceId { get; init; }

    public string? Keyword { get; init; }

    /// <summary>
    /// AND filter: media must contain all selected tags.
    /// </summary>
    public IReadOnlyList<Guid>? TagIds { get; init; }

    /// <summary>
    /// When true, only favorite media are included.
    /// </summary>
    public bool FavoriteOnly { get; init; }
}
