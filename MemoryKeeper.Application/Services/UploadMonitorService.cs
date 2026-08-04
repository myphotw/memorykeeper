using MemoryKeeper.Application.DTOs.Upload;
using MemoryKeeper.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Application.Services;

/// <summary>
/// Polls TC-Backend Upload Job status every 5 seconds until COMPLETED or FAILED.
/// Reports backend <c>progress</c> (16/33/50/66/83/100) and <c>current_plugin</c>.
/// </summary>
public sealed class UploadMonitorService
{
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(5);

    private readonly IUploadJobApiRepository _uploadJobApiRepository;
    private readonly ILogger<UploadMonitorService> _logger;

    public UploadMonitorService(
        IUploadJobApiRepository uploadJobApiRepository,
        ILogger<UploadMonitorService> logger)
    {
        _uploadJobApiRepository = uploadJobApiRepository
            ?? throw new ArgumentNullException(nameof(uploadJobApiRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Polls until terminal status. Invokes <paramref name="progress"/> on each poll.
    /// </summary>
    public Task<UploadJobStatusDto> MonitorAsync(
        Guid jobId,
        IProgress<UploadJobStatusDto>? progress = null,
        CancellationToken cancellationToken = default) =>
        MonitorAsync(jobId, DefaultPollInterval, progress, cancellationToken);

    public async Task<UploadJobStatusDto> MonitorAsync(
        Guid jobId,
        TimeSpan pollInterval,
        IProgress<UploadJobStatusDto>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (pollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(pollInterval), "Poll interval must be positive.");
        }

        _logger.LogInformation("Upload monitor started. JobId={JobId}, Interval={Interval}", jobId, pollInterval);

        UploadJobStatusDto? last = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            last = await _uploadJobApiRepository
                .GetStatusAsync(jobId, cancellationToken)
                .ConfigureAwait(false);

            progress?.Report(last);

            _logger.LogInformation(
                "Upload poll. JobId={JobId}, Status={Status}, Progress={Progress}, Plugin={Plugin}",
                last.JobId,
                last.Status,
                last.Progress,
                last.CurrentPlugin);

            if (last.IsTerminal)
            {
                _logger.LogInformation(
                    "Upload monitor finished. JobId={JobId}, Status={Status}, Progress={Progress}",
                    last.JobId,
                    last.Status,
                    last.Progress);
                return last;
            }

            await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
        }
    }
}
