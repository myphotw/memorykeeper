namespace MemoryKeeper.Application.DTOs;

public sealed class ImportProgressDto
{
    public int TotalCount { get; init; }

    public int ProcessedCount { get; init; }

    public int ImportedCount { get; init; }

    public int DuplicateCount { get; init; }

    public int FailedCount { get; init; }

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

    public bool IsFailed { get; init; }

    /// <summary>
    /// 0–1 ratio. Prefers backend <see cref="BackendProgress"/> when present.
    /// </summary>
    public double ProgressRatio =>
        BackendProgress is int bp
            ? Math.Clamp(bp / 100.0, 0, 1)
            : TotalCount <= 0
                ? 0
                : (double)ProcessedCount / TotalCount;
}
