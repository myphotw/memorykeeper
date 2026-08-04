namespace MemoryKeeper.Domain.Entities;

/// <summary>
/// User-defined or GPS-derived place used for media classification.
/// </summary>
public class Place : BaseEntity
{
    public string DisplayName { get; set; } = string.Empty;

    public string Country { get; set; } = string.Empty;

    public string Province { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public string PostalCode { get; set; } = string.Empty;

    /// <summary>
    /// Optional Google Places Place ID.
    /// </summary>
    public string? GooglePlaceId { get; set; }

    /// <summary>
    /// Immutable Google canonical place name (MK-042O). DisplayName may be user-edited.
    /// </summary>
    public string? CanonicalName { get; set; }

    /// <summary>
    /// Optional category used for default radius hints (집, 카페, 공원, …).
    /// </summary>
    public string? Category { get; set; }

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    /// <summary>
    /// Matching radius in meters for GPS-based classification.
    /// </summary>
    public double Radius { get; set; }

    public bool IsActive { get; set; }

    /// <summary>
    /// User bookmark for quick access in place management / pending assign.
    /// </summary>
    public bool IsFavorite { get; set; }

    /// <summary>
    /// How many times this place was selected/assigned (UI ranking).
    /// </summary>
    public int UsageCount { get; set; }

    /// <summary>
    /// Last time the place was used or updated for ranking.
    /// </summary>
    public DateTime? LastUsedAt { get; set; }

    public ICollection<Media> MediaItems { get; set; } = new List<Media>();
}
