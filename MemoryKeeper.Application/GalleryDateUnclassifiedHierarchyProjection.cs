using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Application;

public sealed record GalleryDateUnclassifiedHierarchyProjectionResult(int Year, int PhotoCount);

public static class GalleryDateUnclassifiedHierarchyProjection
{
    public static GalleryDateUnclassifiedHierarchyProjectionResult? Build(
        FastGalleryHierarchyNodeDto? yearNode)
    {
        if (yearNode?.Year is not int year || yearNode.DateUnclassifiedCount <= 0)
        {
            return null;
        }

        return new GalleryDateUnclassifiedHierarchyProjectionResult(year, yearNode.DateUnclassifiedCount);
    }
}
