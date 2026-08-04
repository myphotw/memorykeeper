namespace MemoryKeeper.Application.DTOs;

public sealed class MediaImportResult
{
    public required string SourceFolderPath { get; init; }

    public required Guid StorageId { get; init; }

    public int ScannedCount { get; init; }

    public int ImportedCount { get; init; }

    public int DuplicateCount { get; init; }

    public int FailedCount { get; init; }

    public IReadOnlyList<MediaImportItemResult> Items { get; init; } = [];
}
