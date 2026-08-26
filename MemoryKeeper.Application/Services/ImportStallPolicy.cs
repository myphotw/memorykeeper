using MemoryKeeper.Application.DTOs.Upload;

namespace MemoryKeeper.Application.Services;

public sealed class ImportStallPolicy
{
    private readonly TimeSpan _threshold;
    private DateTimeOffset _lastTerminalProgressAt;

    public ImportStallPolicy(TimeSpan threshold, DateTimeOffset? startedAt = null)
    {
        _threshold = threshold;
        _lastTerminalProgressAt = startedAt ?? DateTimeOffset.UtcNow;
    }

    public bool Observe(UploadJobStatusDto status, int activeJobCount, DateTimeOffset now)
    {
        if (status.IsTerminal)
        {
            _lastTerminalProgressAt = now;
            return false;
        }

        return IsStalled(activeJobCount, _lastTerminalProgressAt, now, _threshold);
    }

    public static bool IsStalled(
        int activeJobCount,
        DateTimeOffset lastTerminalProgressAt,
        DateTimeOffset now,
        TimeSpan threshold) =>
        activeJobCount > 0 && now - lastTerminalProgressAt >= threshold;
}
