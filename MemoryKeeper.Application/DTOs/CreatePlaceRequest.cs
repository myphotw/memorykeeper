namespace MemoryKeeper.Application.DTOs;

public sealed class CreatePlaceRequest
{
    public required string DisplayName { get; init; }

    public string Country { get; init; } = string.Empty;

    public string Province { get; init; } = string.Empty;

    public string City { get; init; } = string.Empty;

    public string Address { get; init; } = string.Empty;

    public string PostalCode { get; init; } = string.Empty;

    public string? GooglePlaceId { get; init; }

    /// <summary>
    /// Immutable Google canonical name. Defaults to DisplayName when GooglePlaceId is set.
    /// </summary>
    public string? CanonicalName { get; init; }

    public string? Category { get; init; }

    public required double Latitude { get; init; }

    public required double Longitude { get; init; }

    public double? Radius { get; init; }

    public bool IsActive { get; init; } = true;

    public bool IsFavorite { get; init; }

    /// <summary>
    /// When true, GPS media inside the new place radius are assigned after create.
    /// </summary>
    public bool ReclassifyMedia { get; init; }

    /// <summary>
    /// When true with ReclassifyMedia, media already linked to other places are moved.
    /// </summary>
    public bool ReassignFromOtherPlaces { get; init; }
}

public sealed class UpdatePlaceRequest
{
    public required Guid Id { get; init; }

    public required string DisplayName { get; init; }

    public string Country { get; init; } = string.Empty;

    public string Province { get; init; } = string.Empty;

    public string City { get; init; } = string.Empty;

    public string Address { get; init; } = string.Empty;

    public string PostalCode { get; init; } = string.Empty;

    /// <summary>
    /// Ignored on update — GooglePlaceId is immutable after create (MK-042O).
    /// </summary>
    public string? GooglePlaceId { get; init; }

    public string? Category { get; init; }

    public required double Latitude { get; init; }

    public required double Longitude { get; init; }

    public required double Radius { get; init; }

    public bool IsActive { get; init; }

    public bool IsFavorite { get; init; }

    /// <summary>
    /// When true, media are reclassified against the updated place radius/coordinates.
    /// </summary>
    public bool ReclassifyMedia { get; init; }

    /// <summary>
    /// When true with ReclassifyMedia, media already linked to other places are moved.
    /// </summary>
    public bool ReassignFromOtherPlaces { get; init; }
}
