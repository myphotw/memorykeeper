namespace MemoryKeeper.Application.DTOs.Gallery;

/// <summary>
/// Canonical read-only Gallery snapshot used by NAS-backed feature screens.
/// </summary>
public sealed class GalleryPhotoCatalogSnapshot
{
    public IReadOnlyList<PhotoDto> Photos { get; init; } = [];

    public IReadOnlyList<MapMarkerDto> MapMarkers { get; init; } = [];

    /// <summary>
    /// Backend file identities ordered by library registration time (newest first).
    /// The Gallery list contract does not expose created_at yet, but it does support
    /// created_at_desc sorting, which is sufficient to preserve the original top-48 UX.
    /// </summary>
    public IReadOnlyList<string> RecentPhotoFileIds { get; init; } = [];

    /// <summary>
    /// Authoritative detail metadata for GPS photos that could not be joined to /map.
    /// Keys are the original Backend file_id values.
    /// </summary>
    public IReadOnlyDictionary<string, GalleryPhotoLocationMetadataDto> LocationMetadataByFileId { get; init; }
        = new Dictionary<string, GalleryPhotoLocationMetadataDto>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// NAS registered-place geography keyed by the authoritative MemoryKeeper Place UUID.
    /// Raw photo geography still has priority in the shared hierarchy.
    /// </summary>
    public IReadOnlyDictionary<Guid, GalleryRegisteredPlaceGeographyDto> RegisteredPlacesById { get; init; }
        = new Dictionary<Guid, GalleryRegisteredPlaceGeographyDto>();

    public string ApiBaseUrl { get; init; } = string.Empty;
}

public sealed class GalleryPhotoLocationMetadataDto
{
    public double? Latitude { get; init; }

    public double? Longitude { get; init; }

    public string? Country { get; init; }

    public string? Province { get; init; }

    public string? City { get; init; }

    public string? District { get; init; }

    public string? PlaceName { get; init; }

    public Guid? MemorykeeperPlaceId { get; init; }

    public string? PlaceDisplayName { get; init; }

    public string? PlaceCanonicalName { get; init; }

    public string? GeocodedPlaceName { get; init; }

    public string? PlaceMatchSource { get; init; }

    public double? PlaceMatchDistanceM { get; init; }

    public int PlaceRevision { get; init; }
}

public sealed class GalleryRegisteredPlaceGeographyDto
{
    public string? Country { get; init; }

    public string? Province { get; init; }

    public string? City { get; init; }

    public string? District { get; init; }
}
