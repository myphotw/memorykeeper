namespace MemoryKeeper.Application.DTOs;

public sealed class MediaDto
{
    public Guid Id { get; init; }

    public string FileName { get; init; } = string.Empty;

    public string RelativePath { get; init; } = string.Empty;

    public DateTimeOffset? CapturedAt { get; init; }
}
