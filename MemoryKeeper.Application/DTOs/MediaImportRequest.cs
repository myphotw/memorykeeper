namespace MemoryKeeper.Application.DTOs;

public sealed class MediaImportRequest
{
    public required string SourceFolderPath { get; init; }

    public required Guid StorageId { get; init; }
}
