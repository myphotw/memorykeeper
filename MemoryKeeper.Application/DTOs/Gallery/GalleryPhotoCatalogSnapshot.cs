namespace MemoryKeeper.Application.DTOs.Gallery;

/// <summary>
/// Canonical read-only Gallery snapshot used by NAS-backed feature screens.
/// </summary>
public sealed class GalleryPhotoCatalogSnapshot
{
    public IReadOnlyList<PhotoDto> Photos { get; init; } = [];

    public IReadOnlyList<MapMarkerDto> MapMarkers { get; init; } = [];

    /// <summary>
    /// Authoritative detail metadata for GPS photos that could not be joined to /map.
    /// Keys are the original Backend file_id values.
    /// </summary>
    public IReadOnlyDictionary<string, GalleryPhotoLocationMetadataDto> LocationMetadataByFileId { get; init; }
        = new Dictionary<string, GalleryPhotoLocationMetadataDto>(StringComparer.OrdinalIgnoreCase);

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
}
