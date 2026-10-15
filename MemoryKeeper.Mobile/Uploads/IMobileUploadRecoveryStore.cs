namespace MemoryKeeper.Mobile.Uploads;

public sealed record MobileUploadRecoveryRecord(
    Guid JobId,
    string ClientFileId,
    string FileName,
    DateTimeOffset AcceptedAt);

public interface IMobileUploadRecoveryStore
{
    Task<IReadOnlyList<MobileUploadRecoveryRecord>> LoadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        IReadOnlyList<MobileUploadRecoveryRecord> records,
        CancellationToken cancellationToken = default);
}
