using System.Net;
using MemoryKeeper.Infrastructure.Services.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class VideoPlaybackCacheTests
{
    private const string Original = "https://backend.test/api/common/gallery/42/original";

    [Fact]
    public async Task CompletedFileIsReusedAndUsesExistingAuthenticationHandler()
    {
        var calls = 0;
        await using var scope = new CacheScope(request =>
        {
            calls++;
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("video-test-token", request.Headers.Authorization?.Parameter);
            return Ok();
        });
        var id = Guid.NewGuid();
        using var first = await scope.Cache.AcquireAsync(id, Original, ".mp4", 4);
        using var second = await scope.Cache.AcquireAsync(id, Original, ".mp4", 4);
        Assert.Equal(first.Path, second.Path);
        Assert.Equal(4, new FileInfo(first.Path).Length);
        Assert.Equal(1, calls);
        Assert.Empty(Directory.GetFiles(scope.Root, "*.part"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task HttpFailureNeverPublishesCacheFile(HttpStatusCode status)
    {
        await using var scope = new CacheScope(_ => new HttpResponseMessage(status));
        var error = await Assert.ThrowsAsync<ApiException>(() => scope.Cache.AcquireAsync(Guid.NewGuid(), Original, ".mp4", null));
        Assert.Equal(status, error.StatusCode);
        Assert.Empty(Directory.GetFiles(scope.Root));
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(4, 10L)]
    public async Task EmptyOrShortDownloadNeverPublishesCacheFile(int length, long? expected)
    {
        await using var scope = new CacheScope(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[length]),
        });
        await Assert.ThrowsAsync<IOException>(() => scope.Cache.AcquireAsync(Guid.NewGuid(), Original, ".mp4", expected));
        Assert.Empty(Directory.GetFiles(scope.Root));
    }

    [Fact]
    public async Task CancellationRemovesPartialAndNextAttemptDownloadsAgain()
    {
        var body = new BlockingContent();
        var calls = 0;
        await using var scope = new CacheScope(_ => ++calls == 1
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = body } : Ok());
        var id = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        var download = scope.Cache.AcquireAsync(id, Original, ".mp4", 4, cts.Token);
        await body.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(Directory.GetFiles(scope.Root, "*.part"));
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => download);
        Assert.Empty(Directory.GetFiles(scope.Root));
        using var completed = await scope.Cache.AcquireAsync(id, Original, ".mp4", 4);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task CleanupProtectsPlayingFileAndRemovesOversizedFileAfterRelease()
    {
        await using var scope = new CacheScope(_ => Ok(), maxBytes: 2, maxEntries: 0);
        var lease = await scope.Cache.AcquireAsync(Guid.NewGuid(), Original, ".mp4", 4);
        try
        {
            await scope.Cache.TrimAsync();
            Assert.True(File.Exists(lease.Path));
        }
        finally { lease.Dispose(); }
        await scope.Cache.TrimAsync();
        Assert.False(File.Exists(lease.Path));
    }

    [Fact]
    public async Task MediaIdentityPreventsCollisionsAndEntryLimitEvictsReleasedFiles()
    {
        await using var scope = new CacheScope(_ => Ok(), maxEntries: 1);
        var first = await scope.Cache.AcquireAsync(Guid.NewGuid(), Original, ".mp4", 4);
        using var second = await scope.Cache.AcquireAsync(Guid.NewGuid(), Original, ".mp4", 4);
        Assert.NotEqual(first.Path, second.Path);
        Assert.True(File.Exists(first.Path));
        first.Dispose();
        await scope.Cache.TrimAsync();
        Assert.False(File.Exists(first.Path));
        Assert.True(File.Exists(second.Path));
    }

    private static HttpResponseMessage Ok() => new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3, 4]) };

    private sealed class CacheScope : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        public string Root { get; } = Directory.CreateTempSubdirectory("mk-video-tests-").FullName;
        public VideoPlaybackCache Cache { get; }
        public CacheScope(Func<HttpRequestMessage, HttpResponseMessage> send, long maxBytes = 1024, int maxEntries = 3)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddTcBackendApiClient(_ => { });
            // Override deployment env values: tests never contact the live backend.
            services.PostConfigure<TcBackendOptions>(options =>
            {
                options.ApiBaseUrl = "https://backend.test";
                options.AuthToken = "video-test-token";
                options.RetryCount = 0;
            });
            services.AddHttpClient(BaseApiClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => new Handler(send));
            _provider = services.BuildServiceProvider();
            Cache = new VideoPlaybackCache(_provider.GetRequiredService<BaseApiClient>(),
                NullLogger<VideoPlaybackCache>.Instance, Root, maxBytes, maxEntries);
        }
        public async ValueTask DisposeAsync()
        {
            await Cache.TrimAsync();
            await _provider.DisposeAsync();
            Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(send(request));
    }

    private sealed class BlockingContent : HttpContent
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken token)
        {
            await stream.WriteAsync(new byte[] { 1, 2 }, token);
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        }
    }
}
