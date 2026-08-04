using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Time;
using MemoryKeeper.Domain.Entities;
using MemoryKeeper.Domain.Enums;
using MemoryKeeper.Infrastructure.Database;

namespace MemoryKeeper.Infrastructure.Repositories;

public sealed class InMemoryDashboardRepository : IDashboardRepository
{
    private readonly List<Media> _media;
    private readonly List<Place> _places;
    private readonly List<Tag> _tags;

    public InMemoryDashboardRepository(
        IEnumerable<Media>? media = null,
        IEnumerable<Place>? places = null,
        IEnumerable<Tag>? tags = null)
    {
        _media = media?.ToList() ?? [];
        _places = places?.ToList() ?? [];
        _tags = tags?.ToList() ?? [];
    }

    public Task<IReadOnlyList<Media>> GetOnThisDayPhotosAsync(
        int month,
        int day,
        int lookbackYears,
        CancellationToken cancellationToken = default)
    {
        var currentYear = DateTime.Now.Year;
        var minYear = currentYear - Math.Max(1, lookbackYears);
        var rangeStart = MediaQueryFilters.GetYearRange(minYear).Start;
        var rangeEnd = MediaQueryFilters.GetYearRange(currentYear).Start;
        IReadOnlyList<Media> result = _media
            .Where(media => media.MediaType == MediaType.Photo)
            .Where(media => media.PlaceId is not null)
            .Where(media => media.CapturedAt is { } captured
                            && captured >= rangeStart
                            && captured < rangeEnd
                            && captured.Month == month
                            && captured.Day == day)
            .OrderByDescending(media => media.IsFavorite)
            .ThenByDescending(media => media.CapturedAt)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<Media>> GetRecentImportsAsync(int take, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Media> result = _media
            .Where(media => media.MediaType == MediaType.Photo)
            .OrderByDescending(media => media.ImportedAt)
            .Take(take)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<Media>> GetFavoritePhotosAsync(int take, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Media> result = _media
            .Where(media => media.MediaType == MediaType.Photo && media.IsFavorite)
            .OrderByDescending(media => media.UpdatedAt)
            .ThenByDescending(media => media.ImportedAt)
            .Take(take)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<DashboardStatisticsRaw> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var photos = _media.Where(media => media.MediaType == MediaType.Photo).ToList();
        var visitRecordCount = photos
            .Where(media => media.PlaceId is not null)
            .GroupBy(media => media.PlaceId!.Value)
            .Sum(group => group.Select(media => (media.CapturedAt ?? media.ImportedAt).Date).Distinct().Count());

        return Task.FromResult(new DashboardStatisticsRaw
        {
            PhotoCount = photos.Count,
            PlaceCount = _places.Count(place => place.IsActive),
            FavoriteCount = photos.Count(media => media.IsFavorite),
            TagCount = _tags.Count(tag => tag.Source == TagSource.User),
            VisitRecordCount = visitRecordCount
        });
    }

    public Task<PendingBreakdownRaw> GetPendingBreakdownAsync(CancellationToken cancellationToken = default)
    {
        var pending = _media
            .Where(media => media.MediaType == MediaType.Photo)
            .Where(media => media.Status == MediaStatus.Pending)
            .ToList();

        var latest = pending
            .OrderByDescending(media => media.ImportedAt)
            .FirstOrDefault();

        return Task.FromResult(new PendingBreakdownRaw
        {
            Total = pending.Count,
            NoGps = pending.Count(media => media.Latitude is null || media.Longitude is null),
            HasGps = pending.Count(media => media.Latitude is not null && media.Longitude is not null),
            UnknownDate = pending.Count(media => media.CapturedAt is null),
            RepresentativeMediaId = latest?.Id,
            LatestImportedAt = DateTimeHelper.ToUtcOffset(latest?.ImportedAt)
        });
    }
}
