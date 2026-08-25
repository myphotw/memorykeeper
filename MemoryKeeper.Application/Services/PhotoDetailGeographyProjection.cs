using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Application.Services;

/// <summary>
/// Resolves Photo Detail display geography without changing raw photo metadata.
/// Photo metadata remains authoritative; the registered Place only fills gaps.
/// </summary>
public static class PhotoDetailGeographyProjection
{
    public static PhotoDetailGeography Resolve(PhotoDetailDto photo, PlaceDto? registeredPlace)
    {
        ArgumentNullException.ThrowIfNull(photo);

        return new PhotoDetailGeography
        {
            Country = FirstNotEmpty(photo.Country, registeredPlace?.Country),
            Province = FirstNotEmpty(photo.Province, registeredPlace?.Province),
            City = FirstNotEmpty(photo.City, registeredPlace?.City),
            District = FirstNotEmpty(photo.District, registeredPlace?.District),
            Address = FirstNotEmpty(photo.Address, registeredPlace?.Address),
        };
    }

    private static string FirstNotEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}

public sealed class PhotoDetailGeography
{
    public string Country { get; init; } = string.Empty;

    public string Province { get; init; } = string.Empty;

    public string City { get; init; } = string.Empty;

    public string District { get; init; } = string.Empty;

    public string Address { get; init; } = string.Empty;
}
