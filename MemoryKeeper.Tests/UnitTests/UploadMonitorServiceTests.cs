using System.Net;
using System.Text;
using MemoryKeeper.Application.DTOs.Upload;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Infrastructure.Repositories.Api;
using MemoryKeeper.Infrastructure.Services.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class UploadMonitorServiceTests
{
    [Fact]
    public async Task MonitorAsync_Polls_Until_Completed_And_Reports_Progress()
    {
        var jobId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var repo = new SequenceUploadJobRepository(jobId, new[]
        {
            Status(jobId, UploadJobStatusDto.Waiting, 0, null),
            Status(jobId, UploadJobStatusDto.Processing, 16, "HashPlugin"),
            Status(jobId, UploadJobStatusDto.Processing, 33, "PreviewPlugin"),
            Status(jobId, UploadJobStatusDto.Processing, 50, "StoragePlugin"),
            Status(jobId, UploadJobStatusDto.Processing, 66, "MetadataPlugin"),
            Status(jobId, UploadJobStatusDto.Processing, 83, "ExifPlugin"),
            Status(jobId, UploadJobStatusDto.Completed, 100, "GpsPlugin"),
        });

        var reports = new List<UploadJobStatusDto>();
        var monitor = new UploadMonitorService(repo, NullLogger<UploadMonitorService>.Instance);

        var final = await monitor.MonitorAsync(
            jobId,
            pollInterval: TimeSpan.FromMilliseconds(1),
            progress: new Progress<UploadJobStatusDto>(reports.Add));

        Assert.Equal(UploadJobStatusDto.Completed, final.Status);
        Assert.Equal(100, final.Progress);
        Assert.Equal(7, reports.Count);
        Assert.Equal(new[] { 0, 16, 33, 50, 66, 83, 100 }, reports.Select(r => r.Progress).ToArray());
        Assert.Equal("GpsPlugin", final.CurrentPlugin);
        Assert.Equal(7, repo.CallCount);
    }

    [Fact]
    public async Task MonitorAsync_Stops_On_Failed()
    {
        var jobId = Guid.NewGuid();
        var repo = new SequenceUploadJobRepository(jobId, new[]
        {
            Status(jobId, UploadJobStatusDto.Processing, 33, "PreviewPlugin"),
            Status(jobId, UploadJobStatusDto.Failed, 33, "PreviewPlugin", "boom"),
        });

        var monitor = new UploadMonitorService(repo, NullLogger<UploadMonitorService>.Instance);
        var final = await monitor.MonitorAsync(jobId, TimeSpan.FromMilliseconds(1));

        Assert.True(final.IsFailed);
        Assert.Equal(33, final.Progress);
        Assert.Equal("boom", final.LastError);
    }

    [Fact]
    public async Task UploadJobApiRepository_GetStatus_Maps_Json()
    {
        var jobId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var handler = new StubHandler
        {
            ResponseBody =
                """{"job_id":"11111111-1111-1111-1111-111111111111","status":"PROCESSING","progress":50,"current_plugin":"StoragePlugin","processing_log":"x","retry_count":0,"last_error":null}""",
        };

        using var provider = BuildProvider(handler);
        var repo = provider.GetRequiredService<IUploadJobApiRepository>();
        var status = await repo.GetStatusAsync(jobId);

        Assert.Equal(jobId.ToString("D"), status.JobId);
        Assert.Equal(UploadJobStatusDto.Processing, status.Status);
        Assert.Equal(50, status.Progress);
        Assert.Equal("StoragePlugin", status.CurrentPlugin);
        Assert.Equal($"/api/common/upload/jobs/{jobId:D}", handler.LastPath);
    }

    private static UploadJobStatusDto Status(
        Guid jobId,
        string status,
        int progress,
        string? plugin,
        string? error = null) => new()
    {
        JobId = jobId.ToString("D"),
        Status = status,
        Progress = progress,
        CurrentPlugin = plugin,
        LastError = error,
    };

    private static ServiceProvider BuildProvider(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.Configure<TcBackendOptions>(o =>
        {
            o.ApiBaseUrl = "http://localhost:8000";
            o.Timeout = 10;
            o.RetryCount = 0;
        });
        services.AddHttpClient(BaseApiClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddSingleton<BaseApiClient>();
        services.AddSingleton<IUploadJobApiRepository, UploadJobApiRepository>();
        return services.BuildServiceProvider();
    }

    private sealed class SequenceUploadJobRepository : IUploadJobApiRepository
    {
        private readonly Queue<UploadJobStatusDto> _queue;

        public SequenceUploadJobRepository(Guid jobId, IEnumerable<UploadJobStatusDto> sequence)
        {
            _ = jobId;
            _queue = new Queue<UploadJobStatusDto>(sequence);
        }

        public int CallCount { get; private set; }

        public Task<UploadJobStatusDto> GetStatusAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (_queue.Count == 0)
            {
                throw new InvalidOperationException("No more status samples.");
            }

            return Task.FromResult(_queue.Dequeue());
        }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public string ResponseBody { get; set; } = "{}";
        public string? LastPath { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastPath = request.RequestUri!.AbsolutePath;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json"),
            });
        }
    }
}
