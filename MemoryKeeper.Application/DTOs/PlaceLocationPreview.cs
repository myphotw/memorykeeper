namespace MemoryKeeper.Application.DTOs;

public enum PlaceLocationSource
{
    None = 0,
    Original = 1,
    Existing = 2,
    Google = 3,
    Nearby = 4,
    MapPick = 5
}

/// <summary>
/// Snapshot of a place selection for the registration dialog (MK-052).
/// OriginalLocation and SelectedLocation are managed separately until Apply.
/// </summary>
public sealed class PlaceLocationPreview
{
    public static PlaceLocationPreview Empty { get; } = new();

    public Guid? PlaceId { get; init; }

    public string? GooglePlaceId { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public string Country { get; init; } = string.Empty;

    public string Province { get; init; } = string.Empty;

    public string City { get; init; } = string.Empty;

    public string District { get; init; } = string.Empty;

    public string Address { get; init; } = string.Empty;

    public double? Latitude { get; init; }

    public double? Longitude { get; init; }

    public double RadiusMeters { get; init; } = 100;

    public PlaceLocationSource Source { get; init; } = PlaceLocationSource.None;

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(DisplayName)
        && PlaceId is null
        && string.IsNullOrWhiteSpace(GooglePlaceId)
        && Latitude is null
        && Longitude is null;

    public bool HasCoordinates => Latitude is not null && Longitude is not null;

    public string CoordinatesText =>
        HasCoordinates
            ? $"{Latitude:F6}\n{Longitude:F6}"
            : "위치정보 없음";

    public string LatitudeText => Latitude is double lat ? lat.ToString("F6") : "-";

    public string LongitudeText => Longitude is double lng ? lng.ToString("F6") : "-";

    public string RadiusText => $"{RadiusMeters:0}m";

    public static PlaceLocationPreview FromPlaceDto(PlaceDto place, PlaceLocationSource source) =>
        new()
        {
            PlaceId = place.Id,
            GooglePlaceId = place.GooglePlaceId,
            DisplayName = place.DisplayName,
            Country = place.Country,
            Province = place.Province,
            City = place.City,
            District = place.District,
            Address = place.Address,
            Latitude = place.Latitude,
            Longitude = place.Longitude,
            RadiusMeters = place.Radius > 0 ? place.Radius : 100,
            Source = source
        };

    public static PlaceLocationPreview FromLocationResult(
        LocationResult location,
        double? radiusMeters = null,
        PlaceLocationSource source = PlaceLocationSource.Google,
        Guid? placeId = null) =>
        new()
        {
            PlaceId = placeId,
            GooglePlaceId = location.PlaceId,
            DisplayName = string.IsNullOrWhiteSpace(location.DisplayName)
                ? "선택한 장소"
                : location.DisplayName,
            Country = location.Country,
            Province = location.Province,
            City = location.City,
            District = location.District,
            Address = location.Address,
            Latitude = location.Latitude,
            Longitude = location.Longitude,
            RadiusMeters = radiusMeters is > 0 ? radiusMeters.Value : 100,
            Source = source
        };

    public static PlaceLocationPreview FromNearby(
        NearbyPlaceCandidateDto candidate,
        double radiusMeters = 100) =>
        new()
        {
            GooglePlaceId = candidate.GooglePlaceId,
            DisplayName = candidate.Name,
            Country = string.Empty,
            Province = string.Empty,
            City = string.Empty,
            District = string.Empty,
            Address = candidate.Vicinity,
            Latitude = candidate.Latitude,
            Longitude = candidate.Longitude,
            RadiusMeters = radiusMeters,
            Source = PlaceLocationSource.Nearby
        };

    public static PlaceLocationPreview FromMapPick(
        double latitude,
        double longitude,
        double radiusMeters,
        LocationResult? resolved = null) =>
        new()
        {
            GooglePlaceId = resolved?.PlaceId,
            DisplayName = !string.IsNullOrWhiteSpace(resolved?.DisplayName)
                ? resolved!.DisplayName
                : $"지도 선택 {latitude:F4},{longitude:F4}",
            Country = resolved?.Country ?? string.Empty,
            Province = resolved?.Province ?? string.Empty,
            City = resolved?.City ?? string.Empty,
            District = resolved?.District ?? string.Empty,
            Address = resolved?.Address ?? string.Empty,
            Latitude = latitude,
            Longitude = longitude,
            RadiusMeters = radiusMeters,
            Source = PlaceLocationSource.MapPick
        };

    public static bool IsSamePlace(PlaceLocationPreview? left, PlaceLocationPreview? right)
    {
        if (left is null && right is null)
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        if (left.IsEmpty && right.IsEmpty)
        {
            return true;
        }

        if (left.PlaceId is Guid leftId && right.PlaceId is Guid rightId)
        {
            return leftId == rightId;
        }

        if (!string.IsNullOrWhiteSpace(left.GooglePlaceId)
            && !string.IsNullOrWhiteSpace(right.GooglePlaceId)
            && string.Equals(left.GooglePlaceId, right.GooglePlaceId, StringComparison.Ordinal))
        {
            return true;
        }

        if (left.HasCoordinates && right.HasCoordinates
            && Math.Abs(left.Latitude!.Value - right.Latitude!.Value) < 0.00001
            && Math.Abs(left.Longitude!.Value - right.Longitude!.Value) < 0.00001
            && string.Equals(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Apply is enabled only when a non-empty selection differs from the original place.
    /// </summary>
    public static bool CanApply(PlaceLocationPreview? original, PlaceLocationPreview? selected)
    {
        if (selected is null || selected.IsEmpty)
        {
            return false;
        }

        return !IsSamePlace(original ?? Empty, selected);
    }
}
