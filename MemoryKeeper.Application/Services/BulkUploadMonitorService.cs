using System.Collections.Concurrent;
using MemoryKeeper.Application.DTOs.Upload;
using MemoryKeeper.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemoryKeeper.Application.Services;

/// <summary>
/// Polls multiple Upload job_ids until terminal. Adaptive interval: 1s early, 3–5s after 30s.
/// Caps concurrent status GETs to avoid API storms.
/// </summary>
public sealed class BulkUploadMonitorService
{
    public static readonly TimeSpan InitialPollInterval = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan LongPollInterval = TimeSpan.FromSeconds(4);
    public static readonly TimeSpan LongWaitThreshold = TimeSpan.FromSeconds(30);

    private readonly IUploadJobApiRepository _uploadJobApiRepository;
    private readonly ImportUploadOptions _options;
    private readonly ILogger<BulkUploadMonitorService> _logger;

    public BulkUploadMonitorService(
        IUploadJobApiRepository uploadJobApiRepository,
        IOptions<ImportUploadOptions> options,
        ILogger<BulkUploadMonitorService> logger)
    {
        _uploadJobApiRepository = uploadJobApiRepository
            ?? throw new ArgumentNullException(nameof(uploadJobApiRepository));
        _options = options?.Value ?? new ImportUploadOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Polls <paramref name="activeJobIds"/> until empty <b>and</b> <paramref name="isProducerComplete"/> is true,
    /// or until cancelled.
    /// </summary>
    public async Task MonitorAsync(
        ConcurrentDictionary<Guid, byte> activeJobIds,
        Func<bool> isProducerComplete,
        Action<UploadJobStatusDto> onStatus,
        Action<Guid, Exception>? onError = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activeJobIds);
        ArgumentNullException.ThrowIfNull(isProducerComplete);
        ArgumentNullException.ThrowIfNull(onStatus);

        var startedAt = DateTime.UtcNow;
        var maxPolls = Math.Clamp(_options.MaxConcurrentJobPolls <= 0 ? 5 : _options.MaxConcurrentJobPolls, 1, 8);

        _logger.LogInformation(
            "Bulk upload monitor started. MaxPolls={MaxPolls}",
            maxPolls);

        while (!cancellationToken.IsCancellationRequested)
        {
            var ids = activeJobIds.Keys.ToArray();
            if (ids.Length == 0)
            {
                if (isProducerComplete())
                {
                    break;
                }

                await Task.Delay(InitialPollInterval, cancellationToken).ConfigureAwait(false);
                continue;
            }

            using var pollGate = new SemaphoreSlim(maxPolls, maxPolls);
            var tasks = ids.Select(async jobId =>
            {
                await pollGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var status = await _uploadJobApiRepository
                        .GetStatusAsync(jobId, cancellationToken)
                        .ConfigureAwait(false);
                    onStatus(status);
                    if (status.IsTerminal)
                    {
                        activeJobIds.TryRemove(jobId, out _);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Bulk monitor poll failed. JobId={JobId}", jobId);
                    onError?.Invoke(jobId, ex);
                    if (IsNotFound(ex))
                    {
                        activeJobIds.TryRemove(jobId, out _);
                    }
                }
                finally
                {
                    pollGate.Release();
                }
            });

            await Task.WhenAll(tasks).ConfigureAwait(false);

            if (activeJobIds.IsEmpty && isProducerComplete())
            {
                break;
            }

            var elapsed = DateTime.UtcNow - startedAt;
            var delay = elapsed >= LongWaitThreshold ? LongPollInterval : InitialPollInterval;
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Bulk upload monitor finished.");
    }

    /// <summary>Single-job monitor kept for compatibility / resume of one id.</summary>
    public async Task<UploadJobStatusDto> MonitorOneAsync(
        Guid jobId,
        IProgress<UploadJobStatusDto>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var active = new ConcurrentDictionary<Guid, byte>();
        active[jobId] = 0;
        UploadJobStatusDto? last = null;
        await MonitorAsync(
                active,
                isProducerComplete: () => true,
                status =>
                {
                    last = status;
                    progress?.Report(status);
                },
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return last ?? throw new InvalidOperationException($"No status for job {jobId}.");
    }

    public static bool IsDuplicateCompleted(UploadJobStatusDto status) =>
        status.IsCompleted
        && !string.IsNullOrWhiteSpace(status.ProcessingLog)
        && status.ProcessingLog.Contains("DUPLICATE_FOUND", StringComparison.OrdinalIgnoreCase);

    public static bool IsNotFound(Exception exception)
    {
        var statusCode = exception.GetType().GetProperty("StatusCode")?.GetValue(exception);
        return statusCode is not null && Convert.ToInt32(statusCode) == 404;
    }
}
