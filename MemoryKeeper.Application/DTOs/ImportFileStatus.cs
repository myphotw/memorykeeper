namespace MemoryKeeper.Application.DTOs;

/// <summary>Per-file client status for Phase 3B parallel Import.</summary>
public enum ImportFileStatus
{
    Pending = 0,
    Uploading = 1,
    Uploaded = 2,
    Waiting = 3,
    Processing = 4,
    Completed = 5,
    Failed = 6,
    Cancelled = 7,
    Duplicate = 8,
}
