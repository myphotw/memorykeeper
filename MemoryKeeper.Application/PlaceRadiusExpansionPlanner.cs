namespace MemoryKeeper.Application;

/// <summary>
/// Calculates the smallest supported place radius that contains the selected GPS photos.
/// The 10 m increment matches the existing place-radius editor and the small margin keeps
/// a boundary photo from immediately falling outside because of coordinate rounding.
/// </summary>
public static class PlaceRadiusExpansionPlanner
{
    public const double MaximumRadiusMeters = 2000d;
    public const double RadiusStepMeters = 10d;
    public const double SafetyMarginMeters = 10d;

    public static PlaceRadiusExpansionPlan Create(
        double centerLatitude,
        double centerLongitude,
        double currentRadiusMeters,
        IEnumerable<PlaceRadiusPhotoSelection> selections)
    {
        ArgumentNullException.ThrowIfNull(selections);

        var currentRadius = Math.Max(0d, currentRadiusMeters);
        var points = selections
            .Where(selection => selection.Latitude is double && selection.Longitude is double)
            .Where(selection => PlaceIdentity.HasValidCoordinates(
                selection.Latitude!.Value,
                selection.Longitude!.Value))
            .GroupBy(selection => selection.MediaId)
            .Select(group => group.First())
            .Select(selection =>
            {
                var distance = GeoMath.DistanceMeters(
                    centerLatitude,
                    centerLongitude,
                    selection.Latitude!.Value,
                    selection.Longitude!.Value);
                return new PlaceRadiusPhotoPoint(
                    selection.MediaId,
                    selection.FileName,
                    selection.Latitude.Value,
                    selection.Longitude.Value,
                    distance,
                    distance > currentRadius);
            })
            .OrderByDescending(point => point.DistanceMeters)
            .ToList();

        var requiredRadius = points.Count == 0 ? 0d : points.Max(point => point.DistanceMeters);
        var needsExpansion = requiredRadius > currentRadius;
        var proposedRadius = needsExpansion
            ? Math.Ceiling((requiredRadius + SafetyMarginMeters) / RadiusStepMeters) * RadiusStepMeters
            : currentRadius;

        return new PlaceRadiusExpansionPlan
        {
            CenterLatitude = centerLatitude,
            CenterLongitude = centerLongitude,
            CurrentRadiusMeters = currentRadius,
            RequiredRadiusMeters = requiredRadius,
            ProposedRadiusMeters = proposedRadius,
            HasGpsPhotos = points.Count > 0,
            NeedsExpansion = needsExpansion,
            ExceedsMaximum = proposedRadius > MaximumRadiusMeters,
            PhotoPoints = points,
        };
    }
}

public sealed record PlaceRadiusPhotoSelection(
    Guid MediaId,
    string FileName,
    double? Latitude,
    double? Longitude);

public sealed record PlaceRadiusPhotoPoint(
    Guid MediaId,
    string FileName,
    double Latitude,
    double Longitude,
    double DistanceMeters,
    bool IsOutsideCurrentRadius);

public sealed class PlaceRadiusExpansionPlan
{
    public double CenterLatitude { get; init; }

    public double CenterLongitude { get; init; }

    public double CurrentRadiusMeters { get; init; }

    public double RequiredRadiusMeters { get; init; }

    public double ProposedRadiusMeters { get; init; }

    public bool HasGpsPhotos { get; init; }

    public bool NeedsExpansion { get; init; }

    public bool ExceedsMaximum { get; init; }

    public IReadOnlyList<PlaceRadiusPhotoPoint> PhotoPoints { get; init; } = [];
}
