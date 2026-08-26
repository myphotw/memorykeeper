namespace MemoryKeeper.Application;

/// <summary>Import upload concurrency (bound from TcBackend:MaxConcurrentUploads).</summary>
public sealed class ImportUploadOptions
{
    public const string SectionName = "TcBackend";

    /// <summary>Max parallel HTTP uploads. Default 3; must stay ≤ 3 for Phase 3B.</summary>
    public int MaxConcurrentUploads { get; set; } = 3;

    /// <summary>Max parallel job status GETs inside the bulk monitor.</summary>
    public int MaxConcurrentJobPolls { get; set; } = 5;

    /// <summary>Accepted jobs accumulated before writing a full session checkpoint.</summary>
    public int SessionCheckpointBatchSize { get; set; } = 25;

    /// <summary>Maximum delay before dirty session state is checkpointed.</summary>
    public int SessionCheckpointIntervalSeconds { get; set; } = 2;

    /// <summary>Minutes without terminal progress before active NAS work is reported as stalled.</summary>
    public int StalledThresholdMinutes { get; set; } = 10;

    public int ClampMaxConcurrentUploads() =>
        Math.Clamp(MaxConcurrentUploads <= 0 ? 3 : MaxConcurrentUploads, 1, 3);

    public int ClampSessionCheckpointBatchSize() =>
        Math.Clamp(SessionCheckpointBatchSize <= 0 ? 25 : SessionCheckpointBatchSize, 1, 500);

    public TimeSpan GetSessionCheckpointInterval() =>
        TimeSpan.FromSeconds(Math.Clamp(SessionCheckpointIntervalSeconds <= 0 ? 2 : SessionCheckpointIntervalSeconds, 1, 30));

    public TimeSpan GetStalledThreshold() =>
        TimeSpan.FromMinutes(Math.Clamp(StalledThresholdMinutes <= 0 ? 10 : StalledThresholdMinutes, 1, 120));
}
