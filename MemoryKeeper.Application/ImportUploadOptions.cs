namespace MemoryKeeper.Application;

/// <summary>Import upload concurrency (bound from TcBackend:MaxConcurrentUploads).</summary>
public sealed class ImportUploadOptions
{
    public const string SectionName = "TcBackend";

    /// <summary>Max parallel HTTP uploads. Default 3; must stay ≤ 3 for Phase 3B.</summary>
    public int MaxConcurrentUploads { get; set; } = 3;

    /// <summary>Max parallel job status GETs inside the bulk monitor.</summary>
    public int MaxConcurrentJobPolls { get; set; } = 5;

    public int ClampMaxConcurrentUploads() =>
        Math.Clamp(MaxConcurrentUploads <= 0 ? 3 : MaxConcurrentUploads, 1, 3);
}
