namespace MemoryKeeper.Application.DTOs;

public sealed class NearbyPlaceCandidateDto
{
    public string GooglePlaceId { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Vicinity { get; init; } = string.Empty;

    public string? PlaceType { get; init; }

    public double Latitude { get; init; }

    public double Longitude { get; init; }

    public double DistanceMeters { get; init; }

    public string DistanceText => DistanceMeters < 1000
        ? $"{Math.Round(DistanceMeters)}m"
        : $"{DistanceMeters / 1000.0:0.0}km";

    public string DisplayLabel => string.IsNullOrWhiteSpace(Vicinity)
        ? $"{Name}  ·  {DistanceText}"
        : $"{Name}  ·  {DistanceText}  ·  {Vicinity}";
}
