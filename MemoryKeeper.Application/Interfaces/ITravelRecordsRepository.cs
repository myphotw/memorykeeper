namespace MemoryKeeper.Application.Interfaces;

/// <summary>
/// Read models for "나의 여행기록" summaries. Does not alter existing repository contracts.
/// </summary>
public interface ITravelRecordsRepository
{
    Task<IReadOnlyList<TravelPlaceAggregateRaw>> GetPlaceAggregatesAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TravelCountryAggregateRaw>> GetCountryAggregatesAsync(
        CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TravelCountryAggregateRaw>>([]);

    Task<IReadOnlyList<TravelMemoryCandidateRaw>> GetMemoryCandidatesAsync(
        DateOnly referenceDate,
        int limit,
        CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TravelMemoryCandidateRaw>>([]);
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

    /// <summary>Authoritative server-side consecutive effective-date visit count.</summary>
    public int VisitCount { get; init; }

    /// <summary>Server visit_count when supplied; legacy test/read models retain their date-derived meaning.</summary>
    public int ResolvedVisitCount => VisitCount > 0 ? VisitCount : CountDateRanges(VisitDates);

    /// <summary>True when this is the synthetic missing-place aggregate.</summary>
    public bool IsUnclassified { get; init; }

    public Guid? RepresentativeMediaId { get; init; }

    public string? AbsoluteLibraryPath { get; init; }

    public DateOnly? RepresentativeCaptureDate { get; init; }

    /// <summary>
    /// Distinct visit dates (CapturedAt ?? ImportedAt).date.
    /// </summary>
    public IReadOnlyList<DateTime> VisitDates { get; init; } = [];

    /// <summary>
    /// Photo-level candidates already present in the Gallery snapshot. Only the real
    /// capture timestamp is populated so anniversary memories never use import time.
    /// </summary>
    public IReadOnlyList<TravelPhotoCandidateRaw> Photos { get; init; } = [];

    private static int CountDateRanges(IReadOnlyList<DateTime> dates)
    {
        if (dates.Count == 0) return 0;
        var ordered = dates.Distinct().OrderBy(date => date).ToList();
        var visits = 1;
        for (var index = 1; index < ordered.Count; index++)
        {
            if ((ordered[index].Date - ordered[index - 1].Date).Days > 1) visits++;
        }
        return visits;
    }
}

public sealed class TravelCountryAggregateRaw
{
    public string Country { get; init; } = string.Empty;
    public int PhotoCount { get; init; }
    public int VisitCount { get; init; }
    public IReadOnlyList<DateOnly> CaptureDates { get; init; } = [];
    public Guid? RepresentativeMediaId { get; init; }
    public string? RepresentativeThumbnailPath { get; init; }
    public DateOnly? RepresentativeCaptureDate { get; init; }
}

public sealed class TravelMemoryCandidateRaw
{
    public Guid? MediaId { get; init; }
    public Guid? PlaceId { get; init; }
    public string PlaceName { get; init; } = string.Empty;
    public string Country { get; init; } = string.Empty;
    public DateOnly CaptureDate { get; init; }
    public string ThumbnailPath { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
}

public sealed class TravelPhotoCandidateRaw
{
    public Guid? MediaId { get; init; }

    public string BackendFileId { get; init; } = string.Empty;

    public string ThumbnailPath { get; init; } = string.Empty;

    /// <summary>Country resolved for this photo, not inherited from its aggregate.</summary>
    public string Country { get; init; } = string.Empty;

    public DateTimeOffset? CapturedAt { get; init; }

    public bool IsFavorite { get; init; }
}
