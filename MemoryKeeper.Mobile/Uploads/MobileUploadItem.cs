using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MemoryKeeper.Mobile.Uploads;

public enum MobileUploadItemState
{
    Queued,
    Hashing,
    CheckingDuplicate,
    Uploading,
    ServerProcessing,
    Completed,
    Duplicate,
    Failed,
    Canceled,
    TrackingStopped,
}

public sealed class MobileUploadItem : INotifyPropertyChanged
{
    private MobileUploadItemState _state;
    private string? _message;
    private string? _failureCode;
    private Guid? _jobId;
    private long _uploadedBytes;
    private int _serverProgress;

    public MobileUploadItem(MobileSelectedMediaFile source)
        : this(new MobileUploadFile(source), source.FileName, source.ContentType, source.FileSize)
    {
    }

    internal MobileUploadItem(
        MobileUploadFile uploadFile,
        string fileName,
        string contentType,
        long fileSize)
    {
        UploadFile = uploadFile;
        ClientFileId = uploadFile.ClientFileId;
        FileName = fileName;
        ContentType = contentType;
        FileSize = fileSize;
        _state = MobileUploadItemState.Queued;
    }

    private MobileUploadItem(MobileUploadRecoveryRecord recovery)
    {
        ClientFileId = recovery.ClientFileId;
        FileName = recovery.FileName;
        ContentType = "application/octet-stream";
        FileSize = 0;
        _jobId = recovery.JobId;
        AcceptedAt = recovery.AcceptedAt;
        _state = MobileUploadItemState.ServerProcessing;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal MobileUploadFile? UploadFile { get; }

    public string ClientFileId { get; }

    public string FileName { get; }

    public string ContentType { get; }

    public long FileSize { get; }

    public string FileSizeText => FormatFileSize(FileSize);

    public bool IsVideo => ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase);

    public string MediaGlyph => IsVideo ? "▶" : "▧";

    public Guid? JobId => _jobId;

    public DateTimeOffset? AcceptedAt { get; private set; }

    public MobileUploadItemState State => _state;

    public string? FailureCode => _failureCode;

    public string? Message => _message;

    public long UploadedBytes => _uploadedBytes;

    public int ServerProgress => _serverProgress;

    public double TransferProgress => FileSize > 0
        ? Math.Clamp((double)_uploadedBytes / FileSize, 0d, 1d)
        : 0d;

    public bool IsTransferProgressVisible => State == MobileUploadItemState.Uploading
                                             && FileSize > 0;

    public bool IsMessageVisible => !string.IsNullOrWhiteSpace(Message);

    public bool CanCancel => State is MobileUploadItemState.Queued
        or MobileUploadItemState.Hashing
        or MobileUploadItemState.CheckingDuplicate
        or MobileUploadItemState.Uploading
        or MobileUploadItemState.ServerProcessing;

    public bool CanRetry => (JobId.HasValue
                             && State is (MobileUploadItemState.TrackingStopped or MobileUploadItemState.Failed)
                             && !string.Equals(
                                 FailureCode,
                                 MobileUploadErrorCodes.ContentSha256Mismatch,
                                 StringComparison.Ordinal))
                            || (JobId is null
                                && UploadFile is not null
                                && State is (MobileUploadItemState.Failed or MobileUploadItemState.Canceled));

    public string RetryText => JobId.HasValue
        ? "상태 다시 확인"
        : "다시 시도";

    public string StatusText => State switch
    {
        MobileUploadItemState.Queued => "대기 중",
        MobileUploadItemState.Hashing => "파일 확인 중",
        MobileUploadItemState.CheckingDuplicate => "중복 확인 중",
        MobileUploadItemState.Uploading when FileSize > 0 =>
            $"업로드 중 {Math.Round(TransferProgress * 100):0}%",
        MobileUploadItemState.Uploading => "업로드 중",
        MobileUploadItemState.ServerProcessing when ServerProgress > 0 =>
            $"서버 처리 중 {ServerProgress}%",
        MobileUploadItemState.ServerProcessing => "서버 처리 중",
        MobileUploadItemState.Completed => "완료",
        MobileUploadItemState.Duplicate => "이미 보관된 파일",
        MobileUploadItemState.Failed => "실패",
        MobileUploadItemState.Canceled => "취소됨",
        MobileUploadItemState.TrackingStopped => "서버 상태 확인 중단됨",
        _ => string.Empty,
    };

    internal static MobileUploadItem FromRecovery(MobileUploadRecoveryRecord recovery) => new(recovery);

    internal void SetState(
        MobileUploadItemState state,
        string? message = null,
        string? failureCode = null)
    {
        _state = state;
        _message = message;
        _failureCode = failureCode;
        NotifyAll();
    }

    internal void SetTransferProgress(long bytesSent)
    {
        _uploadedBytes = Math.Max(_uploadedBytes, Math.Max(0, bytesSent));
        OnPropertyChanged(nameof(UploadedBytes));
        OnPropertyChanged(nameof(TransferProgress));
        OnPropertyChanged(nameof(StatusText));
    }

    internal void SetAcceptedJob(Guid jobId, DateTimeOffset acceptedAt)
    {
        _jobId = jobId;
        AcceptedAt = acceptedAt;
        OnPropertyChanged(nameof(JobId));
        OnPropertyChanged(nameof(AcceptedAt));
        OnPropertyChanged(nameof(CanRetry));
    }

    internal void SetServerProgress(int progress)
    {
        _serverProgress = Math.Clamp(progress, 0, 100);
        OnPropertyChanged(nameof(ServerProgress));
        OnPropertyChanged(nameof(StatusText));
    }

    internal void ResetForRetry()
    {
        _uploadedBytes = 0;
        _serverProgress = 0;
        _message = null;
        _failureCode = null;
        _state = MobileUploadItemState.Queued;
        OnPropertyChanged(nameof(UploadedBytes));
        OnPropertyChanged(nameof(TransferProgress));
        OnPropertyChanged(nameof(ServerProgress));
        NotifyAll();
    }

    private void NotifyAll()
    {
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(FailureCode));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(IsMessageVisible));
        OnPropertyChanged(nameof(IsTransferProgressVisible));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(RetryText));
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "크기 확인 불가";
        }

        string[] units = ["B", "KB", "MB", "GB"];
        var size = (double)bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.#} {units[unit]}";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
