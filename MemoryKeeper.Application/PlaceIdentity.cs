using System.Security.Cryptography;
using System.Text;

namespace MemoryKeeper.Application;

/// <summary>Stable place identity for Backend-derived travel/visit aggregates.</summary>
public static class PlaceIdentity
{
    public static string Key(string? country, string? city, string? placeName) =>
        $"{Normalize(country)}|{Normalize(city)}|{Normalize(placeName)}";

    /// <summary>
    /// Visit-map / travel-record shared key: place_name only (matches Gallery map grouping).
    /// </summary>
    public static string MapPlaceKey(string? placeName) => Key(null, null, placeName);

    public static Guid StableId(string? country, string? city, string? placeName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Key(country, city, placeName)));
        var bytes = new byte[16];
        Buffer.BlockCopy(hash, 0, bytes, 0, 16);
        return new Guid(bytes);
    }

    /// <summary>Stable id aligned with Visit Record map markers (place_name only).</summary>
    public static Guid MapStableId(string? placeName) => StableId(null, null, placeName);

    public static string DisplayName(string? placeName) =>
        string.IsNullOrWhiteSpace(placeName) ? "장소 미지정" : placeName.Trim();

    /// <summary>Rejects null-island (0,0) and out-of-range values.</summary>
    public static bool HasValidCoordinates(double latitude, double longitude) =>
        (latitude != 0d || longitude != 0d)
        && latitude is >= -90d and <= 90d
        && longitude is >= -180d and <= 180d;

    /// <summary>
    /// Priority: representative valid GPS → first valid in group → average of remaining valid GPS.
    /// </summary>
    public static (double Latitude, double Longitude)? ResolveCoordinates(
        (double Latitude, double Longitude)? representative,
        IEnumerable<(double Latitude, double Longitude)> groupCoordinates)
    {
        if (representative is { } preferred
            && HasValidCoordinates(preferred.Latitude, preferred.Longitude))
        {
            return preferred;
        }

        var valid = groupCoordinates
            .Where(c => HasValidCoordinates(c.Latitude, c.Longitude))
            .ToList();
        if (valid.Count == 0)
        {
            return null;
        }

        // Priority 2: first valid GPS. Priority 3 (average) applies only if first is skipped —
        // with a non-empty valid list the first entry is always usable.
        return valid[0];
    }

    private static string Normalize(string? value) =>
        (value?.Trim() ?? string.Empty).ToLowerInvariant();
}
