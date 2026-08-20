using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Application.Services;

public sealed class TravelRecordsService
{
    private const int DetailTake = 20;
    private const int RecentCardTake = 5;
    private const int TopTagTake = 2;
    private const int LongUnvisitedMinVisits = 2;
    private const int LongUnvisitedMinPhotos = 20;

    private readonly ITravelRecordsRepository _travelRecordsRepository;
    private readonly IMediaTagRepository _mediaTagRepository;
    private readonly ITagRepository _tagRepository;
    private readonly HomeLocationService _homeLocationService;
    private readonly ILogger<TravelRecordsService> _logger;

    public TravelRecordsService(
        ITravelRecordsRepository travelRecordsRepository,
        IMediaTagRepository mediaTagRepository,
        ITagRepository tagRepository,
        HomeLocationService homeLocationService,
        ILogger<TravelRecordsService> logger)
    {
        _travelRecordsRepository = travelRecordsRepository;
        _mediaTagRepository = mediaTagRepository;
        _tagRepository = tagRepository;
        _homeLocationService = homeLocationService;
        _logger = logger;
    }

    public async Task<TravelRecordsDashboardDto> GetDashboardAsync(
        CancellationToken cancellationToken = default)
    {
        var aggregates = await _travelRecordsRepository.GetPlaceAggregatesAsync(cancellationToken);
        _logger.LogInformation(
            "TravelRecords dashboard aggregates. Places={Places}, WithVisits={WithVisits}, UndatedOnly={Undated}",
            aggregates.Count,
            aggregates.Count(a => a.VisitDates.Count > 0),
            aggregates.Count(a => a.VisitDates.Count == 0 && a.PhotoCount > 0));

        if (aggregates.Count == 0)
        {
            return new TravelRecordsDashboardDto();
        }

        var home = await _homeLocationService.GetAsync(cancellationToken);
        var mostVisited = OrderMostVisited(aggregates).FirstOrDefault();
        var longUnvisited = OrderLongUnvisited(aggregates).FirstOrDefault();
        var recent = OrderRecent(aggregates).Take(RecentCardTake).ToList();

        return new TravelRecordsDashboardDto
        {
            MostVisitedPlace = mostVisited is null
                ? null
                : await ToPlaceSummaryAsync(mostVisited, 1, cancellationToken),
            LongUnvisitedPlace = longUnvisited is null
                ? null
                : await ToPlaceSummaryAsync(longUnvisited, 1, cancellationToken),
            SeasonHighlights = BuildSeasonHighlights(aggregates),
            RecentPlaces = await MapPlacesAsync(recent, cancellationToken),
            TopCountry = OrderCountries(aggregates).FirstOrDefault(),
            FarthestPlace = OrderFarthest(aggregates, home).FirstOrDefault(),
            YearChapters = BuildYearChapters(aggregates)
        };
    }

