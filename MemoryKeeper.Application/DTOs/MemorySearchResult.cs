namespace MemoryKeeper.Application.DTOs;

public sealed class MemorySearchResult
{
    public Guid PlaceId { get; init; }

    public string PlaceName { get; init; } = string.Empty;

    public string Country { get; init; } = string.Empty;

    public string City { get; init; } = string.Empty;

    public int PhotoCount { get; init; }

    public int VisitRecordCount { get; init; }

    public int FavoriteCount { get; init; }

    public bool HasFavorite => FavoriteCount > 0;

    /// <summary>
    /// Representative media for opening Photo Detail (favorite-first).
    /// </summary>
    public Guid? RepresentativeMediaId { get; init; }

    public DateTimeOffset? FirstCapturedDate { get; init; }

    public DateTimeOffset? LastCapturedDate { get; init; }
}
