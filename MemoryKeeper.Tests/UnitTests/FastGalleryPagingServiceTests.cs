using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class FastGalleryPagingServiceTests
{
    [Fact]
    public async Task FirstPage_RequestsFiftyAndStoresContinuation()
    {
        var repository = new StubRepository((query, _) => Task.FromResult(Page(
            Photos(50),
            nextCursor: "next-50",
            hasMore: true)));
        var service = new FastGalleryPagingService(repository);

        var update = await service.LoadFirstPageAsync();

        Assert.Equal(50, repository.Queries.Single().Limit);
        Assert.Null(repository.Queries.Single().Cursor);
        Assert.Equal(50, update.Items.Count);
        Assert.Equal("next-50", update.NextCursor);
        Assert.True(update.HasMore);
    }

    [Fact]
    public async Task NextPage_PassesOpaqueCursorAndAppendsUniqueFiles()
    {
        var responses = new Queue<FastGalleryPhotoPageDto>(
        [
            Page([Photo("a"), Photo("b")], "opaque+/=", true),
            Page([Photo("b"), Photo("c")], null, false),
        ]);
        var repository = new StubRepository((_, _) => Task.FromResult(responses.Dequeue()));
        var service = new FastGalleryPagingService(repository);

        await service.LoadFirstPageAsync();
        var update = await service.LoadNextPageAsync();

        Assert.Equal("opaque+/=", repository.Queries[1].Cursor);
        Assert.Equal(new[] { "a", "b", "c" }, update.Items.Select(item => item.FileId));
        Assert.Equal("c", Assert.Single(update.AddedItems).FileId);
        Assert.False(update.HasMore);
        Assert.Null(update.NextCursor);
    }

    [Fact]
    public async Task LastPage_DoesNotRequestAgain()
    {
        var repository = new StubRepository((_, _) => Task.FromResult(Page([Photo("a")], null, false)));
        var service = new FastGalleryPagingService(repository);

        await service.LoadFirstPageAsync();
        var update = await service.LoadNextPageAsync();

        Assert.True(update.Skipped);
        Assert.Single(repository.Queries);
    }

    [Fact]
    public async Task RepeatedCursor_StopsPaginationLoop()
    {
        var responses = new Queue<FastGalleryPhotoPageDto>(
        [
            Page([Photo("a")], "same", true),
            Page([Photo("b")], "same", true),
        ]);
        var repository = new StubRepository((_, _) => Task.FromResult(responses.Dequeue()));
        var service = new FastGalleryPagingService(repository);

        await service.LoadFirstPageAsync();
        var update = await service.LoadNextPageAsync();

        Assert.False(update.HasMore);
        Assert.Null(update.NextCursor);
    }

    [Fact]
    public async Task ConcurrentLoadMore_SkipsDuplicateRequest()
    {
        var nextPage = new TaskCompletionSource<FastGalleryPhotoPageDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var call = 0;
        var repository = new StubRepository((_, _) => ++call == 1
            ? Task.FromResult(Page([Photo("a")], "next", true))
            : nextPage.Task);
        var service = new FastGalleryPagingService(repository);
        await service.LoadFirstPageAsync();

        var first = service.LoadNextPageAsync();
        var duplicate = await service.LoadNextPageAsync();
        nextPage.SetResult(Page([Photo("b")], null, false));
        await first;

        Assert.True(duplicate.Skipped);
        Assert.Equal(2, repository.Queries.Count);
    }

    [Fact]
    public async Task Refresh_ResetsCursorAndReplacesItems()
    {
        var responses = new Queue<FastGalleryPhotoPageDto>(
        [
            Page([Photo("a")], "next", true),
            Page([Photo("b")], null, false),
            Page([Photo("fresh")], null, false),
        ]);
        var repository = new StubRepository((_, _) => Task.FromResult(responses.Dequeue()));
        var service = new FastGalleryPagingService(repository);

        await service.LoadFirstPageAsync();
        await service.LoadNextPageAsync();
        var refreshed = await service.LoadFirstPageAsync();

        Assert.Equal(new string?[] { null, "next", null }, repository.Queries.Select(query => query.Cursor));
        Assert.Equal("fresh", Assert.Single(refreshed.Items).FileId);
    }

    [Fact]
    public async Task FailedRefresh_PreservesLastSuccessfulSnapshot()
    {
        var call = 0;
        var repository = new StubRepository((_, _) => ++call == 1
            ? Task.FromResult(Page([Photo("kept")], null, false))
            : throw new HttpRequestException("offline"));
        var service = new FastGalleryPagingService(repository);
        await service.LoadFirstPageAsync();

        await Assert.ThrowsAsync<HttpRequestException>(() => service.LoadFirstPageAsync());

        Assert.Equal("kept", Assert.Single(service.Items).FileId);
        Assert.False(service.HasMore);
    }

    [Fact]
    public async Task CanceledNextPage_DoesNotAdvanceInternalCursor()
    {
        var nextPage = new TaskCompletionSource<FastGalleryPhotoPageDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var call = 0;
        var repository = new StubRepository((_, _) => ++call == 1
            ? Task.FromResult(Page([Photo("a")], "next", true))
            : nextPage.Task);
        var service = new FastGalleryPagingService(repository);
        await service.LoadFirstPageAsync();
        using var cancellation = new CancellationTokenSource();

        var loading = service.LoadNextPageAsync(cancellation.Token);
        cancellation.Cancel();
        nextPage.SetResult(Page([Photo("b")], "later", true));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loading);
        Assert.Equal("next", service.NextCursor);
        Assert.Equal("a", Assert.Single(service.Items).FileId);
    }

    private static FastGalleryPhotoDto[] Photos(int count) =>
        Enumerable.Range(0, count).Select(index => Photo($"file-{index}")).ToArray();

    private static FastGalleryPhotoDto Photo(string fileId) => new()
    {
        FileId = fileId,
        Filename = $"{fileId}.jpg",
        ThumbnailUrl = $"/api/common/gallery/{fileId}/thumbnail",
    };

    private static FastGalleryPhotoPageDto Page(
        IReadOnlyList<FastGalleryPhotoDto> items,
        string? nextCursor,
        bool hasMore) => new()
    {
        Items = items,
        NextCursor = nextCursor,
        HasMore = hasMore,
    };

    private sealed class StubRepository : IFastGalleryApiRepository
    {
        private readonly Func<FastGalleryPhotoQuery, CancellationToken, Task<FastGalleryPhotoPageDto>> _handler;

        public StubRepository(
            Func<FastGalleryPhotoQuery, CancellationToken, Task<FastGalleryPhotoPageDto>> handler)
        {
            _handler = handler;
        }

        public List<FastGalleryPhotoQuery> Queries { get; } = [];

        public Task<FastGalleryPhotoPageDto> GetPhotosAsync(
            FastGalleryPhotoQuery query,
            CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            return _handler(query, cancellationToken);
        }

        public Task<FastGallerySummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FastGalleryHierarchyDto> GetHierarchyAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
