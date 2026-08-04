namespace MemoryKeeper.Application.DTOs;

public sealed class PlaceDto
{
    public Guid Id { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public string Country { get; init; } = string.Empty;

    public string Province { get; init; } = string.Empty;

    public string City { get; init; } = string.Empty;

    public string Address { get; init; } = string.Empty;

    public string PostalCode { get; init; } = string.Empty;

    public string? GooglePlaceId { get; init; }

    public string? CanonicalName { get; init; }

    public string? Category { get; init; }

    public double Latitude { get; init; }

    public double Longitude { get; init; }

    public double Radius { get; init; }

    public bool IsActive { get; init; }

    /// <summary>Place bookmark (즐겨찾는 장소).</summary>
    public bool IsFavorite { get; init; }

    public int UsageCount { get; init; }

    public DateTime? LastUsedAt { get; init; }

    public int MediaCount { get; init; }

    public int VisitRecordCount { get; init; }

    public int FavoriteCount { get; init; }

    /// <summary>True when any assigned media is favorited.</summary>
    public bool HasFavorite => FavoriteCount > 0;

    public Guid? RepresentativeMediaId { get; init; }

    public string RegionSummary =>
        string.Join(" / ", new[] { Country, City }.Where(value => !string.IsNullOrWhiteSpace(value)));

    public string LocationSummary => $"{Latitude:F5}, {Longitude:F5}";

    public string LastUsedText => LastUsedAt?.ToLocalTime().ToString("yyyy-MM-dd") ?? "-";
}
