using MemoryKeeper.Domain.Enums;

namespace MemoryKeeper.Application.DTOs;

public sealed class MediaImportItemResult
{
    public required string OriginalPath { get; init; }

    public required string FileName { get; init; }

    public MediaType? MediaType { get; init; }

    public MediaStatus Status { get; init; }

    public Guid? MediaId { get; init; }

    public string? ContentHash { get; init; }

    public string? JobId { get; init; }

    public string? RelativePath { get; init; }

    public string? ErrorMessage { get; init; }

    public string? ErrorCategory { get; init; }
}
