namespace MemoryKeeper.Application.DTOs;

public sealed class PlaceOverlapItemDto
{
    public Guid PlaceId { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public double Latitude { get; init; }

    public double Longitude { get; init; }

    public double RadiusMeters { get; init; }

    public double DistanceMeters { get; init; }

    public int MediaCount { get; init; }

    public string SummaryText =>
        $"{DisplayName} · 거리 {FormatDistance(DistanceMeters)} · 반경 {RadiusMeters:0}m · 사진 {MediaCount}장";

    private static string FormatDistance(double meters) =>
        meters < 1000
            ? $"{Math.Round(meters)}m"
            : $"{meters / 1000.0:0.0}km";
}

public sealed class PlaceRadiusImpactDto
{
    public int UnassignedCount { get; init; }

    public int FromOtherPlacesCount { get; init; }

    public int TotalInRadius => UnassignedCount + FromOtherPlacesCount;
}
