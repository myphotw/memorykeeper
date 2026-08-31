using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Domain.Entities;
using MemoryKeeper.Domain.Enums;
using MemoryKeeper.Infrastructure.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using StorageEntity = MemoryKeeper.Domain.Entities.Storage;

namespace MemoryKeeper.Tests.UnitTests;

public class TravelRecordsServiceTests
{
    [Fact]
    public async Task GetDashboardAsync_ComputesMostVisitedLongUnvisitedSeasonCountryAndFarthest()
    {
        var storageId = Guid.NewGuid();
        var sokcho = CreatePlace("속초", "대한민국", 38.2070, 128.5918);
        var busan = CreatePlace("부산", "대한민국", 35.1796, 129.0756);
        var maldives = CreatePlace("몰디브", "몰디브", 3.2028, 73.2207);

        var now = DateTime.Now;
        var media = new List<Media>
        {
            CreatePhoto("s1.jpg", storageId, sokcho, new DateTimeOffset(now.Year - 1, 4, 10, 10, 0, 0, TimeSpan.Zero)),
            CreatePhoto("s2.jpg", storageId, sokcho, new DateTimeOffset(now.Year - 1, 4, 11, 10, 0, 0, TimeSpan.Zero)),
            CreatePhoto("s3.jpg", storageId, sokcho, new DateTimeOffset(now.Year - 1, 7, 1, 10, 0, 0, TimeSpan.Zero)),
            CreatePhoto("b1.jpg", storageId, busan, new DateTimeOffset(2019, 5, 18, 10, 0, 0, TimeSpan.Zero)),
            CreatePhoto("b2.jpg", storageId, busan, new DateTimeOffset(2019, 5, 19, 10, 0, 0, TimeSpan.Zero)),
            CreatePhoto("m1.jpg", storageId, maldives, new DateTimeOffset(2024, 1, 5, 10, 0, 0, TimeSpan.Zero))
        };

        var storage = new StorageEntity
        {
            Id = storageId,
            Name = "Local",
            PhotoRoot = @"D:\Library",
            StorageType = StorageType.Local,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var fileAccess = new FakeFileAccessService();
        var repository = new InMemoryTravelRecordsRepository(fileAccess, media, [sokcho, busan, maldives], [storage]);
        var homeLocation = new HomeLocationService(
            new HomeSettingRepository(new Dictionary<string, string>
            {
                [SettingKeys.TravelHomeLatitude] = "37.5665",
                [SettingKeys.TravelHomeLongitude] = "126.9780",
                [SettingKeys.TravelHomeAddress] = "Seoul"
            }),
            new NoOpLocationResolver(),
            NullLogger<HomeLocationService>.Instance);
        var service = new TravelRecordsService(
            repository,
            new InMemoryMediaTagRepository(),
            new InMemoryTagRepository(),
            homeLocation,
            NullLogger<TravelRecordsService>.Instance);

        var dashboard = await service.GetDashboardAsync();

        Assert.NotNull(dashboard.MostVisitedPlace);
        Assert.Equal(sokcho.Id, dashboard.MostVisitedPlace!.PlaceId);
        Assert.Equal(3, dashboard.MostVisitedPlace.VisitRecordCount);

        Assert.NotNull(dashboard.LongUnvisitedPlace);
        Assert.Equal(busan.Id, dashboard.LongUnvisitedPlace!.PlaceId);

        Assert.Equal(4, dashboard.SeasonHighlights.Count);
        Assert.Contains(dashboard.SeasonHighlights, item =>
            item.Season == TravelSeason.Spring && item.PlaceId == sokcho.Id);

        Assert.NotNull(dashboard.TopCountry);
        Assert.Equal("대한민국", dashboard.TopCountry!.Country);

        Assert.NotNull(dashboard.FarthestPlace);
        Assert.Equal(maldives.Id, dashboard.FarthestPlace!.PlaceId);
        Assert.True(dashboard.FarthestPlace.DistanceKm > 1000);

        Assert.NotEmpty(dashboard.YearChapters);
        Assert.Contains(dashboard.YearChapters, chapter => chapter.Year == 2024);
        Assert.Contains(dashboard.YearChapters, chapter => chapter.Year == 2019);
        var chapter2024 = Assert.Single(dashboard.YearChapters, chapter => chapter.Year == 2024);
        Assert.Contains(chapter2024.Trips, trip => trip.TripName == "몰디브");
        var sokchoTrip = dashboard.YearChapters
            .SelectMany(chapter => chapter.Trips)
            .FirstOrDefault(trip => trip.TripName == "속초");
        Assert.NotNull(sokchoTrip);
        Assert.Equal(sokcho.Id, sokchoTrip!.FocusPlaceId);

        var mostVisitedDetail = await service.GetDetailAsync(TravelRecordsDetailKind.MostVisited);
        Assert.True(mostVisitedDetail.Places.Count >= 2);
        Assert.Equal(1, mostVisitedDetail.Places[0].Rank);

        var seasonDetail = await service.GetDetailAsync(TravelRecordsDetailKind.Season, TravelSeason.Winter);
        Assert.Contains(seasonDetail.Places, item => item.PlaceId == maldives.Id);
    }

    [Fact]
    public async Task GetDashboardAsync_CreatesUndatedYearChapter_WithoutInventingVisitDays()
    {
        var repo = new FixedAggregates(
        [
            new TravelPlaceAggregateRaw
            {
                PlaceId = Guid.NewGuid(),
                PlaceName = "날짜없는곳",
                Country = "대한민국",
                PhotoCount = 2,
                VisitDates = [],
                AbsoluteLibraryPath = "http://127.0.0.1:8000/api/common/gallery/x/thumbnail",
            },
            new TravelPlaceAggregateRaw
            {
                PlaceId = Guid.NewGuid(),
                PlaceName = "속초",
                Country = "대한민국",
                PhotoCount = 1,
                VisitDates = [new DateTime(2025, 12, 20)],
                AbsoluteLibraryPath = "http://127.0.0.1:8000/api/common/gallery/y/thumbnail",
            },
        ]);

        var homeLocation = new HomeLocationService(
            new HomeSettingRepository([]),
            new NoOpLocationResolver(),
            NullLogger<HomeLocationService>.Instance);
        var service = new TravelRecordsService(
            repo,
            new InMemoryMediaTagRepository(),
            new InMemoryTagRepository(),
            homeLocation,
            NullLogger<TravelRecordsService>.Instance);

        var dashboard = await service.GetDashboardAsync();
        Assert.Contains(dashboard.YearChapters, c => c.Year == 2025);
        var undated = Assert.Single(dashboard.YearChapters, c => c.Year == 0);
        Assert.Equal("날짜 미상", undated.YearTitle);
        Assert.Contains(undated.Trips, t => t.TripName == "날짜없는곳" && t.VisitDayCount == 0 && t.PhotoCount == 2);
        Assert.Equal("속초", dashboard.MostVisitedPlace?.PlaceName);
        Assert.Equal(1, dashboard.MostVisitedPlace?.VisitRecordCount);
    }

    [Fact]
    public async Task GetDashboardAsync_WithConfiguredHome_IgnoresRecordsWithoutValidCoordinatesForFarthest()
    {
        var repo = new FixedAggregates(
        [
            new TravelPlaceAggregateRaw
            {
                PlaceId = Guid.NewGuid(),
                PlaceName = "좌표없음",
                Country = "대한민국",
                Latitude = 0,
                Longitude = 0,
                PhotoCount = 1,
                VisitDates = [new DateTime(2025, 1, 1)],
            },
        ]);
        var homeLocation = new HomeLocationService(
            new HomeSettingRepository(new Dictionary<string, string>
            {
                [SettingKeys.TravelHomeLatitude] = "37.5665",
                [SettingKeys.TravelHomeLongitude] = "126.9780",
                [SettingKeys.TravelHomeAddress] = "서울",
            }),
            new NoOpLocationResolver(),
            NullLogger<HomeLocationService>.Instance);
        var service = new TravelRecordsService(
            repo,
            new InMemoryMediaTagRepository(),
            new InMemoryTagRepository(),
            homeLocation,
            NullLogger<TravelRecordsService>.Instance);

        var dashboard = await service.GetDashboardAsync();

        Assert.Null(dashboard.FarthestPlace);
    }

    [Fact]
    public async Task GetDashboardAsync_OneUndatedNasPhoto_StillUsesCountryAndGpsForInsights()
    {
        var guryeId = Guid.NewGuid();
        var repo = new FixedAggregates(
        [
            new TravelPlaceAggregateRaw
            {
                PlaceId = guryeId,
                PlaceName = "원기교",
                Country = "대한민국",
                Latitude = 35.22742,
                Longitude = 127.59052,
                PhotoCount = 1,
                VisitDates = [],
            },
        ]);
        var homeLocation = new HomeLocationService(
            new HomeSettingRepository(new Dictionary<string, string>
            {
                [SettingKeys.TravelHomeLatitude] = "37.495",
                [SettingKeys.TravelHomeLongitude] = "126.875",
                [SettingKeys.TravelHomeAddress] = "대한민국 서울특별시 구로구 구일로8길 6",
            }),
            new NoOpLocationResolver(),
            NullLogger<HomeLocationService>.Instance);
        var service = new TravelRecordsService(
            repo,
            new InMemoryMediaTagRepository(),
            new InMemoryTagRepository(),
            homeLocation,
            NullLogger<TravelRecordsService>.Instance);

        var dashboard = await service.GetDashboardAsync();

        Assert.Equal("대한민국", dashboard.TopCountry?.Country);
        Assert.Equal(0, dashboard.TopCountry?.VisitRecordCount);
        Assert.Equal(guryeId, dashboard.FarthestPlace?.PlaceId);
        Assert.True(dashboard.FarthestPlace?.DistanceKm > 200);
        Assert.Null(dashboard.FarthestPlace?.Year);
    }

    [Fact]
    public async Task GetDashboardAsync_CountryGraphCountsConsecutiveCaptureDateRangesAcrossPlaces()
    {
        var repository = new FixedAggregates(
        [
            new TravelPlaceAggregateRaw
            {
                PlaceId = Guid.NewGuid(),
                PlaceName = "서울",
                Country = "일본",
                PhotoCount = 3,
                Photos =
                [
                    CreateCandidate("seoul-1", new DateTime(2024, 8, 1), "일본"),
                    CreateCandidate("seoul-2", new DateTime(2024, 8, 2), "일본"),
                ],
            },
            new TravelPlaceAggregateRaw
            {
                PlaceId = Guid.NewGuid(),
                PlaceName = "부산",
                Country = "일본",
                PhotoCount = 4,
                Photos =
                [
                    CreateCandidate("busan-2", new DateTime(2024, 8, 2), "일본"),
                    CreateCandidate("busan-4", new DateTime(2024, 8, 4), "일본"),
                    CreateCandidate("busan-5", new DateTime(2024, 8, 5), "일본"),
                ],
            },
            new TravelPlaceAggregateRaw
            {
                PlaceId = Guid.NewGuid(),
                PlaceName = "날짜 미상",
                Country = "일본",
                PhotoCount = 1,
                Photos = [new TravelPhotoCandidateRaw { BackendFileId = "undated" }],
            },
        ]);

        var dashboard = await CreateService(repository).GetDashboardAsync();

        var country = Assert.Single(dashboard.CountryVisitStatistics);
        Assert.Equal("일본", country.Country);
        Assert.Equal(2, country.VisitCount);
        Assert.Equal(4, country.CapturedDayCount);
        Assert.Equal(1, dashboard.VisitedForeignCountryCount);
    }

    [Fact]
    public async Task GetDashboardAsync_CountsUniquePhotosAndDistinctRealPlaces()
    {
        var repeatedPlaceId = Guid.NewGuid();
        var mediaOnlyId = Guid.NewGuid();
        var repository = new FixedAggregates(
        [
            new TravelPlaceAggregateRaw
            {
                PlaceId = repeatedPlaceId,
                PlaceName = "여러 해에 나온 장소",
                VisitDates = [new DateTime(2020, 1, 1)],
                Photos =
                [
                    new TravelPhotoCandidateRaw
                    {
                        BackendFileId = "same-backend-file",
                        MediaId = Guid.NewGuid(),
                    },
                    new TravelPhotoCandidateRaw { MediaId = mediaOnlyId },
                ],
            },
            new TravelPlaceAggregateRaw
            {
                PlaceId = repeatedPlaceId,
                PlaceName = "여러 해에 나온 장소",
                VisitDates = [new DateTime(2024, 1, 1)],
                Photos =
                [
                    new TravelPhotoCandidateRaw
                    {
                        BackendFileId = "SAME-BACKEND-FILE",
                        MediaId = Guid.NewGuid(),
                    },
                    new TravelPhotoCandidateRaw { MediaId = mediaOnlyId },
                ],
            },
            new TravelPlaceAggregateRaw
            {
                PlaceId = LibraryConstants.UnclassifiedPlaceId,
                PlaceName = GalleryHierarchyService.UnclassifiedTitle,
                IsUnclassified = true,
                Photos = [new TravelPhotoCandidateRaw { BackendFileId = "unclassified-photo" }],
            },
            new TravelPlaceAggregateRaw
            {
                PlaceId = Guid.Empty,
                Photos = [new TravelPhotoCandidateRaw { BackendFileId = "missing-place-photo" }],
            },
        ]);

        var dashboard = await CreateService(repository).GetDashboardAsync();

        Assert.Equal(4, dashboard.UniquePhotoCount);
        Assert.Equal(1, dashboard.DistinctPlaceCount);
    }

    [Fact]
    public async Task GetDashboardAsync_CountryGraphUsesPhotoCountryAndExcludesUnknownCountries()
    {
        var repository = new FixedAggregates(
        [
            new TravelPlaceAggregateRaw
            {
                PlaceId = LibraryConstants.UnclassifiedPlaceId,
                PlaceName = GalleryHierarchyService.UnclassifiedTitle,
                Country = "일본",
                IsUnclassified = true,
                Photos =
                [
                    CreateCandidate("kr-1", new DateTime(2024, 8, 1), "대한민국"),
                    CreateCandidate("kr-2", new DateTime(2024, 8, 1), "한국"),
                    CreateCandidate("kr-3", new DateTime(2024, 8, 1), "South Korea"),
                    CreateCandidate("kr-4", new DateTime(2024, 8, 1), "Republic of Korea"),
                    CreateCandidate("jp-1", new DateTime(2024, 8, 1), "일본"),
                    CreateCandidate("jp-2", new DateTime(2024, 8, 2), "일본"),
                    CreateCandidate("other", new DateTime(2024, 8, 3), GalleryHierarchyService.OtherTitle),
                    CreateCandidate("blank", new DateTime(2024, 8, 4), string.Empty),
                    new TravelPhotoCandidateRaw
                    {
                        BackendFileId = "null-country",
                        Country = null!,
                        CapturedAt = new DateTimeOffset(2024, 8, 5, 10, 0, 0, TimeSpan.Zero),
                    },
                ],
            },
            new TravelPlaceAggregateRaw
            {
                PlaceId = Guid.NewGuid(),
                Country = GalleryHierarchyService.OtherTitle,
                Photos =
                [
                    CreateCandidate("jp-4", new DateTime(2024, 8, 4), "일본"),
                    CreateCandidate("unclassified", new DateTime(2024, 8, 7), GalleryHierarchyService.UnclassifiedTitle),
                ],
            },
        ]);

        var dashboard = await CreateService(repository).GetDashboardAsync();

        var japan = Assert.Single(dashboard.CountryVisitStatistics, item => item.Country == "일본");
        Assert.Equal(2, japan.VisitCount);
        Assert.Equal(3, japan.CapturedDayCount);
        Assert.Single(dashboard.CountryVisitStatistics);
        Assert.Equal(dashboard.CountryVisitStatistics.Count, dashboard.VisitedForeignCountryCount);
        Assert.DoesNotContain(dashboard.CountryVisitStatistics, item =>
            item.Country is "대한민국" or GalleryHierarchyService.OtherTitle or GalleryHierarchyService.UnclassifiedTitle);
    }

    [Fact]
    public async Task GetDashboardAsync_DomesticCountriesOnly_HasNoForeignCountries()
    {
        var dashboard = await CreateService(CreateCountryRepository(
            "대한민국",
            "한국",
            "South Korea",
            "Republic of Korea")).GetDashboardAsync();

        Assert.Equal(0, dashboard.VisitedForeignCountryCount);
        Assert.Empty(dashboard.CountryVisitStatistics);
    }

    [Fact]
    public async Task GetDashboardAsync_DomesticAndJapan_HasOneForeignCountry()
    {
        var dashboard = await CreateService(CreateCountryRepository("대한민국", "일본"))
            .GetDashboardAsync();

        var country = Assert.Single(dashboard.CountryVisitStatistics);
        Assert.Equal("일본", country.Country);
        Assert.Equal(1, dashboard.VisitedForeignCountryCount);
    }

    [Fact]
    public async Task GetDashboardAsync_ThreeForeignCountries_SharesGraphCountryCount()
    {
        var dashboard = await CreateService(CreateCountryRepository(
            "대한민국",
            "일본",
            "몰디브",
            "카타르")).GetDashboardAsync();

        Assert.Equal(3, dashboard.VisitedForeignCountryCount);
        Assert.Equal(dashboard.CountryVisitStatistics.Count, dashboard.VisitedForeignCountryCount);
        Assert.Equal(
            ["몰디브", "일본", "카타르"],
            dashboard.CountryVisitStatistics
                .Select(item => item.Country)
                .OrderBy(country => country, StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public async Task GetDashboardAsync_MemoryCardsAreDeterministicAndDoNotRepeatPhotosOrYears()
    {
        var today = DateTime.Today;
        var exactYear = Enumerable.Range(2, 12)
            .Select(offset => today.Year - offset)
            .First(year => DateTime.DaysInMonth(year, today.Month) >= today.Day);
        var aroundYear = today.Year - 4 == exactYear ? today.Year - 5 : today.Year - 4;
        var oldYear = today.Year - 12;
        var nearbyDay = today.Day == 1 ? 2 : today.Day - 1;
        var oldMonth = today.Month == 1 ? 7 : 1;
        var repository = new FixedAggregates(
        [
            new TravelPlaceAggregateRaw
            {
                PlaceId = Guid.NewGuid(),
                PlaceName = "추억 장소",
                Country = "대한민국",
                PhotoCount = 8,
                Photos =
                [
                    CreateCandidate("exact-1", new DateTime(exactYear, today.Month, today.Day)),
                    CreateCandidate("exact-2", new DateTime(exactYear, today.Month, today.Day)),
                    CreateCandidate("last-year", new DateTime(today.Year - 1, today.Month, nearbyDay)),
                    CreateCandidate("around", new DateTime(aroundYear, today.Month, nearbyDay)),
                    CreateCandidate("old", new DateTime(oldYear, oldMonth, 1)),
                ],
            },
        ]);

        var dashboard = await CreateService(repository).GetDashboardAsync();

        Assert.Equal(4, dashboard.MemoryCards.Count);
        Assert.Contains(dashboard.MemoryCards, card =>
            card.Kind == TravelMemoryCardKind.YearsAgoToday
            && card.Title == $"{today.Year - exactYear}년 전 오늘");
        Assert.Contains(dashboard.MemoryCards, card => card.Kind == TravelMemoryCardKind.LastYearAroundNow);
        Assert.Contains(dashboard.MemoryCards, card => card.Kind == TravelMemoryCardKind.YearsAgoAroundNow);
        Assert.Contains(dashboard.MemoryCards, card => card.Kind == TravelMemoryCardKind.Rediscovered);

        var photos = dashboard.MemoryCards.SelectMany(card => card.Photos).ToList();
        Assert.Equal(photos.Count, photos.Select(photo => photo.MediaId).Distinct().Count());
        Assert.All(dashboard.MemoryCards, card => Assert.InRange(card.Photos.Count, 1, 4));
    }

    [Fact]
    public async Task GetDashboardAsync_DomesticTrips_ExcludesDailyRadiusAndMergesDistantPlaceDates()
    {
        var home = (Latitude: 37.5665d, Longitude: 126.9780d);
        var dashboard = await CreateService(
            new FixedAggregates(
            [
                CreateDomesticAggregate("집 근처", "대한민국", home.Latitude, home.Longitude,
                    new DateTime(2024, 8, 1), new DateTime(2024, 8, 2), new DateTime(2024, 8, 3)),
                CreateDomesticAggregate("강릉", "한국", 37.7519d, 128.8761d,
                    new DateTime(2024, 8, 10), new DateTime(2024, 8, 11)),
                CreateDomesticAggregate("속초", "KR", 38.2070d, 128.5918d,
                    new DateTime(2024, 8, 11), new DateTime(2024, 8, 12)),
                CreateDomesticAggregate("도쿄", "일본", 35.6762d, 139.6503d,
                    new DateTime(2024, 8, 10)),
            ]),
            home.Latitude,
            home.Longitude).GetDashboardAsync();

        Assert.Equal(1, dashboard.DomesticTripCount);
    }

    [Fact]
    public async Task GetDashboardAsync_DomesticTrips_UsesStrictDistanceAndSplitsDateGaps()
    {
        const double homeLatitude = 37.5665d;
        const double homeLongitude = 126.9780d;
        var dashboard = await CreateService(
            new FixedAggregates(
            [
                // 0km is within the <= 2km daily-life radius and must not count.
                CreateDomesticAggregate("경계 안", "대한민국", homeLatitude, homeLongitude,
                    new DateTime(2024, 8, 1)),
                // Plainly farther than 2km avoids floating-point boundary ambiguity.
                CreateDomesticAggregate("원거리 A", "South Korea", 37.6165d, homeLongitude,
                    new DateTime(2024, 8, 1), new DateTime(2024, 8, 2)),
                CreateDomesticAggregate("원거리 B", "Republic of Korea", 37.6665d, homeLongitude,
                    new DateTime(2024, 8, 4)),
            ]),
            homeLatitude,
            homeLongitude).GetDashboardAsync();

        Assert.Equal(2, dashboard.DomesticTripCount);
    }

    [Fact]
    public async Task GetDashboardAsync_DomesticTrips_ExcludeInvalidCandidatesAndRequireHomeCoordinates()
    {
        var aggregates = new FixedAggregates(
        [
            CreateDomesticAggregate("좌표 없음", "대한민국", 0d, 0d,
                new DateTime(2024, 8, 1)),
            CreateDomesticAggregate("기타", GalleryHierarchyService.OtherTitle, 37.8d, 128.0d,
                new DateTime(2024, 8, 2)),
            CreateDomesticAggregate("미분류", GalleryHierarchyService.UnclassifiedTitle, 37.8d, 128.0d,
                new DateTime(2024, 8, 3)),
            CreateDomesticAggregate("빈 국가", null, 37.8d, 128.0d,
                new DateTime(2024, 8, 4)),
            CreateDomesticAggregate("유효 여행", "Korea", 37.8d, 128.0d,
                new DateTime(2024, 8, 5)),
        ]);

        var withHome = await CreateService(aggregates, 37.5665d, 126.9780d).GetDashboardAsync();
        var withoutHome = await CreateService(aggregates).GetDashboardAsync();

        Assert.Equal(1, withHome.DomesticTripCount);
        Assert.Equal(0, withoutHome.DomesticTripCount);
    }

    [Fact]
    public async Task GetDashboardAsync_DomesticTrips_CountsSeparatedReturnsToTheSameDistantPlace()
    {
        var dashboard = await CreateService(
            new FixedAggregates(
            [
                CreateDomesticAggregate("부산", "대한민국", 35.1796d, 129.0756d,
                    new DateTime(2024, 8, 1), new DateTime(2024, 8, 5)),
            ]),
            37.5665d,
            126.9780d).GetDashboardAsync();

        Assert.Equal(2, dashboard.DomesticTripCount);
    }

    [Fact]
    public async Task GetDashboardAsync_SeparatesDomesticAndForeignSummaryStatisticsWithUniquePhotos()
    {
        var japanPlaceId = Guid.NewGuid();
        var domesticPlaceId = Guid.NewGuid();
        var fallbackMediaId = Guid.NewGuid();
        var japanRepresentativeMediaId = Guid.NewGuid();
        var duplicateForeignPhoto = CreateStatsCandidate("japan-1", Guid.NewGuid(), "일본", new DateTime(2024, 8, 1));
        var repository = new FixedAggregates(
        [
            new TravelPlaceAggregateRaw
            {
                PlaceId = japanPlaceId,
                PlaceName = "도쿄",
                Country = "Japan",
                Photos =
                [
                    duplicateForeignPhoto,
                    CreateStatsCandidate("japan-2", Guid.NewGuid(), "일본", new DateTime(2024, 8, 3)),
                    CreateStatsCandidate(
                        "japan-3",
                        japanRepresentativeMediaId,
                        "일본",
                        new DateTime(2024, 8, 5),
                        "https://backend.example/thumbnails/japan-3"),
                ],
            },
            new TravelPlaceAggregateRaw
            {
                PlaceId = japanPlaceId,
                PlaceName = "도쿄 중복 집계",
                Country = "일본",
                Photos = [duplicateForeignPhoto],
            },
            new TravelPlaceAggregateRaw
            {
                PlaceId = Guid.NewGuid(),
                PlaceName = "몰디브",
                Country = "몰디브",
                Photos = [CreateStatsCandidate("maldives-1", Guid.NewGuid(), "몰디브", new DateTime(2024, 8, 7))],
            },
            new TravelPlaceAggregateRaw
            {
                PlaceId = Guid.NewGuid(),
                PlaceName = "도하",
                Country = "카타르",
                Photos = [CreateStatsCandidate("qatar-1", Guid.NewGuid(), "카타르", new DateTime(2024, 8, 8))],
            },
            new TravelPlaceAggregateRaw
            {
                PlaceId = domesticPlaceId,
                PlaceName = "부산",
                Country = "Korea",
                Photos =
                [
                    CreateStatsCandidate("domestic-1", Guid.NewGuid(), "대한민국", new DateTime(2024, 8, 9)),
                    CreateStatsCandidate(null, fallbackMediaId, "KR", new DateTime(2024, 8, 10)),
                ],
            },
            new TravelPlaceAggregateRaw
            {
                PlaceId = domesticPlaceId,
                PlaceName = "부산 중복 집계",
                Country = "South Korea",
                Photos = [CreateStatsCandidate("domestic-1", Guid.NewGuid(), "Republic of Korea", new DateTime(2024, 8, 9))],
            },
            new TravelPlaceAggregateRaw
            {
                PlaceId = LibraryConstants.UnclassifiedPlaceId,
                PlaceName = GalleryHierarchyService.UnclassifiedTitle,
                Country = GalleryHierarchyService.OtherTitle,
                IsUnclassified = true,
                Photos =
                [
                    CreateStatsCandidate("other", Guid.NewGuid(), GalleryHierarchyService.OtherTitle, new DateTime(2024, 8, 11)),
                    CreateStatsCandidate("unknown", Guid.NewGuid(), string.Empty, new DateTime(2024, 8, 12)),
                    CreateStatsCandidate("null", Guid.NewGuid(), null, new DateTime(2024, 8, 13)),
                ],
            },
        ]);

        var dashboard = await CreateService(repository, 37.5665d, 126.9780d).GetDashboardAsync();

        Assert.Equal(5, dashboard.ForeignTripCount);
        Assert.Equal(3, dashboard.VisitedForeignCountryCount);
        Assert.Equal(3, dashboard.ForeignPlaceCount);
        Assert.Equal(5, dashboard.ForeignPhotoCount);
        Assert.Equal(1, dashboard.DomesticPlaceCount);
        Assert.Equal(2, dashboard.DomesticPhotoCount);
        Assert.Equal(3, dashboard.ForeignCountries.Count);
        var japan = Assert.Single(dashboard.ForeignCountries, item => item.Country == "일본");
        Assert.Equal(3, japan.VisitCount);
        Assert.Equal(3, japan.PhotoCount);
        Assert.Equal(japanRepresentativeMediaId, japan.RepresentativeMediaId);
        Assert.Equal("https://backend.example/thumbnails/japan-3", japan.ThumbnailPath);
        Assert.All(dashboard.ForeignCountries, item => Assert.NotEqual("대한민국", item.Country));
    }

    private static TravelRecordsService CreateService(ITravelRecordsRepository repository)
        => CreateService(repository, null, null);

    private static TravelRecordsService CreateService(
        ITravelRecordsRepository repository,
        double? homeLatitude,
        double? homeLongitude)
    {
        var settings = new Dictionary<string, string>();
        if (homeLatitude is not null && homeLongitude is not null)
        {
            settings[SettingKeys.TravelHomeLatitude] = homeLatitude.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            settings[SettingKeys.TravelHomeLongitude] = homeLongitude.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var homeLocation = new HomeLocationService(
            new HomeSettingRepository(settings),
            new NoOpLocationResolver(),
            NullLogger<HomeLocationService>.Instance);
        return new TravelRecordsService(
            repository,
            new InMemoryMediaTagRepository(),
            new InMemoryTagRepository(),
            homeLocation,
            NullLogger<TravelRecordsService>.Instance);
    }

    private static TravelPhotoCandidateRaw CreateCandidate(
        string id,
        DateTime capturedAt,
        string country = "대한민국") => new()
    {
        MediaId = Guid.NewGuid(),
        BackendFileId = id,
        ThumbnailPath = $"https://backend.example/thumbnails/{id}",
        Country = country,
        CapturedAt = new DateTimeOffset(DateTime.SpecifyKind(capturedAt, DateTimeKind.Local)),
    };

    private static TravelPlaceAggregateRaw CreateDomesticAggregate(
        string placeName,
        string? country,
        double latitude,
        double longitude,
        params DateTime[] visitDates) => new()
    {
        PlaceId = Guid.NewGuid(),
        PlaceName = placeName,
        Country = country ?? string.Empty,
        Latitude = latitude,
        Longitude = longitude,
        PhotoCount = visitDates.Length,
        VisitDates = visitDates,
    };

    private static TravelPhotoCandidateRaw CreateStatsCandidate(
        string? backendFileId,
        Guid? mediaId,
        string? country,
        DateTime capturedAt,
        string? thumbnailPath = null) => new()
    {
        BackendFileId = backendFileId ?? string.Empty,
        MediaId = mediaId,
        Country = country!,
        CapturedAt = new DateTimeOffset(DateTime.SpecifyKind(capturedAt, DateTimeKind.Local)),
        ThumbnailPath = thumbnailPath ?? string.Empty,
    };

    private static FixedAggregates CreateCountryRepository(params string?[] countries) =>
        new(countries.Select((country, index) => new TravelPlaceAggregateRaw
        {
            PlaceId = Guid.NewGuid(),
            Country = country ?? string.Empty,
            Photos =
            [
                new TravelPhotoCandidateRaw
                {
                    BackendFileId = $"country-{index}",
                    Country = country!,
                    CapturedAt = new DateTimeOffset(2024, 1, index + 1, 10, 0, 0, TimeSpan.Zero),
                },
            ],
        }).ToList());

    private sealed class FixedAggregates : ITravelRecordsRepository
    {
        private readonly IReadOnlyList<TravelPlaceAggregateRaw> _items;

        public FixedAggregates(IReadOnlyList<TravelPlaceAggregateRaw> items) => _items = items;

        public Task<IReadOnlyList<TravelPlaceAggregateRaw>> GetPlaceAggregatesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_items);
    }

    private static Place CreatePlace(string name, string country, double lat, double lon) => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = name,
        Country = country,
        City = name,
        Latitude = lat,
        Longitude = lon,
        Radius = 200,
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static Media CreatePhoto(
        string fileName,
        Guid storageId,
        Place place,
        DateTimeOffset capturedAt) => new()
    {
        Id = Guid.NewGuid(),
        FileName = fileName,
        MediaType = MediaType.Photo,
        Status = MediaStatus.Imported,
        OriginalPath = $@"D:\src\{fileName}",
        RelativePath = $@"2024\{fileName}",
        ContentHash = fileName,
        CapturedAt = capturedAt.UtcDateTime,
        ImportedAt = capturedAt.UtcDateTime,
        PlaceId = place.Id,
        Place = place,
        StorageId = storageId,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private sealed class HomeSettingRepository : ISettingRepository
    {
        private readonly Dictionary<string, string> _values;

        public HomeSettingRepository(Dictionary<string, string> values)
        {
            _values = values;
        }

        public Task<Setting?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<Setting?>(null);

        public Task<Setting?> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
        {
            if (!_values.TryGetValue(key, out var value))
            {
                return Task.FromResult<Setting?>(null);
            }

            return Task.FromResult<Setting?>(new Setting
            {
                Id = Guid.NewGuid(),
                Key = key,
                Value = value,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        public Task<IReadOnlyList<Setting>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Setting>>([]);

        public Task AddAsync(Setting setting, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task UpdateAsync(Setting setting, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteAsync(Setting setting, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class NoOpLocationResolver : ILocationResolver
    {
        public Task<LocationResult?> ResolveAsync(
            double latitude,
            double longitude,
            CancellationToken cancellationToken = default)
            => Task.FromResult<LocationResult?>(null);

        public Task<LocationResult?> ResolveAddressAsync(
            string address,
            CancellationToken cancellationToken = default)
            => Task.FromResult<LocationResult?>(null);

        public Task<IReadOnlyList<PlaceSuggestionDto>> SuggestPlacesAsync(
            string input,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<PlaceSuggestionDto>>([]);

        public Task<LocationResult?> ResolvePlaceIdAsync(
            string placeId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<LocationResult?>(null);

        public Task<IReadOnlyList<NearbyPlaceCandidateDto>> SearchNearbyAsync(
            double latitude,
            double longitude,
            int maxResults = 5,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<NearbyPlaceCandidateDto>>([]);
    }
}
