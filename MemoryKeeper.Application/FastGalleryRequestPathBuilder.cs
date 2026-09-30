using System.Globalization;
using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Application;

/// <summary>Builds the shared tc-backend Fast Gallery request paths for every client.</summary>
public static class FastGalleryRequestPathBuilder
{
    public const string Root = "/api/memorykeeper/gallery";

    public static string Photos(FastGalleryPhotoQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.DateUnclassified == true && query.Year is null)
        {
            throw new ArgumentException("date_unclassified requires year.", nameof(query));
        }

        if (query.DateUnclassified == true && query.Unclassified == true)
        {
            throw new ArgumentException("date_unclassified and unclassified are separate filters.", nameof(query));
        }

        var limit = Math.Clamp(query.Limit, 1, 100);
        return BuildPath($"{Root}/photos", new Dictionary<string, string?>
        {
            ["limit"] = limit.ToString(CultureInfo.InvariantCulture),
            ["cursor"] = query.Cursor,
            ["year"] = query.Year?.ToString(CultureInfo.InvariantCulture),
            ["country"] = query.Country,
            ["region"] = query.Region,
            ["location_key"] = query.LocationKey,
            ["place_id"] = string.IsNullOrWhiteSpace(query.LocationKey)
                ? query.PlaceId?.ToString("D")
                : null,
            ["unclassified"] = query.Unclassified == true ? "true" : null,
            ["date_unclassified"] = query.DateUnclassified == true ? "true" : null,
            ["favorite"] = query.Favorite?.ToString().ToLowerInvariant(),
            ["has_gps"] = query.HasGps?.ToString().ToLowerInvariant(),
            ["date_from"] = query.DateFrom?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["date_to"] = query.DateTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["photo_category"] = query.PhotoCategory,
        });
    }

    private static string BuildPath(string root, IReadOnlyDictionary<string, string?> query)
    {
        var parts = query
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}");
        var suffix = string.Join("&", parts);
        return string.IsNullOrEmpty(suffix) ? root : $"{root}?{suffix}";
    }
}
