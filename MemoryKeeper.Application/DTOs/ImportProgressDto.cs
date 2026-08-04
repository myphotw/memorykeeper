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
    /// UI stage label (e.g. 메타데이터 분석, 사진 복사). Does not change import pipeline.
    /// </summary>
    public string? CurrentStage { get; init; }

    public bool IsCompleted { get; init; }

    public double ProgressRatio =>
        TotalCount <= 0 ? 0 : (double)ProcessedCount / TotalCount;
}
