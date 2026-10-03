using MemoryKeeper.Application.Services;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class BoundedDecodedThumbnailCacheTests
{
    [Fact]
    public void HitMovesEntryToMostRecentAndBudgetEvictsOldest()
    {
        var cache = new BoundedDecodedThumbnailCache<FakeDecodedThumbnail>(
            item => item.AllocationBytes,
            maxBytes: 5);
        var first = new FakeDecodedThumbnail("first", 2);
        var second = new FakeDecodedThumbnail("second", 2);
        var third = new FakeDecodedThumbnail("third", 3);

        Assert.True(cache.AddOrUpdate("file:first", first));
        Assert.True(cache.AddOrUpdate("file:second", second));
        Assert.True(cache.TryGet("file:first", out var hit));
        Assert.Same(first, hit);

        Assert.True(cache.AddOrUpdate("file:third", third));

        Assert.True(cache.TryGet("file:first", out _));
        Assert.False(cache.TryGet("file:second", out _));
        Assert.True(cache.TryGet("file:third", out _));
        Assert.Equal(5, cache.TotalBytes);
    }

    [Fact]
    public void SameIdentityUpdatesWithoutSharingOtherIdentity()
    {
        var cache = new BoundedDecodedThumbnailCache<FakeDecodedThumbnail>(
            item => item.AllocationBytes,
            maxBytes: 10);
        var original = new FakeDecodedThumbnail("original", 4);
        var replacement = new FakeDecodedThumbnail("replacement", 3);
        var other = new FakeDecodedThumbnail("other", 2);

        Assert.True(cache.AddOrUpdate("file:a", original));
        Assert.True(cache.AddOrUpdate("file:a", replacement));
        Assert.True(cache.AddOrUpdate("file:b", other));

        Assert.True(cache.TryGet("file:a", out var firstHit));
        Assert.Same(replacement, firstHit);
        Assert.True(cache.TryGet("file:b", out var secondHit));
        Assert.Same(other, secondHit);
        Assert.Equal(2, cache.Count);
        Assert.Equal(5, cache.TotalBytes);
    }

    [Fact]
    public void OversizedValueIsNotCachedOrDisposed()
    {
        var cache = new BoundedDecodedThumbnailCache<FakeDecodedThumbnail>(
            item => item.AllocationBytes,
            maxBytes: 4);
        var oversized = new FakeDecodedThumbnail("oversized", 5);

        Assert.False(cache.AddOrUpdate("file:large", oversized));

        Assert.False(cache.TryGet("file:large", out _));
        Assert.False(oversized.Disposed);
        Assert.Equal(0, cache.TotalBytes);
    }

    [Fact]
    public void EvictionDropsOnlyCacheReferenceWithoutDisposingDisplayedValue()
    {
        var cache = new BoundedDecodedThumbnailCache<FakeDecodedThumbnail>(
            item => item.AllocationBytes,
            maxBytes: 4);
        var displayed = new FakeDecodedThumbnail("displayed", 4);

        Assert.True(cache.AddOrUpdate("file:displayed", displayed));
        Assert.True(cache.AddOrUpdate("file:new", new FakeDecodedThumbnail("new", 4)));

        Assert.False(cache.TryGet("file:displayed", out _));
        Assert.False(displayed.Disposed);
    }

    private sealed class FakeDecodedThumbnail(string name, long allocationBytes) : IDisposable
    {
        public string Name { get; } = name;

        public long AllocationBytes { get; } = allocationBytes;

        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}
