namespace MemoryKeeper.Application.Interfaces;

/// <summary>
/// Read models for "나의 여행기록" summaries. Does not alter existing repository contracts.
/// </summary>
public interface ITravelRecordsRepository
{
    Task<IReadOnlyList<TravelPlaceAggregateRaw>> GetPlaceAggregatesAsync(
        CancellationToken cancellationToken = default);
}

public sealed class TravelPlaceAggregateRaw
{
    public Guid PlaceId { get; init; }

    public string PlaceName { get; init; } = string.Empty;

    public string Country { get; init; } = string.Empty;

    public double Latitude { get; init; }

    public double Longitude { get; init; }

    public int PhotoCount { get; init; }

    public int FavoriteCount { get; init; }

    public Guid? RepresentativeMediaId { get; init; }

    public string? AbsoluteLibraryPath { get; init; }

    /// <summary>
    /// Distinct visit dates (CapturedAt ?? ImportedAt).date.
    /// </summary>
    public IReadOnlyList<DateTime> VisitDates { get; init; } = [];
}
