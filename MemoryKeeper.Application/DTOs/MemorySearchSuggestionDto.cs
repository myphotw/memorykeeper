namespace MemoryKeeper.Application.DTOs;

public enum MemorySearchSuggestionKind
{
    Place = 0,
    Tag = 1
}

public sealed class MemorySearchSuggestionDto
{
    public required string Text { get; init; }

    public MemorySearchSuggestionKind Kind { get; init; }

    public string KindLabel => Kind == MemorySearchSuggestionKind.Place ? "장소" : "태그";
}
