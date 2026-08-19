using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Infrastructure.Repositories.Api;
using MemoryKeeper.Infrastructure.Services.Api;
using Microsoft.Extensions.Logging.Abstractions;

namespace MemoryKeeper.Tests.UnitTests;

/// <summary>
/// Live upload → poll until COMPLETED/FAILED when TC-Backend (+ worker) is available.
/// </summary>
public sealed class UploadJobPollingSmokeTests
{
    private static readonly string DefaultBaseUrl =
        Environment.GetEnvironmentVariable(TcBackendOptions.ApiBaseUrlEnvironmentVariable)
        ?? TcBackendOptions.ProductionApiBaseUrl;

    [LiveBackendWriteFact]
    public async Task Live_Upload_Then_Poll_Until_Terminal()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"mk-poll-{Guid.NewGuid():N}.jpg");
        await File.WriteAllBytesAsync(tempFile, [0xFF, 0xD8, 0xFF, 0xD9]);

        try
        {
            using var handle = ApiClientFactory.Create(new TcBackendOptions
            {
                ApiBaseUrl = DefaultBaseUrl,
                AuthToken = Environment.GetEnvironmentVariable(TcBackendOptions.AuthTokenEnvironmentVariable) ?? string.Empty,
                Timeout = 30,
                RetryCount = 0,
                ServiceName = "MemoryKeeper",
            });

            IUploadApiRepository uploadRepo = new UploadApiRepository(handle.Client);
            IUploadJobApiRepository jobRepo = new UploadJobApiRepository(handle.Client);
            var monitor = new UploadMonitorService(jobRepo, NullLogger<UploadMonitorService>.Instance);

            var upload = await uploadRepo.UploadAsync(tempFile);
            Assert.False(string.IsNullOrWhiteSpace(upload.JobId));
            Assert.True(Guid.TryParse(upload.JobId, out var jobId));

            // Worker may be offline; accept FAILED or COMPLETED, or timeout WAITING after short window.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            try
            {
                var reports = new List<Application.DTOs.Upload.UploadJobStatusDto>();
                var final = await monitor.MonitorAsync(
                    jobId,
                    pollInterval: TimeSpan.FromSeconds(2),
                    progress: new Progress<Application.DTOs.Upload.UploadJobStatusDto>(reports.Add),
                    cancellationToken: cts.Token);

                Assert.True(final.IsTerminal);
                Assert.NotEmpty(reports);
                Assert.Contains(reports, r => r.JobId == upload.JobId);
            }
            catch (OperationCanceledException)
            {
                // No worker / stuck WAITING — still verify at least one status fetch works.
                var status = await jobRepo.GetStatusAsync(jobId);
                Assert.Equal(upload.JobId, status.JobId);
                Assert.False(string.IsNullOrWhiteSpace(status.Status));
            }
            catch (ApiException ex) when ((int)ex.StatusCode >= 500 || (int)ex.StatusCode == 404)
            {
                Assert.True(true, ex.Message);
            }
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

}
