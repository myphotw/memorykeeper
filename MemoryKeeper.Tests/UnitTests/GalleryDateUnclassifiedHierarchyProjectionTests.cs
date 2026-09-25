using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class GalleryDateUnclassifiedHierarchyProjectionTests
{
    [Fact]
    public void Build_ReturnsYearScopedVirtualNodeDataWhenCountExists()
    {
        var result = GalleryDateUnclassifiedHierarchyProjection.Build(new FastGalleryHierarchyNodeDto
        {
            Year = 2018,
            Count = 500,
            DateUnclassifiedCount = 431,
        });

        Assert.NotNull(result);
        Assert.Equal(2018, result.Year);
        Assert.Equal(431, result.PhotoCount);
    }

    [Fact]
    public void Build_ReturnsNullWhenCountIsZero()
    {
        var result = GalleryDateUnclassifiedHierarchyProjection.Build(new FastGalleryHierarchyNodeDto
        {
            Year = 2018,
            Count = 69,
            DateUnclassifiedCount = 0,
        });

        Assert.Null(result);
    }
}
