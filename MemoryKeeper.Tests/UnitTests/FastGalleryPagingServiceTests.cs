using MemoryKeeper.Application;
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
    public async Task YearScope_PreservesYearFilterOnLoadMore()
    {
        var responses = new Queue<FastGalleryPhotoPageDto>(
        [
            Page([Photo("year-first")], "year-next", true),
            Page([Photo("year-second")], null, false),
        ]);
        var repository = new StubRepository((_, _) => Task.FromResult(responses.Dequeue()));
        var service = new FastGalleryPagingService(repository);

        await service.LoadFirstPageAsync(new FastGalleryPhotoQuery { Year = 2026 });
        await service.LoadNextPageAsync();

        Assert.Collection(
            repository.Queries,
            query =>
            {
                Assert.Equal(2026, query.Year);
                Assert.Null(query.Cursor);
                Assert.Equal(FastGalleryPagingService.PageSize, query.Limit);
            },
            query =>
            {
                Assert.Equal(2026, query.Year);
                Assert.Equal("year-next", query.Cursor);
                Assert.Equal(FastGalleryPagingService.PageSize, query.Limit);
            });
    }

    [Fact]
    public async Task YearPlaceScope_PreservesEveryFilterOnLoadMore()
    {
        var responses = new Queue<FastGalleryPhotoPageDto>(
        [
            Page([Photo("place-first")], "place-next", true),
            Page([Photo("place-second")], null, false),
        ]);
        var repository = new StubRepository((_, _) => Task.FromResult(responses.Dequeue()));
        var service = new FastGalleryPagingService(repository);
        var query = GalleryBrowseScopeQueryMapper.ToQuery(GalleryBrowseScope.ForHierarchy(
            year: 2025,
            country: "일본",
            region: "교토",
            locationKey: "registered:place"));

        await service.LoadFirstPageAsync(query);
        await service.LoadNextPageAsync();

        Assert.All(repository.Queries, request =>
        {
            Assert.Equal(2025, request.Year);
            Assert.Equal("일본", request.Country);
            Assert.Equal("교토", request.Region);
            Assert.Equal("registered:place", request.LocationKey);
        });
        Assert.Null(repository.Queries[0].Cursor);
        Assert.Equal("place-next", repository.Queries[1].Cursor);
    }

    [Fact]
    public async Task SwitchingScope_ResetsCursorAndDoesNotMixItems()
    {
        var responses = new Queue<FastGalleryPhotoPageDto>(
        [
            Page([Photo("default")], "default-next", true),
            Page([Photo("year")], "year-next", true),
            Page([Photo("year-more")], null, false),
            Page([Photo("default-again")], null, false),
        ]);
        var repository = new StubRepository((_, _) => Task.FromResult(responses.Dequeue()));
        var service = new FastGalleryPagingService(repository);

        await service.LoadFirstPageAsync();
        var year = await service.LoadFirstPageAsync(new FastGalleryPhotoQuery { Year = 2026 });
        var yearWithMore = await service.LoadNextPageAsync();
        var defaultFeed = await service.LoadFirstPageAsync(new FastGalleryPhotoQuery());

        Assert.Equal("year", Assert.Single(year.Items).FileId);
        Assert.Equal(new[] { "year", "year-more" }, yearWithMore.Items.Select(item => item.FileId));
        Assert.Equal("default-again", Assert.Single(defaultFeed.Items).FileId);
        Assert.Null(repository.Queries[1].Cursor);
        Assert.Equal(2026, repository.Queries[1].Year);
        Assert.Equal("year-next", repository.Queries[2].Cursor);
        Assert.Equal(2026, repository.Queries[2].Year);
        Assert.Null(repository.Queries[3].Cursor);
        Assert.Null(repository.Queries[3].Year);
    }

    [Fact]
    public async Task CanonicalRegion_MergesExactAliasQueriesIntoFiftyItemPages()
    {
        var repository = new StubRepository((query, _) => Task.FromResult(query.Region switch
        {
            "Osaka" => Page(PhotosWithPrefix("english", 30), null, false),
            "오사카" => Page(PhotosWithPrefix("korean", 30), null, false),
            _ => throw new InvalidOperationException("Unexpected region query."),
        }));
        var service = new FastGalleryPagingService(repository);
        var scope = GalleryBrowseScope.ForCanonicalRegion(
            2025,
            "Japan",
            "오사카",
            ["Osaka", "오사카"]);

        var first = await service.LoadFirstPageAsync(
            GalleryBrowseScopeQueryMapper.ToQueries(scope));
        var second = await service.LoadNextPageAsync();

        Assert.Equal(50, first.Items.Count);
        Assert.True(first.HasMore);
        Assert.Equal(10, second.AddedItems.Count);
        Assert.Equal(60, second.Items.Count);
        Assert.False(second.HasMore);
        Assert.Equal(2, repository.Queries.Count);
        Assert.Equal(
            ["Osaka", "오사카"],
            repository.Queries.Select(query => query.Region).OrderBy(region => region, StringComparer.Ordinal));
        Assert.All(repository.Queries, query =>
        {
            Assert.Equal(2025, query.Year);
            Assert.Equal("Japan", query.Country);
            Assert.Equal(FastGalleryPagingService.PageSize, query.Limit);
        });
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

    private static FastGalleryPhotoDto[] PhotosWithPrefix(string prefix, int count) =>
        Enumerable.Range(0, count).Select(index => Photo($"{prefix}-{index:D2}")).ToArray();

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
