using MemoryKeeper.App.Services;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class StartupPreloadCacheTests
{
    [Fact]
    public async Task HomeAndGallery_JoinTheSameInFlightFirstPageAndSummary()
    {
        var summaryGate = new TaskCompletionSource<FastGallerySummaryDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inner = new FastGalleryStub
        {
            SummaryFactory = () => summaryGate.Task,
        };
        var invalidation = new CatalogInvalidation();
        using var gallery = new CachedFastGalleryApiRepository(inner, invalidation);
        using var places = new CachedMemoryKeeperPlaceApiRepository(new PlaceRepositoryStub(), invalidation);
        var preload = new StartupPreloadService(gallery, places);

        var startup = preload.EnsureRequiredAsync();
        var menu = gallery.GetSummaryAsync();

        await WaitUntilAsync(() => inner.SummaryCalls == 1);
        Assert.Equal(1, inner.SummaryCalls);
        summaryGate.SetResult(new FastGallerySummaryDto { TotalPhotos = 7 });

        Assert.Equal(7, (await menu).TotalPhotos);
        Assert.True((await startup).Succeeded);
        Assert.Equal(1, inner.PhotoCalls);
        Assert.Equal(1, inner.SummaryCalls);
        Assert.Equal(1, inner.HierarchyCalls);
    }

    [Fact]
    public async Task GalleryInvalidation_DiscardsAStaleInFlightResult()
    {
        var first = new TaskCompletionSource<FastGallerySummaryDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<FastGallerySummaryDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inner = new FastGalleryStub
        {
            SummaryFactory = () => first.Task,
        };
        var invalidation = new CatalogInvalidation();
        using var gallery = new CachedFastGalleryApiRepository(inner, invalidation);

        var staleWaiter = gallery.GetSummaryAsync();
        await WaitUntilAsync(() => inner.SummaryCalls == 1);
        invalidation.Invalidate(CatalogSurface.Gallery);
        inner.SummaryFactory = () => second.Task;
        var currentWaiter = gallery.GetSummaryAsync();
        await WaitUntilAsync(() => inner.SummaryCalls == 2);

        first.SetResult(new FastGallerySummaryDto { TotalPhotos = 1 });
        second.SetResult(new FastGallerySummaryDto { TotalPhotos = 2 });

        Assert.Equal(2, (await staleWaiter).TotalPhotos);
        Assert.Equal(2, (await currentWaiter).TotalPhotos);
        Assert.Equal(2, inner.SummaryCalls);
    }

    [Fact]
    public async Task PlaceCatalog_ReusesSnapshotUntilAPlaceOrGalleryInvalidation()
    {
        var inner = new PlaceRepositoryStub();
        var invalidation = new CatalogInvalidation();
        using var places = new CachedMemoryKeeperPlaceApiRepository(inner, invalidation);

        await places.GetPlacesAsync();
        await places.GetPlacesAsync();
        await places.GetPlaceHierarchyAsync();
        await places.GetPlaceHierarchyAsync();

        Assert.Equal(1, inner.PlaceCalls);
        Assert.Equal(1, inner.HierarchyCalls);

        invalidation.Invalidate(CatalogSurface.Places);
        await places.GetPlacesAsync();
        await places.GetPlaceHierarchyAsync();

        Assert.Equal(2, inner.PlaceCalls);
        Assert.Equal(2, inner.HierarchyCalls);
    }

    [Fact]
    public async Task FilteredGalleryQueries_AreNeverReplacedByTheSharedDefaultSnapshot()
    {
        var inner = new FastGalleryStub();
        using var gallery = new CachedFastGalleryApiRepository(inner, new CatalogInvalidation());

        await gallery.GetPhotosAsync(new FastGalleryPhotoQuery { Limit = 50, Country = "대한민국" });
        await gallery.GetPhotosAsync(new FastGalleryPhotoQuery { Limit = 50, Country = "대한민국" });

        Assert.Equal(2, inner.PhotoCalls);
    }

    [Fact]
    public async Task PreloadFailure_IsFailOpenSoMenusCanRetry()
    {
        var inner = new FastGalleryStub
        {
            FailAll = true,
        };
        var invalidation = new CatalogInvalidation();
        using var gallery = new CachedFastGalleryApiRepository(inner, invalidation);
        using var places = new CachedMemoryKeeperPlaceApiRepository(new PlaceRepositoryStub { FailAll = true }, invalidation);
        var preload = new StartupPreloadService(gallery, places);

        var result = await preload.EnsureRequiredAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(5, result.FailedCount);
        await Assert.ThrowsAsync<InvalidOperationException>(() => gallery.GetSummaryAsync());
        Assert.Equal(2, inner.SummaryCalls);
    }

    [Fact]
    public void SettingsPlaces_StartsMapAndCatalogLoadingTogether()
    {
        var source = ReadSource("MemoryKeeper.App", "Views", "SettingsPage.xaml.cs");
        var placesCase = Slice(source, "case SettingsSection.Places:", "case SettingsSection.Tags:");

        Assert.Contains("Task.WhenAll(", placesCase, StringComparison.Ordinal);
        Assert.Contains("_placeView.ActivateAsync()", placesCase, StringComparison.Ordinal);
        Assert.Contains("_placeView.ViewModel.LoadCommand.ExecuteAsync(null)", placesCase, StringComparison.Ordinal);
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static string ReadSource(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MemoryKeeper.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine([directory!.FullName, .. segments]));
    }

    private sealed class FastGalleryStub : IFastGalleryApiRepository
    {
        public Func<Task<FastGallerySummaryDto>> SummaryFactory { get; set; } =
            () => Task.FromResult(new FastGallerySummaryDto());
        public bool FailAll { get; init; }
        public int PhotoCalls { get; private set; }
        public int SummaryCalls { get; private set; }
        public int HierarchyCalls { get; private set; }

        public Task<FastGalleryPhotoPageDto> GetPhotosAsync(FastGalleryPhotoQuery query, CancellationToken cancellationToken = default)
        {
            PhotoCalls++;
            return FailAll
                ? Task.FromException<FastGalleryPhotoPageDto>(new InvalidOperationException("offline"))
                : Task.FromResult(new FastGalleryPhotoPageDto());
        }

        public Task<FastGallerySummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default)
        {
            SummaryCalls++;
            return FailAll
                ? Task.FromException<FastGallerySummaryDto>(new InvalidOperationException("offline"))
                : SummaryFactory();
        }

        public Task<FastGalleryHierarchyDto> GetHierarchyAsync(CancellationToken cancellationToken = default)
        {
            HierarchyCalls++;
            return FailAll
                ? Task.FromException<FastGalleryHierarchyDto>(new InvalidOperationException("offline"))
                : Task.FromResult(new FastGalleryHierarchyDto());
        }
    }

    private sealed class PlaceRepositoryStub : IMemoryKeeperPlaceApiRepository
    {
        public bool FailAll { get; init; }
        public int PlaceCalls { get; private set; }
        public int HierarchyCalls { get; private set; }

        public Task<MemoryKeeperPlaceListApiDto> GetPlacesAsync(CancellationToken cancellationToken = default)
        {
            PlaceCalls++;
            return Result(new MemoryKeeperPlaceListApiDto());
        }

        public Task<MemoryKeeperPlaceHierarchyListApiDto> GetPlaceHierarchyAsync(CancellationToken cancellationToken = default)
        {
            HierarchyCalls++;
            return Result(new MemoryKeeperPlaceHierarchyListApiDto());
        }

        public Task<MemoryKeeperPlaceDuplicateCandidateListApiDto> GetDuplicateCandidatesAsync(CancellationToken cancellationToken = default) =>
            Result(new MemoryKeeperPlaceDuplicateCandidateListApiDto());

        private Task<T> Result<T>(T value) => FailAll
            ? Task.FromException<T>(new InvalidOperationException("offline"))
            : Task.FromResult(value);

        public Task<MemoryKeeperPlaceApiDto> GetPlaceAsync(Guid placeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MemoryKeeperPlaceApiDto> CreatePlaceAsync(MemoryKeeperPlaceCreateApiRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MemoryKeeperPlaceApiDto> UpdatePlaceAsync(Guid placeId, MemoryKeeperPlaceUpdateApiRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeletePlaceAsync(Guid placeId, int expectedRevision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MemoryKeeperPlacePhotoPageApiDto> GetPlacePhotosAsync(Guid placeId, int limit, int offset, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MemoryKeeperPlaceMergePreviewApiDto> GetMergePreviewAsync(Guid sourcePlaceId, Guid targetPlaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MemoryKeeperPlaceMergeApiResult> MergePlacesAsync(MemoryKeeperPlaceMergeApiRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MemoryKeeperPlaceMatchApiResult> MatchAsync(MemoryKeeperPlaceMatchApiRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MemoryKeeperPlaceReclassifyApiResult> ReclassifyAsync(Guid placeId, bool reassignFromOtherPlaces, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MemoryKeeperRadiusImpactApiResult> GetRadiusImpactAsync(MemoryKeeperRadiusImpactApiRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MemoryKeeperFilePlaceUpdateApiResult> AssignFilePlaceAsync(string fileId, Guid? placeId, int expectedRevision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
