namespace MemoryKeeper.Application.DTOs;

public enum IncrementalImportClassification
{
    Existing,
    InProgress,
    Duplicate,
    New,
    Uncertain,
}

public sealed class ImportFileIdentityDto
{
    public required string FilePath { get; init; }
    public long FileSize { get; init; }
    public long LastWriteUtcTicks { get; init; }
    public string? ContentHash { get; init; }
    public bool FromCache { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class IncrementalImportItemDto
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public string? ContentHash { get; init; }
    public IncrementalImportClassification Classification { get; init; }
    public string? ExistingJobId { get; init; }
    public string? Reason { get; init; }
}

public sealed class IncrementalImportPreflightResult
{
    public required string SourceFolderPath { get; init; }
    public IReadOnlyList<IncrementalImportItemDto> Items { get; init; } = [];
    public bool BackendSnapshotComplete { get; init; }
    public string? BackendWarning { get; init; }
    public int TotalCount => Items.Count;
    public int ExistingCount => Items.Count(item => item.Classification == IncrementalImportClassification.Existing);
    public int InProgressCount => Items.Count(item => item.Classification == IncrementalImportClassification.InProgress);
    public int DuplicateCount => Items.Count(item => item.Classification == IncrementalImportClassification.Duplicate);
    public int NewCount => Items.Count(item => item.Classification == IncrementalImportClassification.New);
    public int UncertainCount => Items.Count(item => item.Classification == IncrementalImportClassification.Uncertain);
    public IReadOnlyList<IncrementalImportItemDto> UploadTargets =>
        Items.Where(item => item.Classification == IncrementalImportClassification.New).ToList();
}

public sealed class ImportPreflightProgressDto
{
    public int TotalCount { get; init; }
    public int ProcessedCount { get; init; }
    public string Stage { get; init; } = string.Empty;
    public string? CurrentFileName { get; init; }
}

public sealed class ImportBackendIdentitySnapshot
{
    public IReadOnlySet<string> ExistingContentHashes { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlySet<string> AcceptedContentHashes { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, string> SessionJobIdsByPath { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, string> SessionContentHashesByPath { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public bool IsComplete { get; init; }
    public int UnidentifiedAcceptedJobCount { get; init; }
    public string? Warning { get; init; }
}
