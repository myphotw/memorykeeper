using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Application;

public sealed record GalleryDailyHierarchyProjectionResult(
    int Year,
    int PhotoCount,
    FastGalleryHierarchyNodeDto? DomesticCountryNode)
{
    public bool RequiresSyntheticDomesticCountry => DomesticCountryNode is null;
}

/// <summary>
/// Projects the year-level DAILY count into the virtual 대한민국 &gt; 일상 branch.
/// It never creates or mutates a Backend Place.
/// </summary>
public static class GalleryDailyHierarchyProjection
{
    public const string DomesticCountryName = "대한민국";

    public static GalleryDailyHierarchyProjectionResult? Build(FastGalleryHierarchyNodeDto? yearNode)
    {
        if (yearNode?.Year is not int year || yearNode.DailyCount <= 0)
        {
            return null;
        }

        var domestic = yearNode.ChildNodes.FirstOrDefault(node =>
            string.Equals(
                PlaceNormalizer.NormalizeCountry(node.Country),
                DomesticCountryName,
                StringComparison.OrdinalIgnoreCase));
        return new GalleryDailyHierarchyProjectionResult(year, yearNode.DailyCount, domestic);
    }
}
