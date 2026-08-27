namespace MemoryKeeper.Application.DTOs;

/// <summary>Mutable per-file Import tracking state (upload + analysis).</summary>
public sealed class ImportFileState
{
    public required string LocalFilePath { get; init; }

    public required string FileName { get; init; }

    public Guid? JobId { get; set; }

    public string? ContentHash { get; set; }

    public ImportFileStatus Status { get; set; } = ImportFileStatus.Pending;

    public int Progress { get; set; }

    public string? CurrentPlugin { get; set; }

    public string? ErrorMessage { get; set; }

    public string? ErrorCategory { get; set; }

    public bool HasPersistenceWarning { get; set; }

    public DateTimeOffset? UploadedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string? IncomingPath { get; set; }

    public bool UploadAccepted => JobId is not null;

    public bool IsTerminal =>
        Status is ImportFileStatus.Completed
            or ImportFileStatus.Failed
            or ImportFileStatus.Cancelled
            or ImportFileStatus.Duplicate;

    public bool IsUploadFinished =>
        Status is not ImportFileStatus.Pending and not ImportFileStatus.Uploading;

    public bool IsAnalysisTerminal =>
        Status is ImportFileStatus.Completed
            or ImportFileStatus.Failed
            or ImportFileStatus.Duplicate;
}
