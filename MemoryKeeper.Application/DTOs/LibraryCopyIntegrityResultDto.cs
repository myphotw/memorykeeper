namespace MemoryKeeper.Application.DTOs;

public sealed class LibraryCopyIntegrityResultDto
{
    public bool Succeeded { get; init; }

    public string Message { get; init; } = string.Empty;

    public int MediaChecked { get; init; }

    public int MissingFiles { get; init; }

    public int PathMismatches { get; init; }

    public int DuplicateFileGroups { get; init; }

    public int OrphanFiles { get; init; }

    public int DeletedDuplicateFiles { get; init; }

    public int RepairedRelativePaths { get; init; }

    public int DeletedEmptyFolders { get; init; }

    public bool RepairApplied { get; init; }
}
