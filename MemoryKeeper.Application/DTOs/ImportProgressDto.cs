namespace MemoryKeeper.Application.DTOs;

public sealed class ImportProgressDto
{
    public int TotalCount { get; init; }

    public int ProcessedCount { get; init; }

    public int ImportedCount { get; init; }

    public int DuplicateCount { get; init; }

    public int FailedCount { get; init; }

    public int PendingCount { get; init; }

    public int UploadingCount { get; init; }

    public int UploadedCount { get; init; }

    public int WaitingCount { get; init; }

    public int ProcessingCount { get; init; }

    public int CompletedCount { get; init; }

    public int CancelledCount { get; init; }

    /// <summary>Files whose HTTP upload finished (accepted by server or failed/cancelled after attempt).</summary>
    public int UploadFinishedCount { get; init; }

    /// <summary>Files whose analysis reached a terminal state (completed/duplicate/failed).</summary>
    public int AnalysisFinishedCount { get; init; }

    public string? CurrentFileName { get; init; }

    /// <summary>
    /// UI stage label (e.g. WAITING, PROCESSING, or legacy SQLite stage).
    /// </summary>
    public string? CurrentStage { get; init; }

    public bool IsCompleted { get; init; }

    /// <summary>Backend job status: WAITING / PROCESSING / COMPLETED / FAILED.</summary>
    public string? BackendStatus { get; init; }

    /// <summary>Backend progress: 0 / 16 / 33 / 50 / 66 / 83 / 100.</summary>
    public int? BackendProgress { get; init; }

    public string? CurrentPlugin { get; init; }

    public string? JobId { get; init; }

    public string? LastError { get; init; }

    public string? LastFailureFileName { get; init; }

    public string? LastErrorCategory { get; init; }

    public bool IsResumedSession { get; init; }

    public bool IsStalled { get; init; }

    public bool HasPersistenceWarning { get; init; }

    public DateTimeOffset? LastStatusCheckedAt { get; init; }

    public bool IsFailed { get; init; }

    public string? StatusSummary { get; init; }

    /// <summary>0–1 upload acceptance progress.</summary>
    public double UploadProgressRatio =>
        TotalCount <= 0 ? 0 : Math.Clamp((double)UploadFinishedCount / TotalCount, 0, 1);

    /// <summary>0–1 analysis completion progress.</summary>
    public double AnalysisProgressRatio =>
        TotalCount <= 0 ? 0 : Math.Clamp((double)AnalysisFinishedCount / TotalCount, 0, 1);

    /// <summary>
    /// 0–1 ratio for the main ProgressBar — prefers analysis once uploads finish, else upload.
    /// </summary>
    public double ProgressRatio =>
        UploadFinishedCount >= TotalCount && TotalCount > 0
            ? AnalysisProgressRatio
            : UploadProgressRatio;
}
