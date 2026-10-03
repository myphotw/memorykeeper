using MemoryKeeper.Application.Services;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class BoundedThumbnailMemoryCacheTests
{
    [Fact]
    public async Task SameThumbnailTwice_FetchesOnlyOnce()
    {
        var cache = CreateCache();
        var fetches = 0;

        var first = await cache.GetOrLoadAsync("file:a", _ =>
        {
            fetches++;
            return Task.FromResult<byte[]?>([1, 2, 3]);
        });
        var second = await cache.GetOrLoadAsync("file:a", _ =>
        {
            fetches++;
            return Task.FromResult<byte[]?>([9]);
        });

        Assert.Equal(1, fetches);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task CacheHit_DoesNotInvokeLoaderAgain()
    {
        var cache = CreateCache();
        await cache.GetOrLoadAsync("file:a", _ => Task.FromResult<byte[]?>([1]));

        var called = false;
        var cached = await cache.GetOrLoadAsync("file:a", _ =>
        {
            called = true;
            return Task.FromResult<byte[]?>([2]);
        });

        Assert.False(called);
        Assert.Equal(new byte[] { 1 }, cached);
    }

    [Fact]
    public async Task PreloadThenImageRequest_WithSameKeyDoesNotFetchTwice()
    {
        var cache = CreateCache();
        var fetches = 0;
        await cache.GetOrLoadAsync("file:preloaded", _ =>
        {
            fetches++;
            return Task.FromResult<byte[]?>([1, 2, 3]);
        });

        var imageBytes = await cache.GetOrLoadAsync("file:preloaded", _ =>
        {
            fetches++;
            return Task.FromResult<byte[]?>([9]);
        });

        Assert.Equal(1, fetches);
        Assert.Equal(new byte[] { 1, 2, 3 }, imageBytes);
    }

    [Fact]
    public async Task ByteLimit_EvictsLeastRecentlyUsedEntry()
    {
        var cache = new BoundedThumbnailMemoryCache(maxEntries: 10, maxBytes: 4, maxConcurrentLoads: 2);
        await cache.GetOrLoadAsync("file:a", _ => Task.FromResult<byte[]?>([1, 1]));
        await cache.GetOrLoadAsync("file:b", _ => Task.FromResult<byte[]?>([2, 2]));
        Assert.True(cache.TryGet("file:a", out _));

        await cache.GetOrLoadAsync("file:c", _ => Task.FromResult<byte[]?>([3, 3]));

        Assert.True(cache.TryGet("file:a", out _));
        Assert.False(cache.TryGet("file:b", out _));
        Assert.True(cache.TryGet("file:c", out _));
        Assert.Equal(4, cache.TotalBytes);
    }

    [Fact]
    public async Task ConcurrentSameThumbnail_SharesOneInFlightLoad()
    {
        var cache = CreateCache();
        var response = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fetches = 0;

        Task<byte[]?> Loader(CancellationToken _)
        {
            fetches++;
            return response.Task;
        }

        var first = cache.GetOrLoadAsync("file:a", Loader);
        var second = cache.GetOrLoadAsync("file:a", Loader);
        response.SetResult([4, 5, 6]);

        var results = await Task.WhenAll(first, second);
        Assert.Equal(1, fetches);
        Assert.All(results, bytes => Assert.Equal(new byte[] { 4, 5, 6 }, bytes));
    }

    [Fact]
    public async Task FailedLoad_IsNotCachedAndCanRetry()
    {
        var cache = CreateCache();
        var fetches = 0;

        var failed = await cache.GetOrLoadAsync("file:a", _ =>
        {
            fetches++;
            return Task.FromResult<byte[]?>(null);
        });
        var retried = await cache.GetOrLoadAsync("file:a", _ =>
        {
            fetches++;
            return Task.FromResult<byte[]?>([7]);
        });

        Assert.Null(failed);
        Assert.Equal(new byte[] { 7 }, retried);
        Assert.Equal(2, fetches);
    }

    [Fact]
    public async Task CanceledConsumer_DoesNotCancelSharedLoadOrRemoveCachedResult()
    {
        var cache = CreateCache();
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fetches = 0;
        using var cancellation = new CancellationTokenSource();

        var first = cache.GetOrLoadAsync("file:a", _ =>
        {
            fetches++;
            started.SetResult(true);
            return response.Task;
        }, cancellation.Token);
        await started.Task;
        var second = cache.GetOrLoadAsync("file:a", _ =>
        {
            fetches++;
            return Task.FromResult<byte[]?>([9]);
        });
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        response.SetResult([8]);
        Assert.Equal(new byte[] { 8 }, await second);
        var cached = await cache.GetOrLoadAsync("file:a", _ =>
        {
            fetches++;
            return Task.FromResult<byte[]?>([9]);
        });

        Assert.Equal(new byte[] { 8 }, cached);
        Assert.Equal(1, fetches);
    }

    [Fact]
    public async Task DifferentFileIds_NeverShareBytes()
    {
        var cache = CreateCache();

        var first = await cache.GetOrLoadAsync("file:a", _ => Task.FromResult<byte[]?>([1]));
        var second = await cache.GetOrLoadAsync("file:b", _ => Task.FromResult<byte[]?>([2]));

        Assert.Equal(new byte[] { 1 }, first);
        Assert.Equal(new byte[] { 2 }, second);
        Assert.Equal(2, cache.Count);
    }

    private static BoundedThumbnailMemoryCache CreateCache() =>
        new(maxEntries: 8, maxBytes: 1024, maxConcurrentLoads: 2);
}
