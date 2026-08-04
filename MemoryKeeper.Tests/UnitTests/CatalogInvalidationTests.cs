using MemoryKeeper.Application.Interfaces;

namespace MemoryKeeper.Tests.UnitTests;

public class CatalogInvalidationTests
{
    [Fact]
    public void Invalidate_ThenConsume_ReturnsTrueOnce()
    {
        var catalog = new CatalogInvalidation();
        catalog.Invalidate(CatalogSurface.Visits | CatalogSurface.Pending);

        Assert.True(catalog.Consume(CatalogSurface.Visits));
        Assert.False(catalog.Consume(CatalogSurface.Visits));
        Assert.True(catalog.Consume(CatalogSurface.Pending));
    }

    [Fact]
    public void Consume_UnaffectedSurface_ReturnsFalse()
    {
        var catalog = new CatalogInvalidation();
        catalog.Invalidate(CatalogSurface.Home);
        Assert.False(catalog.Consume(CatalogSurface.Visits));
        Assert.True(catalog.Consume(CatalogSurface.Home));
    }
}