    private static IReadOnlyList<TravelYearChapterDto> BuildYearChapters(
        IReadOnlyList<TravelPlaceAggregateRaw> aggregates)
    {
        var placeYears = aggregates
            .SelectMany(place =>
            {
                var years = place.VisitDates
                    .Select(date => date.Year)
                    .Where(year => year > 0)
                    .Distinct();
                return years.Select(year =>
                {
                    var datesInYear = place.VisitDates
                        .Where(date => date.Year == year)
                        .Select(date => date.Date)
                        .Distinct()
                        .OrderBy(date => date)
                        .ToList();
                    return (Place: place, Year: year, Dates: datesInYear);
                });
            })
            .Where(item => item.Dates.Count > 0)
            .ToList();

        var chapters = placeYears
            .GroupBy(item => item.Year)
            .OrderByDescending(group => group.Key)
            .Select(yearGroup =>
            {
                var trips = new List<TravelTripCardDto>();

                var withForeignCountry = yearGroup
                    .Where(item =>
                        !string.IsNullOrWhiteSpace(item.Place.Country)
                        && !IsDomesticCountry(item.Place.Country))
                    .GroupBy(item => item.Place.Country.Trim(), StringComparer.OrdinalIgnoreCase);

                foreach (var countryGroup in withForeignCountry)
                {
                    trips.Add(BuildTripCard(
                        year: yearGroup.Key,
                        tripName: countryGroup.Key,
                        country: countryGroup.Key,
                        members: countryGroup.ToList()));
                }

                foreach (var item in yearGroup.Where(entry =>
                             string.IsNullOrWhiteSpace(entry.Place.Country)
                             || IsDomesticCountry(entry.Place.Country)))
                {
                    trips.Add(BuildTripCard(
                        year: yearGroup.Key,
                        tripName: item.Place.PlaceName,
                        country: item.Place.Country?.Trim() ?? string.Empty,
                        members: [item]));
                }

                return new TravelYearChapterDto
                {
                    Year = yearGroup.Key,
                    Trips = trips
                        .OrderByDescending(trip => trip.EndDate)
                        .ThenBy(trip => trip.TripName, StringComparer.OrdinalIgnoreCase)
                        .ToList()
                };
            })
            .Where(chapter => chapter.Trips.Count > 0)
            .ToList();

        var undatedOnly = aggregates
            .Where(place => place.VisitDates.Count == 0 && place.PhotoCount > 0)
            .ToList();
        if (undatedOnly.Count > 0)
        {
            var undatedTrips = undatedOnly
                .Select(place => new TravelTripCardDto
                {
                    FocusPlaceId = place.PlaceId,
                    TripName = place.PlaceName,
                    LocationText = string.Empty,
                    Country = place.Country?.Trim() ?? string.Empty,
                    Year = 0,
                    StartDate = null,
                    EndDate = null,
                    PeriodText = "날짜 미상",
                    PlaceCount = 1,
                    PhotoCount = place.PhotoCount,
                    VisitDayCount = 0,
                    RepresentativeMediaId = place.RepresentativeMediaId,
                    AbsoluteLibraryPath = place.AbsoluteLibraryPath,
                    PlaceNames = [place.PlaceName],
                })
                .OrderBy(trip => trip.TripName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            chapters.Add(new TravelYearChapterDto
            {
                Year = 0,
                Trips = undatedTrips,
            });
        }

        return chapters;
    }

    private static bool IsDomesticCountry(string? country)
    {
        if (string.IsNullOrWhiteSpace(country))
        {
            return false;
        }

        var value = country.Trim();
        return value.Equals("대한민국", StringComparison.OrdinalIgnoreCase)
            || value.Equals("한국", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Korea", StringComparison.OrdinalIgnoreCase)
            || value.Equals("South Korea", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Republic of Korea", StringComparison.OrdinalIgnoreCase);
    }

    private static TravelTripCardDto BuildTripCard(
        int year,
        string tripName,
        string country,
        IReadOnlyList<(TravelPlaceAggregateRaw Place, int Year, List<DateTime> Dates)> members)
    {
        var allDates = members
            .SelectMany(member => member.Dates)
            .Distinct()
            .OrderBy(date => date)
            .ToList();
        var start = allDates[0];
        var end = allDates[^1];
        var placeNames = members
            .Select(member => member.Place.PlaceName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var focus = members
            .OrderByDescending(member => member.Place.PhotoCount)
            .ThenByDescending(member => member.Dates.Count)
            .ThenBy(member => member.Place.PlaceName, StringComparer.OrdinalIgnoreCase)
            .First()
            .Place;

        var locationParts = placeNames
            .Where(name => !string.Equals(name, tripName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return new TravelTripCardDto
        {
            FocusPlaceId = focus.PlaceId,
            TripName = tripName,
            LocationText = string.Join(" · ", locationParts),
            Country = country,
            Year = year,
            StartDate = new DateTimeOffset(start),
            EndDate = new DateTimeOffset(end),
            PeriodText = FormatPeriod(start, end),
            PlaceCount = members.Count,
            PhotoCount = members.Sum(member => member.Place.PhotoCount),
            VisitDayCount = allDates.Count,
            RepresentativeMediaId = focus.RepresentativeMediaId,
            AbsoluteLibraryPath = focus.AbsoluteLibraryPath,
            PlaceNames = placeNames
        };
    }

    private static string FormatPeriod(DateTime start, DateTime end)
    {
        if (start.Date == end.Date)
        {
            return start.ToString("yyyy.MM.dd");
        }

        if (start.Year == end.Year && start.Month == end.Month)
        {
            return $"{start:yyyy.MM.dd} – {end:dd}";
        }

        return $"{start:yyyy.MM.dd} – {end:yyyy.MM.dd}";
    }

    public async Task<TravelRecordsDetailDto> GetDetailAsync(
        TravelRecordsDetailKind kind,
        TravelSeason? season = null,
        CancellationToken cancellationToken = default)
    {
        var aggregates = await _travelRecordsRepository.GetPlaceAggregatesAsync(cancellationToken);
        var home = await _homeLocationService.GetAsync(cancellationToken);

        if (kind == TravelRecordsDetailKind.Season)
        {
            return await BuildSeasonDetailAsync(aggregates, season ?? TravelSeason.Spring, cancellationToken);
        }

        return kind switch
        {
            TravelRecordsDetailKind.MostVisited => new TravelRecordsDetailDto
            {
                Kind = kind,
                Title = "가장 많이 방문한 장소",
                Places = await MapPlacesAsync(OrderMostVisited(aggregates).Take(DetailTake), cancellationToken)
            },
            TravelRecordsDetailKind.LongUnvisited => new TravelRecordsDetailDto
            {
                Kind = kind,
                Title = "오래 안 간 장소",
                Places = await MapPlacesAsync(OrderLongUnvisited(aggregates).Take(DetailTake), cancellationToken)
            },
            TravelRecordsDetailKind.Recent => new TravelRecordsDetailDto
            {
                Kind = kind,
                Title = "최근 다녀온 장소",
                Places = await MapPlacesAsync(OrderRecent(aggregates).Take(DetailTake), cancellationToken)
            },
            TravelRecordsDetailKind.Countries => new TravelRecordsDetailDto
            {
                Kind = kind,
                Title = "가장 많이 방문한 국가",
                Countries = OrderCountries(aggregates).Take(DetailTake).ToList()
            },
            TravelRecordsDetailKind.Farthest => new TravelRecordsDetailDto
            {
                Kind = kind,
                Title = "가장 멀리 여행한 장소",
                FarthestPlaces = OrderFarthest(aggregates, home).Take(DetailTake).ToList()
            },
            _ => new TravelRecordsDetailDto { Kind = kind, Title = "나의 여행기록" }
        };
    }

    private async Task<TravelRecordsDetailDto> BuildSeasonDetailAsync(
        IReadOnlyList<TravelPlaceAggregateRaw> aggregates,
        TravelSeason season,
        CancellationToken cancellationToken)
    {
        var ranked = OrderSeasonPlaces(aggregates, season).Take(DetailTake).ToList();
        return new TravelRecordsDetailDto
        {
            Kind = TravelRecordsDetailKind.Season,
            Title = $"{GetSeasonEmoji(season)} {GetSeasonLabel(season)} 추억",
            Season = season,
            Places = await MapPlacesAsync(ranked, cancellationToken)
        };
    }

    private static IEnumerable<TravelPlaceAggregateRaw> OrderMostVisited(
        IEnumerable<TravelPlaceAggregateRaw> aggregates) =>
        aggregates
            .Where(item => item.VisitDates.Count > 0)
            .OrderByDescending(item => item.VisitDates.Count)
            .ThenByDescending(item => item.PhotoCount)
            .ThenBy(item => item.PlaceName);

    private static IEnumerable<TravelPlaceAggregateRaw> OrderLongUnvisited(
        IEnumerable<TravelPlaceAggregateRaw> aggregates) =>
        aggregates
            .Where(item =>
                item.VisitDates.Count >= LongUnvisitedMinVisits
                || item.PhotoCount >= LongUnvisitedMinPhotos)
            .Where(item => item.VisitDates.Count > 0)
            .OrderBy(item => item.VisitDates.Max())
            .ThenBy(item => item.PlaceName);

    private static IEnumerable<TravelPlaceAggregateRaw> OrderRecent(
        IEnumerable<TravelPlaceAggregateRaw> aggregates) =>
        aggregates
            .Where(item => item.VisitDates.Count > 0)
            .OrderByDescending(item => item.VisitDates.Max())
            .ThenBy(item => item.PlaceName);

    private static IReadOnlyList<TravelCountrySummaryDto> OrderCountries(
        IEnumerable<TravelPlaceAggregateRaw> aggregates) =>
        aggregates
            // Country is authoritative location metadata and remains useful even when
            // Backend capture_datetime is missing. Keep the truthful visit count at 0.
            .Where(item => !string.IsNullOrWhiteSpace(item.Country))
            .GroupBy(item => item.Country.Trim())
            .Select(group => new
            {
                Country = group.Key,
                VisitRecordCount = group.Sum(item => item.VisitDates.Count),
                PlaceCount = group.Count(),
                PhotoCount = group.Sum(item => item.PhotoCount),
            })
            .OrderByDescending(item => item.VisitRecordCount)
            .ThenByDescending(item => item.PhotoCount)
            .ThenBy(item => item.Country)
            .Select((item, index) => new TravelCountrySummaryDto
            {
                Country = item.Country,
                VisitRecordCount = item.VisitRecordCount,
                PlaceCount = item.PlaceCount,
                Rank = index + 1
            })
            .ToList();

    private static IReadOnlyList<TravelFarthestSummaryDto> OrderFarthest(
        IReadOnlyList<TravelPlaceAggregateRaw> aggregates,
        HomeLocationDto home)
    {
        if (!home.IsConfigured || home.Latitude is null || home.Longitude is null)
        {
            return [];
        }

        var homeLat = home.Latitude.Value;
        var homeLon = home.Longitude.Value;
        var homeLabel = string.IsNullOrWhiteSpace(home.Address) ? "Home" : home.Address;

        return aggregates
            .Where(item => PlaceIdentity.HasValidCoordinates(item.Latitude, item.Longitude))
            .Select(item =>
            {
                var distanceKm = GeoMath.DistanceMeters(
                    homeLat,
                    homeLon,
                    item.Latitude,
                    item.Longitude) / 1000d;

                return new TravelFarthestSummaryDto
                {
                    PlaceId = item.PlaceId,
                    PlaceName = item.PlaceName,
                    Country = item.Country,
                    DistanceKm = Math.Round(distanceKm, 0),
                    Year = item.VisitDates.Count > 0 ? item.VisitDates.Max().Year : null,
                    HomePlaceId = null,
                    HomePlaceName = homeLabel,
                    RepresentativeMediaId = item.RepresentativeMediaId,
                    AbsoluteLibraryPath = item.AbsoluteLibraryPath
                };
            })
            .OrderByDescending(item => item.DistanceKm)
            .ThenBy(item => item.PlaceName)
            .Select((item, index) => new TravelFarthestSummaryDto
            {
                PlaceId = item.PlaceId,
                PlaceName = item.PlaceName,
                Country = item.Country,
                DistanceKm = item.DistanceKm,
                Year = item.Year,
                HomePlaceId = item.HomePlaceId,
                HomePlaceName = item.HomePlaceName,
                RepresentativeMediaId = item.RepresentativeMediaId,
                AbsoluteLibraryPath = item.AbsoluteLibraryPath,
                Rank = index + 1
            })
            .ToList();
    }

    private static IReadOnlyList<TravelSeasonSummaryDto> BuildSeasonHighlights(
        IReadOnlyList<TravelPlaceAggregateRaw> aggregates) =>
        Enum.GetValues<TravelSeason>()
            .Select(season =>
            {
                var top = OrderSeasonPlaces(aggregates, season).FirstOrDefault();
                return new TravelSeasonSummaryDto
                {
                    Season = season,
                    SeasonLabel = GetSeasonLabel(season),
                    Emoji = GetSeasonEmoji(season),
                    PlaceId = top?.PlaceId,
                    PlaceName = top?.PlaceName ?? "기록 없음",
                    VisitRecordCount = top is null ? 0 : CountSeasonVisits(top.VisitDates, season)
                };
            })
            .ToList();

    private static IEnumerable<TravelPlaceAggregateRaw> OrderSeasonPlaces(
        IEnumerable<TravelPlaceAggregateRaw> aggregates,
        TravelSeason season) =>
        aggregates
            .Select(item => new { Item = item, Visits = CountSeasonVisits(item.VisitDates, season) })
            .Where(item => item.Visits > 0)
            .OrderByDescending(item => item.Visits)
            .ThenByDescending(item => item.Item.PhotoCount)
            .ThenBy(item => item.Item.PlaceName)
            .Select(item => item.Item);

    private async Task<IReadOnlyList<TravelPlaceSummaryDto>> MapPlacesAsync(
        IEnumerable<TravelPlaceAggregateRaw> items,
        CancellationToken cancellationToken)
    {
        var list = items.ToList();
        var result = new List<TravelPlaceSummaryDto>(list.Count);
        for (var i = 0; i < list.Count; i++)
        {
            result.Add(await ToPlaceSummaryAsync(list[i], i + 1, cancellationToken));
        }

        return result;
    }

    private Task<TravelPlaceSummaryDto> ToPlaceSummaryAsync(
        TravelPlaceAggregateRaw item,
        int rank,
        CancellationToken cancellationToken)
    {
        var last = item.VisitDates.Count == 0
            ? (DateTimeOffset?)null
            : new DateTimeOffset(item.VisitDates.Max());

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new TravelPlaceSummaryDto
        {
            PlaceId = item.PlaceId,
            PlaceName = item.PlaceName,
            Country = item.Country,
            VisitRecordCount = item.VisitDates.Count,
            LastVisitDate = last,
            RelativeLastVisitText = FormatRelative(last),
            RepresentativeMediaId = item.RepresentativeMediaId,
            AbsoluteLibraryPath = item.AbsoluteLibraryPath,
            TopTags = [],
            Rank = rank
        });
    }

    private async Task<IReadOnlyList<string>> GetTopTagsAsync(
        IReadOnlyList<Guid> mediaIds,
        CancellationToken cancellationToken)
    {
        // Backend V2 photos are not tagged in SQLite; do not invent tags.
        _ = mediaIds;
        _ = cancellationToken;
        await Task.CompletedTask;
        return [];
    }

    public static int[] GetSeasonMonths(TravelSeason season) => season switch
    {
        TravelSeason.Spring => [3, 4, 5],
        TravelSeason.Summer => [6, 7, 8],
        TravelSeason.Autumn => [9, 10, 11],
        TravelSeason.Winter => [12, 1, 2],
        _ => []
    };

    public static string GetSeasonLabel(TravelSeason season) => season switch
    {
        TravelSeason.Spring => "봄",
        TravelSeason.Summer => "여름",
        TravelSeason.Autumn => "가을",
        TravelSeason.Winter => "겨울",
        _ => season.ToString()
    };

    public static string GetSeasonEmoji(TravelSeason season) => season switch
    {
        TravelSeason.Spring => "🌸",
        TravelSeason.Summer => "☀",
        TravelSeason.Autumn => "🍁",
        TravelSeason.Winter => "❄",
        _ => "•"
    };

    private static int CountSeasonVisits(IEnumerable<DateTime> visitDates, TravelSeason season)
    {
        var months = GetSeasonMonths(season).ToHashSet();
        return visitDates
            .Where(date => months.Contains(date.Month))
            .Select(date => date.Date)
            .Distinct()
            .Count();
    }

    public static string FormatRelative(DateTimeOffset? date)
    {
        if (date is null)
        {
            return string.Empty;
        }

        var days = Math.Max(0, (DateTime.Now.Date - date.Value.Date).Days);
        if (days <= 0)
        {
            return "오늘";
        }

        if (days < 7)
        {
            return $"{days}일 전";
        }

        if (days < 30)
        {
            return $"{Math.Max(1, days / 7)}주 전";
        }

        if (days < 365)
        {
            return $"{Math.Max(1, days / 30)}개월 전";
        }

        return $"{Math.Max(1, days / 365)}년 전";
    }
}
