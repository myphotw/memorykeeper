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
    private const int MemoryCardPhotoTake = 4;
    private const double DomesticTravelMinimumDistanceKm = 2d;

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
        // The aggregate payload, memory candidates, and local Home setting are independent.
        // Start them together so the Travel screen can render its text/cards as soon as the
        // aggregate response is ready; image loading remains post-render in the ViewModel.
        var aggregatesTask = _travelRecordsRepository.GetPlaceAggregatesAsync(cancellationToken);
        var countryAggregatesTask = _travelRecordsRepository.GetCountryAggregatesAsync(cancellationToken);
        var memoryCandidatesTask = _travelRecordsRepository.GetMemoryCandidatesAsync(
            DateOnly.FromDateTime(DateTime.Today), MemoryCardPhotoTake * 5, cancellationToken);
        var homeTask = _homeLocationService.GetAsync(cancellationToken);

        IReadOnlyList<TravelPlaceAggregateRaw> aggregates;
        try
        {
            aggregates = await aggregatesTask;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "FarthestTrip aggregate request failed. Result=unavailable");
            throw;
        }
        var countryAggregates = await countryAggregatesTask;
        IReadOnlyList<TravelMemoryCandidateRaw> memoryCandidates;
        try
        {
            memoryCandidates = await memoryCandidatesTask;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Travel memories failed; aggregate dashboard remains available.");
            memoryCandidates = [];
        }
        _logger.LogInformation(
            "TravelRecords dashboard aggregates. Places={Places}, WithVisits={WithVisits}, UndatedOnly={Undated}",
            aggregates.Count,
            aggregates.Count(a => a.VisitDates.Count > 0),
            aggregates.Count(a => a.VisitDates.Count == 0 && a.PhotoCount > 0));

        if (aggregates.Count == 0 && countryAggregates.Count == 0)
        {
            return new TravelRecordsDashboardDto();
        }

        var home = await homeTask;
        var mostVisited = OrderMostVisited(aggregates).FirstOrDefault();
        var longUnvisited = OrderLongUnvisited(aggregates).FirstOrDefault();
        var recent = OrderRecent(aggregates).Take(RecentCardTake).ToList();
        var countries = countryAggregates.Count > 0 ? OrderCountries(countryAggregates) : OrderCountries(aggregates);
        var countryVisitStatistics = countryAggregates.Count > 0
            ? BuildCountryVisitStatistics(countryAggregates)
            : BuildCountryVisitStatistics(aggregates);
        var farthestCandidates = OrderFarthest(aggregates, home);
        _logger.LogInformation(
            "FarthestTrip HomeConfigured={HomeConfigured}, HomeCoordinatesPresent={HomeCoordinatesPresent}, AggregatePlaces={AggregatePlaces}, ValidCoordinatePlaces={ValidCoordinatePlaces}, Candidates={Candidates}, Result={Result}",
            home.IsConfigured,
            home.Latitude.HasValue && home.Longitude.HasValue,
            aggregates.Count,
            aggregates.Count(item => PlaceIdentity.HasValidCoordinates(item.Latitude, item.Longitude)),
            farthestCandidates.Count,
            farthestCandidates.Count > 0 ? "selected" : "none");

        return new TravelRecordsDashboardDto
        {
            DomesticTripCount = countryAggregates.Count > 0
                ? countryAggregates.Where(item => IsDomesticCountry(item.Country)).Sum(item => item.VisitCount)
                : CountDomesticTrips(aggregates, home),
            ForeignTripCount = countryVisitStatistics.Sum(item => item.VisitCount),
            ForeignPlaceCount = CountForeignPlaces(aggregates),
            ForeignPhotoCount = countryAggregates.Count > 0
                ? countryAggregates.Where(item => TryNormalizeForeignCountry(item.Country) is not null).Sum(item => item.PhotoCount)
                : CountPhotoCount(aggregates.Where(item => TryNormalizeForeignCountry(item.Country) is not null)),
            DomesticPlaceCount = CountDomesticPlaces(aggregates),
            DomesticPhotoCount = countryAggregates.Count > 0
                ? countryAggregates.Where(item => IsDomesticCountry(item.Country)).Sum(item => item.PhotoCount)
                : CountPhotoCount(aggregates.Where(item => IsDomesticCountry(item.Country))),
            UniquePhotoCount = countryAggregates.Count > 0 ? countryAggregates.Sum(item => item.PhotoCount) : CountPhotoCount(aggregates),
            DistinctPlaceCount = CountDistinctPlaces(aggregates),
            VisitedForeignCountryCount = countryVisitStatistics.Count,
            MostVisitedPlace = mostVisited is null
                ? null
                : await ToPlaceSummaryAsync(mostVisited, 1, cancellationToken),
            LongUnvisitedPlace = longUnvisited is null
                ? null
                : await ToPlaceSummaryAsync(longUnvisited, 1, cancellationToken),
            SeasonHighlights = BuildSeasonHighlights(aggregates),
            RecentPlaces = await MapPlacesAsync(recent, cancellationToken),
            TopCountry = countries.FirstOrDefault(),
            FarthestPlace = farthestCandidates.FirstOrDefault(),
            CountryVisitStatistics = countryVisitStatistics,
            ForeignCountries = countryAggregates.Count > 0
                ? BuildForeignCountries(countryAggregates)
                : BuildForeignCountries(countryVisitStatistics, aggregates),
            MemoryCards = memoryCandidates.Count > 0
                ? BuildMemoryCardsFromCandidates(memoryCandidates, DateOnly.FromDateTime(DateTime.Today))
                : BuildMemoryCards(aggregates, DateOnly.FromDateTime(DateTime.Today)),
            YearChapters = BuildYearChapters(aggregates)
        };
    }

    private static IReadOnlyList<TravelCountryVisitSummaryDto> BuildCountryVisitStatistics(
        IReadOnlyList<TravelPlaceAggregateRaw> aggregates)
    {
        if (!aggregates.SelectMany(item => item.Photos).Any())
        {
            return aggregates
                .Select(item => new { Aggregate = item, Country = TryNormalizeForeignCountry(item.Country) })
                .Where(item => item.Country is not null)
                .GroupBy(item => item.Country!, StringComparer.OrdinalIgnoreCase)
                .Select(group => new
                {
                    Country = group.Key,
                    Dates = group.SelectMany(item => item.Aggregate.VisitDates).Distinct().OrderBy(date => date).ToList(),
                    PhotoCount = group.Sum(item => item.Aggregate.PhotoCount),
                })
                .Where(item => item.Dates.Count > 0)
                .OrderByDescending(item => CountConsecutiveDateRanges(item.Dates))
                .ThenByDescending(item => item.PhotoCount)
                .ThenBy(item => item.Country, StringComparer.OrdinalIgnoreCase)
                .Select((item, index) => new TravelCountryVisitSummaryDto
                {
                    Country = item.Country, VisitCount = CountConsecutiveDateRanges(item.Dates),
                    CapturedDayCount = item.Dates.Count, Rank = index + 1,
                }).ToList();
        }
        return aggregates
            .SelectMany(item => item.Photos)
            .Select(photo => new
            {
                Photo = photo,
                Country = TryNormalizeForeignCountry(photo.Country),
            })
            .Where(item => item.Country is not null)
            .GroupBy(item => item.Country!, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var dates = group
                    .Where(item => item.Photo.CapturedAt.HasValue)
                    .Select(item => item.Photo.CapturedAt!.Value.ToLocalTime().Date)
                    .Distinct()
                    .OrderBy(date => date)
                    .ToList();
                return new
                {
                    Country = group.Key,
                    Dates = dates,
                    VisitCount = CountConsecutiveDateRanges(dates),
                    PhotoCount = group.Count(),
                };
            })
            .Where(item => item.Dates.Count > 0)
            .OrderByDescending(item => item.VisitCount)
            .ThenByDescending(item => item.PhotoCount)
            .ThenBy(item => item.Country, StringComparer.OrdinalIgnoreCase)
            .Select((item, index) => new TravelCountryVisitSummaryDto
            {
                Country = item.Country,
                VisitCount = item.VisitCount,
                CapturedDayCount = item.Dates.Count,
                Rank = index + 1,
            })
            .ToList();
    }

    private static int CountUniquePhotos(IEnumerable<TravelPlaceAggregateRaw> aggregates) =>
        CountUniquePhotos(aggregates.SelectMany(item => item.Photos));

    private static int CountPhotoCount(IEnumerable<TravelPlaceAggregateRaw> aggregates)
    {
        var list = aggregates.ToList();
        return list.SelectMany(item => item.Photos).Any()
            ? CountUniquePhotos(list)
            : list.Sum(item => item.PhotoCount);
    }

    private static IReadOnlyList<TravelCountryVisitSummaryDto> BuildCountryVisitStatistics(
        IReadOnlyList<TravelCountryAggregateRaw> countries) =>
        countries.Select(item => new { Item = item, Country = TryNormalizeForeignCountry(item.Country) })
            .Where(item => item.Country is not null)
            .OrderByDescending(item => item.Item.VisitCount)
            .ThenByDescending(item => item.Item.PhotoCount)
            .ThenBy(item => item.Country, StringComparer.OrdinalIgnoreCase)
            .Select((item, index) => new TravelCountryVisitSummaryDto
            {
                Country = item.Country!, VisitCount = item.Item.VisitCount,
                CapturedDayCount = item.Item.CaptureDates.Count, Rank = index + 1,
            }).ToList();

    private static int CountUniquePhotos(IEnumerable<TravelPhotoCandidateRaw> photos) =>
        photos
            .Select(GetPhotoIdentity)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

    private static string? GetPhotoIdentity(TravelPhotoCandidateRaw photo)
    {
        if (!string.IsNullOrWhiteSpace(photo.BackendFileId))
        {
            return $"file:{photo.BackendFileId.Trim()}";
        }

        return photo.MediaId is Guid mediaId && mediaId != Guid.Empty
            ? $"media:{mediaId:N}"
            : null;
    }

    private static int CountDistinctPlaces(IEnumerable<TravelPlaceAggregateRaw> aggregates) =>
        aggregates
            .Where(item => !item.IsUnclassified && item.PlaceId != Guid.Empty)
            .Select(item => item.PlaceId)
            .Distinct()
            .Count();

    private static int CountForeignPlaces(IEnumerable<TravelPlaceAggregateRaw> aggregates) =>
        aggregates
            .Where(item => !item.IsUnclassified && item.PlaceId != Guid.Empty)
            .Where(item => TryNormalizeForeignCountry(item.Country) is not null)
            .Select(item => item.PlaceId)
            .Distinct()
            .Count();

    private static int CountDomesticPlaces(IEnumerable<TravelPlaceAggregateRaw> aggregates) =>
        aggregates
            .Where(item => !item.IsUnclassified && item.PlaceId != Guid.Empty)
            .Where(item => IsDomesticCountry(item.Country))
            .Select(item => item.PlaceId)
            .Distinct()
            .Count();

    private static string? TryNormalizeForeignCountry(string? country)
    {
        var normalized = PlaceNormalizer.NormalizeCountry(country);
        return string.IsNullOrWhiteSpace(normalized)
               || string.Equals(normalized, GalleryHierarchyService.OtherTitle, StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, GalleryHierarchyService.UnclassifiedTitle, StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, "대한민국", StringComparison.OrdinalIgnoreCase)
            ? null
            : normalized;
    }

    private static int CountConsecutiveDateRanges(IReadOnlyList<DateTime> orderedDates)
    {
        if (orderedDates.Count == 0)
        {
            return 0;
        }

        var visits = 1;
        for (var index = 1; index < orderedDates.Count; index++)
        {
            if ((orderedDates[index].Date - orderedDates[index - 1].Date).Days > 1)
            {
                visits++;
            }
        }

        return visits;
    }

    private static int CountDomesticTrips(
        IEnumerable<TravelPlaceAggregateRaw> aggregates,
        HomeLocationDto home)
    {
        if (!home.IsConfigured
            || home.Latitude is not double homeLatitude
            || home.Longitude is not double homeLongitude
            || !PlaceIdentity.HasValidCoordinates(homeLatitude, homeLongitude))
        {
            return 0;
        }

        var visitDates = aggregates
            .Where(item => IsDomesticCountry(item.Country))
            .Where(item => PlaceIdentity.HasValidCoordinates(item.Latitude, item.Longitude))
            .Where(item => GeoMath.DistanceMeters(
                               homeLatitude,
                               homeLongitude,
                               item.Latitude,
                               item.Longitude) / 1000d > DomesticTravelMinimumDistanceKm)
            .SelectMany(item => item.VisitDates)
            .Select(date => date.Date)
            .Distinct()
            .OrderBy(date => date)
            .ToList();

        return CountConsecutiveDateRanges(visitDates);
    }

    private static IReadOnlyList<TravelForeignCountryDto> BuildForeignCountries(
        IReadOnlyList<TravelCountryVisitSummaryDto> statistics,
        IReadOnlyList<TravelPlaceAggregateRaw> aggregates)
    {
        var photosByCountry = aggregates
            .SelectMany(item => item.Photos)
            .Select(photo => new { Photo = photo, Country = TryNormalizeForeignCountry(photo.Country) })
            .Where(item => item.Country is not null)
            .GroupBy(item => item.Country!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Photo).ToList(), StringComparer.OrdinalIgnoreCase);

        var aggregateByCountry = aggregates
            .Select(item => new { Aggregate = item, Country = TryNormalizeForeignCountry(item.Country) })
            .Where(item => item.Country is not null)
            .GroupBy(item => item.Country!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Aggregate).ToList(), StringComparer.OrdinalIgnoreCase);
        return statistics
            .Select(statistic =>
            {
                photosByCountry.TryGetValue(statistic.Country, out var photos);
                photos ??= [];
                aggregateByCountry.TryGetValue(statistic.Country, out var aggregateRows);
                aggregateRows ??= [];
                var representative = photos
                    .Where(photo => !string.IsNullOrWhiteSpace(photo.ThumbnailPath))
                    .OrderByDescending(photo => photo.CapturedAt)
                    .ThenBy(photo => GetPhotoIdentity(photo), StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();

                return new TravelForeignCountryDto
                {
                    Country = statistic.Country,
                    VisitCount = statistic.VisitCount,
                    PhotoCount = photos.Count > 0 ? CountUniquePhotos(photos) : aggregateRows.Sum(item => item.PhotoCount),
                    RepresentativeMediaId = representative?.MediaId,
                    ThumbnailPath = representative?.ThumbnailPath ?? string.Empty,
                };
            })
            .ToList();
    }

    private static IReadOnlyList<TravelForeignCountryDto> BuildForeignCountries(
        IReadOnlyList<TravelCountryAggregateRaw> countries) =>
        countries.Select(item => new { Item = item, Country = TryNormalizeForeignCountry(item.Country) })
            .Where(item => item.Country is not null)
            .OrderByDescending(item => item.Item.VisitCount)
            .ThenByDescending(item => item.Item.PhotoCount)
            .ThenBy(item => item.Country, StringComparer.OrdinalIgnoreCase)
            .Select(item => new TravelForeignCountryDto
            {
                Country = item.Country!, VisitCount = item.Item.VisitCount, PhotoCount = item.Item.PhotoCount,
                RepresentativeMediaId = item.Item.RepresentativeMediaId,
                ThumbnailPath = item.Item.RepresentativeThumbnailPath ?? string.Empty,
            }).ToList();

    private static IReadOnlyList<TravelMemoryCardDto> BuildMemoryCardsFromCandidates(
        IReadOnlyList<TravelMemoryCandidateRaw> candidates,
        DateOnly today)
    {
        // Fast Travel returns candidates rather than pre-built cards.  Keep the established
        // client-side selection semantics: the backend category is advisory only and must not
        // collapse "N년 전 이맘때" and rediscovered memories into one generic bucket.
        var usable = candidates
            .Where(item => item.CaptureDate < today && item.PlaceId is Guid placeId && placeId != Guid.Empty)
            .GroupBy(MemoryCandidateKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        if (usable.Count == 0)
        {
            return [];
        }

        var cards = new List<TravelMemoryCardDto>(4);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedYears = new HashSet<int>();

        var exactYear = usable
            .Where(item => item.CaptureDate.Month == today.Month
                           && item.CaptureDate.Day == today.Day
                           && item.CaptureDate.Year < today.Year)
            .GroupBy(item => item.CaptureDate.Year)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .FirstOrDefault();
        if (exactYear is not null)
        {
            AddCandidateMemoryCard(cards, used, usedYears, TravelMemoryCardKind.YearsAgoToday,
                $"{today.Year - exactYear.Key}년 전 오늘",
                new DateTime(exactYear.Key, today.Month, today.Day).ToString("yyyy.MM.dd"),
                exactYear.OrderByDescending(item => item.CaptureDate));
        }

        var lastYear = today.Year - 1;
        if (!usedYears.Contains(lastYear))
        {
            AddCandidateMemoryCard(cards, used, usedYears, TravelMemoryCardKind.LastYearAroundNow,
                "작년 이맘때", $"{lastYear}년 {today.Month}월의 추억",
                usable.Where(item => item.CaptureDate.Year == lastYear && item.CaptureDate.Month == today.Month)
                    .OrderBy(item => Math.Abs(item.CaptureDate.Day - today.Day)));
        }

        var aroundYear = usable
            .Where(item => item.CaptureDate.Year < lastYear
                           && item.CaptureDate.Month == today.Month
                           && !usedYears.Contains(item.CaptureDate.Year))
            .GroupBy(item => item.CaptureDate.Year)
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Key)
            .FirstOrDefault();
        if (aroundYear is not null)
        {
            AddCandidateMemoryCard(cards, used, usedYears, TravelMemoryCardKind.YearsAgoAroundNow,
                $"{today.Year - aroundYear.Key}년 전 이맘때",
                $"{aroundYear.Key}년 {today.Month}월의 추억",
                aroundYear.OrderBy(item => Math.Abs(item.CaptureDate.Day - today.Day)));
        }

        var rediscoveredYear = usable
            .Where(item => !usedYears.Contains(item.CaptureDate.Year))
            .GroupBy(item => item.CaptureDate.Year)
            .OrderBy(group => group.Key)
            .ThenByDescending(group => group.Count())
            .FirstOrDefault();
        if (rediscoveredYear is not null)
        {
            AddCandidateMemoryCard(cards, used, usedYears, TravelMemoryCardKind.Rediscovered,
                "오랜만에 꺼내본 추억", $"{rediscoveredYear.Key}년의 추억",
                rediscoveredYear.OrderBy(item => item.CaptureDate));
        }

        return cards;
    }

    private static string MemoryCandidateKey(TravelMemoryCandidateRaw item) =>
        item.MediaId is Guid mediaId && mediaId != Guid.Empty
            ? $"media:{mediaId:N}"
            : $"{item.PlaceId?.ToString("N")}|{item.CaptureDate:yyyyMMdd}|{item.ThumbnailPath}";

    private static void AddCandidateMemoryCard(
        ICollection<TravelMemoryCardDto> cards,
        ISet<string> used,
        ISet<int> usedYears,
        TravelMemoryCardKind kind,
        string title,
        string subtitle,
        IEnumerable<TravelMemoryCandidateRaw> candidates)
    {
        var selected = candidates
            .Where(item => !used.Contains(MemoryCandidateKey(item)))
            .Take(MemoryCardPhotoTake)
            .ToList();
        if (selected.Count == 0)
        {
            return;
        }

        foreach (var item in selected)
        {
            used.Add(MemoryCandidateKey(item));
        }

        usedYears.Add(selected[0].CaptureDate.Year);
        cards.Add(new TravelMemoryCardDto
        {
            Kind = kind,
            Title = title,
            Subtitle = subtitle,
            FocusPlaceId = selected[0].PlaceId!.Value,
            RepresentativeMediaId = selected[0].MediaId,
            Photos = selected.Select(item => new TravelMemoryPhotoDto
            {
                MediaId = item.MediaId,
                PlaceId = item.PlaceId!.Value,
                PlaceName = item.PlaceName,
                Country = item.Country,
                CapturedAt = new DateTimeOffset(item.CaptureDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                ThumbnailPath = item.ThumbnailPath,
            }).ToList(),
        });
    }

    private static IReadOnlyList<TravelMemoryCardDto> BuildMemoryCards(
        IReadOnlyList<TravelPlaceAggregateRaw> aggregates,
        DateOnly today)
    {
        var candidates = aggregates
            .SelectMany(place => place.Photos
                .Where(photo => photo.CapturedAt.HasValue)
                .Where(photo => !string.IsNullOrWhiteSpace(photo.ThumbnailPath))
                .Select(photo => new MemoryPhotoCandidate(
                    place.PlaceId,
                    place.PlaceName,
                    place.Country,
                    photo,
                    DateOnly.FromDateTime(photo.CapturedAt!.Value.ToLocalTime().Date))))
            .Where(candidate => candidate.Date < today)
            .GroupBy(candidate => candidate.StableKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        if (candidates.Count == 0)
        {
            return [];
        }

        var cards = new List<TravelMemoryCardDto>(4);
        var usedPhotos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedYears = new HashSet<int>();

        var exactYearGroup = candidates
            .Where(candidate => candidate.Date.Month == today.Month
                                && candidate.Date.Day == today.Day
                                && candidate.Date.Year < today.Year)
            .GroupBy(candidate => candidate.Date.Year)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .FirstOrDefault();
        if (exactYearGroup is not null)
        {
            AddMemoryCard(
                cards,
                usedPhotos,
                usedYears,
                TravelMemoryCardKind.YearsAgoToday,
                $"{today.Year - exactYearGroup.Key}년 전 오늘",
                new DateTime(exactYearGroup.Key, today.Month, today.Day).ToString("yyyy.MM.dd"),
                exactYearGroup
                    .OrderByDescending(candidate => candidate.Photo.IsFavorite)
                    .ThenBy(candidate => candidate.StableKey, StringComparer.OrdinalIgnoreCase));
        }

        var lastYear = today.Year - 1;
        if (!usedYears.Contains(lastYear))
        {
            AddMemoryCard(
                cards,
                usedPhotos,
                usedYears,
                TravelMemoryCardKind.LastYearAroundNow,
                "작년 이맘때",
                $"{lastYear}년 {today.Month}월의 추억",
                candidates
                    .Where(candidate => candidate.Date.Year == lastYear
                                        && candidate.Date.Month == today.Month)
                    .OrderBy(candidate => Math.Abs(candidate.Date.Day - today.Day))
                    .ThenByDescending(candidate => candidate.Photo.IsFavorite)
                    .ThenBy(candidate => candidate.StableKey, StringComparer.OrdinalIgnoreCase));
        }

        var aroundYearGroup = candidates
            .Where(candidate => candidate.Date.Year < lastYear
                                && candidate.Date.Month == today.Month
                                && !usedYears.Contains(candidate.Date.Year))
            .GroupBy(candidate => candidate.Date.Year)
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Key)
            .FirstOrDefault();
        if (aroundYearGroup is not null)
        {
            AddMemoryCard(
                cards,
                usedPhotos,
                usedYears,
                TravelMemoryCardKind.YearsAgoAroundNow,
                $"{today.Year - aroundYearGroup.Key}년 전 이맘때",
                $"{aroundYearGroup.Key}년 {today.Month}월의 추억",
                aroundYearGroup
                    .OrderBy(candidate => Math.Abs(candidate.Date.Day - today.Day))
                    .ThenByDescending(candidate => candidate.Photo.IsFavorite)
                    .ThenBy(candidate => candidate.StableKey, StringComparer.OrdinalIgnoreCase));
        }

        var rediscoveredYearGroup = candidates
            .Where(candidate => !usedYears.Contains(candidate.Date.Year))
            .GroupBy(candidate => candidate.Date.Year)
            .OrderBy(group => group.Key)
            .ThenByDescending(group => group.Count())
            .FirstOrDefault();
        if (rediscoveredYearGroup is not null)
        {
            AddMemoryCard(
                cards,
                usedPhotos,
                usedYears,
                TravelMemoryCardKind.Rediscovered,
                "오랜만에 꺼내본 추억",
                $"{rediscoveredYearGroup.Key}년의 추억",
                rediscoveredYearGroup
                    .OrderByDescending(candidate => candidate.Photo.IsFavorite)
                    .ThenBy(candidate => candidate.Date)
                    .ThenBy(candidate => candidate.StableKey, StringComparer.OrdinalIgnoreCase));
        }

        return cards;
    }

    private static void AddMemoryCard(
        ICollection<TravelMemoryCardDto> cards,
        ISet<string> usedPhotos,
        ISet<int> usedYears,
        TravelMemoryCardKind kind,
        string title,
        string subtitle,
        IEnumerable<MemoryPhotoCandidate> candidates)
    {
        var selected = candidates
            .Where(candidate => !usedPhotos.Contains(candidate.StableKey))
            .Take(MemoryCardPhotoTake)
            .ToList();
        if (selected.Count == 0)
        {
            return;
        }

        foreach (var candidate in selected)
        {
            usedPhotos.Add(candidate.StableKey);
        }

        usedYears.Add(selected[0].Date.Year);
        cards.Add(new TravelMemoryCardDto
        {
            Kind = kind,
            Title = title,
            Subtitle = subtitle,
            FocusPlaceId = selected[0].PlaceId,
            RepresentativeMediaId = selected[0].Photo.MediaId,
            Photos = selected.Select(candidate => new TravelMemoryPhotoDto
            {
                MediaId = candidate.Photo.MediaId,
                PlaceId = candidate.PlaceId,
                PlaceName = candidate.PlaceName,
                Country = candidate.Country,
                CapturedAt = candidate.Photo.CapturedAt!.Value,
                ThumbnailPath = candidate.Photo.ThumbnailPath,
            }).ToList(),
        });
    }

    private sealed record MemoryPhotoCandidate(
        Guid PlaceId,
        string PlaceName,
        string Country,
        TravelPhotoCandidateRaw Photo,
        DateOnly Date)
    {
        public string StableKey => !string.IsNullOrWhiteSpace(Photo.BackendFileId)
            ? Photo.BackendFileId.Trim()
            : Photo.MediaId?.ToString("N")
              ?? $"{Photo.ThumbnailPath}|{Photo.CapturedAt:O}";
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
        => string.Equals(
            PlaceNormalizer.NormalizeCountry(country),
            "대한민국",
            StringComparison.OrdinalIgnoreCase);

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
            LocationText = string.Join(" 쨌 ", locationParts),
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
        var countryAggregates = await _travelRecordsRepository.GetCountryAggregatesAsync(cancellationToken);
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
                Countries = (countryAggregates.Count > 0 ? OrderCountries(countryAggregates) : OrderCountries(aggregates)).Take(DetailTake).ToList()
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
            .OrderByDescending(item => item.ResolvedVisitCount)
            .ThenByDescending(item => item.PhotoCount)
            .ThenBy(item => item.PlaceName);

    private static IEnumerable<TravelPlaceAggregateRaw> OrderLongUnvisited(
        IEnumerable<TravelPlaceAggregateRaw> aggregates) =>
        aggregates
            .Where(item =>
                item.ResolvedVisitCount >= LongUnvisitedMinVisits
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
                VisitRecordCount = group.Sum(item => item.ResolvedVisitCount),
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

    private static IReadOnlyList<TravelCountrySummaryDto> OrderCountries(
        IEnumerable<TravelCountryAggregateRaw> countries) =>
        countries.Where(item => !string.IsNullOrWhiteSpace(item.Country))
            .OrderByDescending(item => item.VisitCount)
            .ThenByDescending(item => item.PhotoCount)
            .ThenBy(item => item.Country)
            .Select((item, index) => new TravelCountrySummaryDto
            {
                Country = item.Country, VisitRecordCount = item.VisitCount, PlaceCount = 0, Rank = index + 1,
            }).ToList();

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
            VisitRecordCount = item.ResolvedVisitCount,
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
