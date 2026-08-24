using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class BackendChangeMonitorServiceTests
{
    [Fact]
    public async Task FirstCheckPrimesCursor_SecondChangeInvalidatesAllNasSurfaces()
    {
        var feed = new FakeFeed(
            new BackendChangeFeedDto
            {
                Items = [new BackendChangeEventDto { Cursor = 10 }],
                NextCursor = 10,
            },
            new BackendChangeFeedDto
            {
                Items = [new BackendChangeEventDto { Cursor = 11, ResourceType = "MemoryKeeperFile", Tombstone = true }],
                NextCursor = 11,
            });
        var invalidation = new CatalogInvalidation();
        var service = new BackendChangeMonitorService(
            feed,
            invalidation,
            NullLogger<BackendChangeMonitorService>.Instance);

        Assert.False(await service.CheckForChangesAsync());
        Assert.False(invalidation.Consume(CatalogSurface.Gallery));

        Assert.True(await service.CheckForChangesAsync());
        Assert.True(invalidation.Consume(CatalogSurface.Gallery));
        Assert.True(invalidation.Consume(CatalogSurface.Home));
        Assert.True(invalidation.Consume(CatalogSurface.Visits));
        Assert.True(invalidation.Consume(CatalogSurface.Travel));
        Assert.True(invalidation.Consume(CatalogSurface.Pending));
        Assert.True(invalidation.Consume(CatalogSurface.Favorites));
        Assert.True(invalidation.Consume(CatalogSurface.Tags));
        Assert.Equal([0L, 10L], feed.Cursors);
    }

    [Fact]
    public async Task TagResources_DoNotReloadPendingOrVisitHierarchy()
    {
        var feed = new FakeFeed(
            new BackendChangeFeedDto { NextCursor = 20 },
            new BackendChangeFeedDto
            {
                Items =
                [
                    new BackendChangeEventDto
                    {
                        Cursor = 21,
                        ResourceType = "MemoryKeeperFileTag",
                        Tombstone = true,
                    },
                ],
                NextCursor = 21,
            });
        var invalidation = new CatalogInvalidation();
        var service = new BackendChangeMonitorService(
            feed,
            invalidation,
            NullLogger<BackendChangeMonitorService>.Instance);

        Assert.False(await service.CheckForChangesAsync());
        Assert.True(await service.CheckForChangesAsync());

        Assert.True(invalidation.Consume(CatalogSurface.Gallery));
        Assert.True(invalidation.Consume(CatalogSurface.Home));
        Assert.True(invalidation.Consume(CatalogSurface.Travel));
        Assert.True(invalidation.Consume(CatalogSurface.Tags));
        Assert.False(invalidation.Consume(CatalogSurface.Pending));
        Assert.False(invalidation.Consume(CatalogSurface.Visits));
    }

    [Fact]
    public async Task ResetResource_InvalidatesEveryMemoryKeeperSurface()
    {
        var feed = new FakeFeed(
            new BackendChangeFeedDto { NextCursor = 30 },
            new BackendChangeFeedDto
            {
                Items = [new BackendChangeEventDto { Cursor = 31, ResourceType = "MemoryKeeperReset" }],
                NextCursor = 31,
            });
        var invalidation = new CatalogInvalidation();
        var service = new BackendChangeMonitorService(
            feed,
            invalidation,
            NullLogger<BackendChangeMonitorService>.Instance);

        Assert.False(await service.CheckForChangesAsync());
        Assert.True(await service.CheckForChangesAsync());
        Assert.Equal(CatalogSurface.AllMemoryKeeper, service.LastAffectedSurfaces);
        foreach (var surface in new[]
                 {
                     CatalogSurface.Gallery, CatalogSurface.Home, CatalogSurface.Visits,
                     CatalogSurface.Travel, CatalogSurface.Pending, CatalogSurface.Tags,
                     CatalogSurface.Places, CatalogSurface.Favorites,
                 })
        {
            Assert.True(invalidation.Consume(surface));
        }
    }

    private sealed class FakeFeed(params BackendChangeFeedDto[] pages) : IBackendChangeFeed
    {
        private readonly Queue<BackendChangeFeedDto> _pages = new(pages);

        public List<long> Cursors { get; } = [];

        public Task<BackendChangeFeedDto> GetChangesAsync(
            long cursor,
            int limit = 500,
            CancellationToken cancellationToken = default)
        {
            Cursors.Add(cursor);
            return Task.FromResult(_pages.Dequeue());
        }
    }
}
