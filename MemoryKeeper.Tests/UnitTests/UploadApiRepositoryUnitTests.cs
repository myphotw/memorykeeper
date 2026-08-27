using System.Net;
using System.Text;
using MemoryKeeper.Application.DTOs.Upload;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Infrastructure.Repositories.Api;
using MemoryKeeper.Infrastructure.Services.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class UploadApiRepositoryUnitTests
{
    [Fact]
    public async Task UploadAsync_PostsMultipart_AndMapsResponse()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"mk-upload-{Guid.NewGuid():N}.jpg");
        await File.WriteAllBytesAsync(tempFile, "fake-jpeg"u8.ToArray());

        try
        {
            var handler = new StubHandler
            {
                ResponseBody =
                    """{"id":7,"job_id":"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee","status":"WAITING","incoming_path":"incoming/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee_mk.jpg"}""",
            };

            using var provider = BuildProvider(handler);
            var repo = provider.GetRequiredService<IUploadApiRepository>();

            var result = await repo.UploadAsync(tempFile);

            Assert.Equal("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", result.JobId);
            Assert.Equal("WAITING", result.Status);
            Assert.Equal(7, result.Id);
            Assert.Equal("incoming/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee_mk.jpg", result.IncomingPath);
            Assert.False(string.IsNullOrWhiteSpace(result.Message));

            Assert.Equal(HttpMethod.Post, handler.LastMethod);
            Assert.Equal("/api/common/upload", handler.LastPath);
            Assert.Contains("multipart/form-data", handler.LastContentType, StringComparison.OrdinalIgnoreCase);
            Assert.True(handler.LastBodyLength > 0);

            var status = UploadStatusDto.FromResponse(result);
            Assert.Equal(result.JobId, status.JobId);
            Assert.Equal("WAITING", status.Status);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task UploadWithIdentity_SendsContentHashIdempotencyFields()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"mk-upload-identity-{Guid.NewGuid():N}.jpg");
        await File.WriteAllBytesAsync(tempFile, "fake-jpeg"u8.ToArray());
        var hash = new string('a', 64);
        try
        {
            var handler = new StubHandler
            {
                ResponseBody =
                    """{"id":8,"job_id":"aaaaaaaa-bbbb-cccc-dddd-ffffffffffff","status":"WAITING","client_file_id":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}""",
            };
            using var provider = BuildProvider(handler);
            var repo = provider.GetRequiredService<IUploadApiRepository>();

            await repo.UploadWithIdentityAsync(tempFile, hash, hash);

            Assert.Contains("client_file_id", handler.LastBody, StringComparison.Ordinal);
            Assert.Contains("client_content_sha256", handler.LastBody, StringComparison.Ordinal);
            Assert.Contains(hash, handler.LastBody, StringComparison.Ordinal);
            Assert.Contains("service_name", handler.LastBody, StringComparison.Ordinal);
            Assert.Contains("MemoryKeeper", handler.LastBody, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    private static ServiceProvider BuildProvider(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.Configure<TcBackendOptions>(o =>
        {
            o.ApiBaseUrl = "http://localhost:8000";
            o.Timeout = 10;
            o.RetryCount = 0;
            o.ServiceName = "MemoryKeeper";
        });
        services.AddHttpClient(BaseApiClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddSingleton<BaseApiClient>();
        services.AddSingleton<IUploadApiRepository, UploadApiRepository>();
        return services.BuildServiceProvider();
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public string ResponseBody { get; set; } = "{}";
        public HttpMethod? LastMethod { get; private set; }
        public string? LastPath { get; private set; }
        public string? LastContentType { get; private set; }
        public long LastBodyLength { get; private set; }
        public string LastBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastMethod = request.Method;
            LastPath = request.RequestUri!.AbsolutePath;
            LastContentType = request.Content?.Headers.ContentType?.ToString();
            if (request.Content is not null)
            {
                var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
                LastBodyLength = bytes.LongLength;
                LastBody = Encoding.UTF8.GetString(bytes);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}
