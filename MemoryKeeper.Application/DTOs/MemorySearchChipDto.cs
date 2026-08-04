namespace MemoryKeeper.Application.DTOs;

public enum MemorySearchChipKind
{
    Year = 0,
    Place = 1,
    Tag = 2,
    Favorite = 3
}

public sealed class MemorySearchChipDto
{
    public required string Label { get; init; }

    public MemorySearchChipKind Kind { get; init; }
}
