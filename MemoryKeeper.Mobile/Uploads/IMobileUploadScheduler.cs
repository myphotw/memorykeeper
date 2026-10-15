namespace MemoryKeeper.Mobile.Uploads;

public interface IMobileUploadScheduler
{
    DateTimeOffset UtcNow { get; }

    Task DelayBeforeNextJobPollAsync(CancellationToken cancellationToken);
}

public sealed class MobileUploadScheduler : IMobileUploadScheduler
{
    internal static readonly TimeSpan JobPollInterval = TimeSpan.FromSeconds(2);

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public Task DelayBeforeNextJobPollAsync(CancellationToken cancellationToken) =>
        Task.Delay(JobPollInterval, cancellationToken);
}
