namespace MemoryKeeper.Application;

/// <summary>
/// Recommended default radii (meters) by place category. User can override.
/// </summary>
public static class PlaceCategoryDefaults
{
    public static readonly IReadOnlyList<(string Category, double RadiusMeters)> Items =
    [
        ("집", 100),
        ("회사", 100),
        ("카페", 50),
        ("공원", 300),
        ("놀이공원", 500),
        ("산", 1000),
        ("기타", 100)
    ];

    public static double GetRecommendedRadius(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return 100;
        }

        var match = Items.FirstOrDefault(item =>
            string.Equals(item.Category, category.Trim(), StringComparison.OrdinalIgnoreCase));
        return match.Category is null ? 100 : match.RadiusMeters;
    }
}
