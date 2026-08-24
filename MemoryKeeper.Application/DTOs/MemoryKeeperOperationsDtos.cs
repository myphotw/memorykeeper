namespace MemoryKeeper.Application.DTOs;

public sealed class AutoTagStatusDto
{
    public bool ServiceAvailable { get; init; }
    public bool CredentialReady { get; init; }
    public bool WorkerOnline { get; init; }
    public bool QuotaAvailable { get; init; }
    public bool MonthlyLimitReached { get; init; }
    public int QuotaWaitingCount { get; init; }
    public int WaitingCount { get; init; }
    public int ProcessingCount { get; init; }
    public int FailedCount { get; init; }
    public int TodayCompletedCount { get; init; }
    public int MonthlyUsage { get; init; }
    public int MonthlyLimit { get; init; }
    public int MonthlyRemaining { get; init; }
    public int CurationVersion { get; init; }
    public DateTimeOffset? LastProcessedAt { get; init; }
    public DateTimeOffset? LastFailureAt { get; init; }
}

public sealed class AutoTagFailedJobDto
{
    public int JobId { get; init; }
    public string FileId { get; init; } = string.Empty;
    public DateTimeOffset? FailedAt { get; init; }
    public int RetryCount { get; init; }
    public string SafeErrorCode { get; init; } = string.Empty;
    public bool Retryable { get; init; }
}

public sealed class AutoTagFailedJobListDto
{
    public IReadOnlyList<AutoTagFailedJobDto> Items { get; init; } = [];
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}

public sealed class AutoTagRetryResultDto
{
    public int RequestedCount { get; init; }
    public int RequeuedCount { get; init; }
    public int SkippedCount { get; init; }
    public int FailedCount { get; init; }
}

public sealed class MemoryKeeperResetPreviewDto
{
    public int MemorykeeperFileCount { get; init; }
    public int PlaceCount { get; init; }
    public int UserTagCount { get; init; }
    public int FavoriteCount { get; init; }
    public int MemoCount { get; init; }
    public int FileTagRelationCount { get; init; }
    public int FileTagSuppressionCount { get; init; }
    public int PendingCount { get; init; }
    public int PreservedCommonFileCount { get; init; }
    public int PreservedRawVisionCount { get; init; }
    public int SharedWithOtherServiceCount { get; init; }
    public int UploadJobCount { get; init; }
    public int ActiveUploadJobCount { get; init; }
    public int ProcessingVisionJobCount { get; init; }
    public bool ResetBlocked { get; init; }
}

public sealed class MemoryKeeperResetExecuteRequest
{
    public string Confirmation { get; init; } = string.Empty;
}

public sealed class MemoryKeeperResetExecuteResultDto
{
    public bool ResetCompleted { get; init; }
    public int AffectedFileCount { get; init; }
    public int RemovedPlaceCount { get; init; }
    public int RemovedUserTagCount { get; init; }
    public int ClearedStateCount { get; init; }
    public int PreservedCommonFileCount { get; init; }
    public int PreservedRawVisionCount { get; init; }
    public long ResetEventCursor { get; init; }
}

public enum AutoTagUserState
{
    Normal,
    MonthlyLimitReached,
    AttentionRequired,
}

public sealed class AutoTagUserStatusDto
{
    public required AutoTagStatusDto Status { get; init; }
    public AutoTagUserState State { get; init; }
    public string StateText { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string QuotaWaitingText { get; init; } = string.Empty;
}

public sealed class AutoTagFailedItemViewDto
{
    public int JobId { get; init; }
    public string FileId { get; init; } = string.Empty;
    public DateTimeOffset? FailedAt { get; init; }
    public int RetryCount { get; init; }
    public bool Retryable { get; init; }
    public string StatusText => Retryable
        ? "다시 시도할 수 있습니다."
        : "자동 분석을 다시 준비할 수 없습니다.";
}
