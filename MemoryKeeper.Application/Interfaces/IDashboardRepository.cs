using MemoryKeeper.Domain.Entities;

namespace MemoryKeeper.Application.Interfaces;

/// <summary>
/// Dashboard-oriented read APIs. Does not alter existing repository contracts.
/// </summary>
public interface IDashboardRepository
{
    Task<IReadOnlyList<Media>> GetOnThisDayPhotosAsync(
        int month,
        int day,
        int lookbackYears,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Media>> GetRecentImportsAsync(
        int take,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Media>> GetFavoritePhotosAsync(
        int take,
        CancellationToken cancellationToken = default);

    Task<DashboardStatisticsRaw> GetStatisticsAsync(CancellationToken cancellationToken = default);

    Task<PendingBreakdownRaw> GetPendingBreakdownAsync(CancellationToken cancellationToken = default);
}

public sealed class DashboardStatisticsRaw
{
    public int PhotoCount { get; init; }

    public int PlaceCount { get; init; }

    public int FavoriteCount { get; init; }

    public int TagCount { get; init; }

    public int VisitRecordCount { get; init; }
}

public sealed class PendingBreakdownRaw
{
    public int Total { get; init; }

    public int NoGps { get; init; }

    public int HasGps { get; init; }

    public int UnknownDate { get; init; }

    public Guid? RepresentativeMediaId { get; init; }

    public DateTimeOffset? LatestImportedAt { get; init; }
}
