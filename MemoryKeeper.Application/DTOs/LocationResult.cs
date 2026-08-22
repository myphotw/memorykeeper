namespace MemoryKeeper.Application.DTOs;

public sealed record LocationResult
{
    public string DisplayName { get; init; } = string.Empty;

    public string Country { get; init; } = string.Empty;

    public string Province { get; init; } = string.Empty;

    public string City { get; init; } = string.Empty;

    public string District { get; init; } = string.Empty;

    public string Address { get; init; } = string.Empty;

    public string PostalCode { get; init; } = string.Empty;

    public double Latitude { get; init; }

    public double Longitude { get; init; }

    public string? PlaceId { get; init; }

    /// <summary>
    /// Primary Google Places type (e.g. tourist_attraction). Stored as Place.Category.
    /// </summary>
    public string? PlaceType { get; init; }
}
