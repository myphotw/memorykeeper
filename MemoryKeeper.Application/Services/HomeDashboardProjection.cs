using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;

namespace MemoryKeeper.Application.Services;

/// <summary>Applies a completed authoritative place aggregate to an already visible Home shell.</summary>
public static class HomeDashboardProjection
{
    public static HomeDashboardDto ApplyAuthoritativePlaceAggregates(
        HomeDashboardDto dashboard,
        IReadOnlyList<TravelPlaceAggregateRaw> placeAggregates)
    {
        ArgumentNullException.ThrowIfNull(dashboard);
        ArgumentNullException.ThrowIfNull(placeAggregates);

        var recentVisits = placeAggregates
            .Where(place => !place.IsUnclassified && place.VisitDates.Count > 0)
            .OrderByDescending(place => place.VisitDates.Max())
            .Take(3)
            .Select(place => new RecentVisitDto
            {
                PlaceId = place.PlaceId,
                PlaceName = string.IsNullOrWhiteSpace(place.PlaceName) ? "장소" : place.PlaceName,
                Country = place.Country,
                PhotoCount = place.PhotoCount,
                VisitRecordCount = place.ResolvedVisitCount,
                LastVisitDate = ToLocalDateOffset(place.VisitDates.Max()),
                RepresentativeMediaId = place.RepresentativeMediaId,
                AbsoluteLibraryPath = place.AbsoluteLibraryPath,
                FallbackAbsoluteLibraryPath = place.FallbackAbsoluteLibraryPath,
            }).ToList();
        var heroes = recentVisits.Select(visit => new HeroMemoryDto
        {
            PlaceId = visit.PlaceId,
            PlaceName = visit.PlaceName,
            Year = visit.LastVisitDate?.Year ?? 0,
            PhotoCount = visit.PhotoCount,
            VisitRecordCount = visit.VisitRecordCount,
            RepresentativeMediaId = visit.RepresentativeMediaId,
            AbsoluteLibraryPath = visit.AbsoluteLibraryPath,
            FallbackAbsoluteLibraryPath = visit.FallbackAbsoluteLibraryPath,
            KindLabel = "최근 방문",
            DateText = visit.LastVisitDate?.ToLocalTime().ToString("yyyy.MM.dd") ?? string.Empty,
        }).ToList();

        return new HomeDashboardDto
        {
            HeroMemories = heroes,
            RecentVisits = recentVisits,
            RecentImports = dashboard.RecentImports,
            Favorites = dashboard.Favorites,
            TodayMemories = dashboard.TodayMemories,
            PendingSummary = dashboard.PendingSummary,
            RecentQueries = dashboard.RecentQueries,
            Statistics = new DashboardStatisticsDto
            {
                PhotoCount = dashboard.Statistics.PhotoCount,
                FavoriteCount = dashboard.Statistics.FavoriteCount,
                GpsCount = dashboard.Statistics.GpsCount,
                PlaceCount = placeAggregates.Count(place => !place.IsUnclassified),
                CountryCount = dashboard.Statistics.CountryCount,
                VisitRecordCount = dashboard.Statistics.VisitRecordCount,
                TagCount = dashboard.Statistics.TagCount,
                CountrySummary = dashboard.Statistics.CountrySummary,
                LastUpdatedText = dashboard.Statistics.LastUpdatedText,
                ByYear = dashboard.Statistics.ByYear,
                ByCountry = dashboard.Statistics.ByCountry,
            },
        };
    }

    private static DateTimeOffset ToLocalDateOffset(DateTime value)
    {
        var local = DateTime.SpecifyKind(value, DateTimeKind.Local);
        return new DateTimeOffset(local);
    }
}
