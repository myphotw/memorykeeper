using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.DTOs.Gallery;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class GalleryHierarchyServiceTests
{
    [Fact]
    public async Task DomesticPhoto_Builds_YearCountryCityPlace()
    {
        var service = CreateService(Photo(
            "20260815_140628.jpg",
            2026,
            "대한민국",
            "구례군",
            "원기교"));

        var summary = await service.GetSidebarSummaryAsync();
        var countries = await service.GetCountriesAsync(2026);
        var cities = await service.GetCitiesAsync(2026, "대한민국");
        var places = await service.GetPlacesAsync(2026, "대한민국", "구례군");

        Assert.Contains(summary.Years, item => item.Year == 2026 && item.Count == 1);
        Assert.Contains(countries, item => item.Title == "대한민국" && item.Count == 1);
        Assert.Contains(cities, item => item.Title == "구례군" && item.Count == 1);
        Assert.Contains(places, item => item.Title == "원기교" && item.Count == 1);
    }

    [Fact]
    public async Task OverseasPhoto_PreservesCountryLevelAndNormalization()
    {
        var service = CreateService(Photo(
            "usj.jpg",
            2025,
            "Japan",
            "Osaka-shi",
            "Universal Studios Japan"));

        var countries = await service.GetCountriesAsync(2025);
        var cities = await service.GetCitiesAsync(2025, "일본");
        var places = await service.GetPlacesAsync(2025, "일본", "오사카");

        Assert.Contains(countries, item => item.Title == "일본");
        Assert.Contains(cities, item => item.Title == "오사카");
        Assert.Single(places);
    }

    [Fact]
    public async Task MultipleCountries_AreSeparatedUnderSameYear()
    {
        var service = CreateService(
            Photo("korea.jpg", 2025, "South Korea", "서울", "경복궁"),
            Photo("japan.jpg", 2025, "日本", "大阪市", "도톤보리"));

        var countries = await service.GetCountriesAsync(2025);

        Assert.Equal(2, countries.Count);
        Assert.Contains(countries, item => item.Title == "대한민국" && item.Count == 1);
        Assert.Contains(countries, item => item.Title == "일본" && item.Count == 1);
    }

    [Fact]
    public async Task PlaceBrowse_GroupsSameStablePlaceAcrossYears()
    {
        var service = CreateService(
            Photo("bridge-2026.jpg", 2026, "대한민국", "구례군", "원기교"),
            Photo("bridge-2025.jpg", 2025, "대한민국", "구례군", "원기교"));

        var root = Assert.Single(await service.GetPlaceBrowseRootsAsync());
        var years = await service.GetYearsForPlaceAsync(root.PlaceId!.Value);

        Assert.Equal("원기교", root.Title);
        Assert.Equal(2, root.Count);
        Assert.Collection(
            years,
            item => Assert.Equal(2026, item.Year),
            item => Assert.Equal(2025, item.Year));
    }

    [Fact]
    public async Task Query_KeepsYearCountryCityAndPlaceConditions()
    {
        var service = CreateService(
            Photo("target.jpg", 2026, "대한민국", "구례군", "원기교"),
            Photo("wrong-year.jpg", 2025, "대한민국", "구례군", "원기교"),
            Photo("wrong-country.jpg", 2026, "일본", "구례군", "원기교"),
            Photo("wrong-city.jpg", 2026, "대한민국", "순천시", "원기교"),
            Photo("wrong-place.jpg", 2026, "대한민국", "구례군", "화엄사"));
        var place = Assert.Single(await service.GetPlacesAsync(2026, "대한민국", "구례군"),
            item => item.Title == "원기교");

        var cityResult = await service.QueryAsync(new GalleryHierarchyQuery
        {
            Year = 2026,
            Country = "대한민국",
            City = "구례군",
        });
        var placeResult = await service.QueryAsync(new GalleryHierarchyQuery
        {
            Year = 2026,
            Country = "대한민국",
            City = "구례군",
            PlaceId = place.PlaceId,
        });

        Assert.Equal(2, cityResult.Count);
        Assert.Single(placeResult);
        Assert.Equal("target.jpg", placeResult[0].Filename);
    }

    [Fact]
    public async Task PlaceBrowseAndPlaceYear_FilterExactly()
    {
        var service = CreateService(
            Photo("bridge-2026.jpg", 2026, "대한민국", "구례군", "원기교"),
            Photo("bridge-2025.jpg", 2025, "대한민국", "구례군", "원기교"),
            Photo("temple.jpg", 2026, "대한민국", "구례군", "화엄사"));
        var place = Assert.Single(await service.GetPlaceBrowseRootsAsync("원기교"));

        var allYears = await service.QueryAsync(new GalleryHierarchyQuery { PlaceId = place.PlaceId });
        var oneYear = await service.QueryAsync(new GalleryHierarchyQuery
        {
            PlaceId = place.PlaceId,
            Year = 2026,
        });

        Assert.Equal(2, allYears.Count);
        Assert.Single(oneYear);
        Assert.Equal("bridge-2026.jpg", oneYear[0].Filename);
    }

    [Fact]
    public async Task Province_IsCityFallback_AndDistrictDoesNotAddDepth()
    {
        var photo = Photo(
            "fallback.jpg",
            2025,
            "日本",
            null,
            "유니버설 스튜디오 재팬",
            province: "大阪府",
            district: "此花区");
        var service = CreateService(photo);

        var cities = await service.GetCitiesAsync(2025, "일본");

        var city = Assert.Single(cities);
        Assert.Equal("오사카", city.Title);
    }

    [Fact]
    public async Task FullAddressPlaceName_IsPreservedWithoutHeuristicParsing()
    {
        const string fullPlaceName = "대한민국 전라남도 구례군 토지면 내서리 96 원기교";
        var photo = Photo(
            "20260815_140628.jpg",
            2026,
            "대한민국",
            "구례군",
            fullPlaceName);
        var snapshot = new GalleryPhotoCatalogSnapshot
        {
            Photos = [photo],
            LocationMetadataByFileId = new Dictionary<string, GalleryPhotoLocationMetadataDto>
            {
                [photo.FileId] = new()
                {
                    Latitude = 35.2274226997,
                    Longitude = 127.5905235997,
                    Country = "대한민국",
                    Province = "전라남도",
                    City = "구례군",
                    District = "내서리",
                    PlaceName = fullPlaceName,
                },
            },
        };
        var service = new GalleryHierarchyService(
            new FixedCatalog(snapshot),
            NullLogger<GalleryHierarchyService>.Instance);

        var place = Assert.Single(await service.GetPlacesAsync(2026, "대한민국", "구례군"));

        Assert.Equal(fullPlaceName, place.Title);
    }

    [Fact]
    public async Task RegisteredPlace_UsesBackendUuidAndDisplayNameBeforeRawGeocodedAddress()
    {
        var backendPlaceId = Guid.NewGuid();
        var photo = new PhotoDto
        {
            FileId = Guid.NewGuid().ToString(),
            Filename = "piagol.jpg",
            CaptureDatetime = new DateTimeOffset(2026, 8, 15, 14, 6, 28, TimeSpan.FromHours(9)),
            Country = "대한민국",
            City = "구례군",
            MemorykeeperPlaceId = backendPlaceId,
            PlaceDisplayName = "피아골",
            PlaceCanonicalName = "지리산 피아골",
            PlaceName = "대한민국 전라남도 구례군 토지면 내서리 96 원기교",
            GeocodedPlaceName = "대한민국 전라남도 구례군 토지면 내서리 96 원기교",
        };
        var service = CreateService(photo);

        var node = Assert.Single(await service.GetPlacesAsync(2026, "대한민국", "구례군"));
        var visit = Assert.Single((await service.QueryVisitRecordsAsync(new GalleryHierarchyQuery())).AllMapPlaces);

        Assert.Equal(backendPlaceId, node.PlaceId);
        Assert.Equal("피아골", node.Title);
        Assert.Equal(backendPlaceId, visit.PlaceId);
        Assert.Equal("피아골", visit.PlaceName);
    }

    [Fact]
    public async Task RegisteredOakwood_PreservesRawCountryAndProvinceWhenCityAndPlaceGeographyAreMissing()
    {
        var placeId = Guid.NewGuid();
        var photo = new PhotoDto
        {
            FileId = Guid.NewGuid().ToString(),
            Filename = "oakwood.jpg",
            CaptureDatetime = new DateTimeOffset(2017, 5, 1, 12, 0, 0, TimeSpan.FromHours(9)),
            Country = "대한민국",
            Province = "서울특별시",
            MemorykeeperPlaceId = placeId,
            PlaceDisplayName = "Oakwood Premier Coex Center Seoul",
        };
        var service = CreateService(photo);

        var country = Assert.Single(await service.GetCountriesAsync(2017));
        var city = Assert.Single(await service.GetCitiesAsync(2017, "대한민국"));
        var place = Assert.Single(await service.GetPlacesAsync(2017, "대한민국", "서울특별시"));

        Assert.Equal("대한민국", country.Title);
        Assert.Equal("서울특별시", city.Title);
        Assert.Equal("Oakwood Premier Coex Center Seoul", place.Title);
    }

    [Fact]
    public async Task RegisteredPlaceGeography_FillsOnlyMissingRawCountryAndRegion()
    {
        var placeId = Guid.NewGuid();
        var photo = new PhotoDto
        {
            FileId = Guid.NewGuid().ToString(),
            Filename = "registered-fallback.jpg",
            CaptureDatetime = new DateTimeOffset(2018, 5, 1, 12, 0, 0, TimeSpan.FromHours(9)),
            MemorykeeperPlaceId = placeId,
            PlaceDisplayName = "용유지(용비저수지)",
        };
        var snapshot = new GalleryPhotoCatalogSnapshot
        {
            Photos = [photo],
            RegisteredPlacesById = new Dictionary<Guid, GalleryRegisteredPlaceGeographyDto>
            {
                [placeId] = new()
                {
                    Country = "대한민국",
                    Province = "경기도",
                    City = "용인시",
                },
            },
        };
        var service = new GalleryHierarchyService(
            new FixedCatalog(snapshot),
            NullLogger<GalleryHierarchyService>.Instance);

        Assert.Equal("대한민국", Assert.Single(await service.GetCountriesAsync(2018)).Title);
        Assert.Equal("용인시", Assert.Single(await service.GetCitiesAsync(2018, "대한민국")).Title);
        Assert.Equal(
            "용유지(용비저수지)",
            Assert.Single(await service.GetPlacesAsync(2018, "대한민국", "용인시")).Title);
    }

    [Fact]
    public async Task RegisteredPlaceWithoutRawOrRegisteredGeography_UsesOtherAtBothLevels()
    {
        var photo = new PhotoDto
        {
            FileId = Guid.NewGuid().ToString(),
            Filename = "unknown-geography.jpg",
            CaptureDatetime = new DateTimeOffset(2018, 5, 1, 12, 0, 0, TimeSpan.FromHours(9)),
            MemorykeeperPlaceId = Guid.NewGuid(),
            PlaceDisplayName = "장소 이름",
        };
        var service = CreateService(photo);

        Assert.Equal("기타", Assert.Single(await service.GetCountriesAsync(2018)).Title);
        Assert.Equal("기타", Assert.Single(await service.GetCitiesAsync(2018, "기타")).Title);
    }

    [Fact]
    public async Task MissingBackendPlaceId_FallsBackToStableRawPlaceIdentity()
    {
        var photo = Photo("raw.jpg", 2026, "대한민국", "구례군", "원기교");
        var service = CreateService(photo);

        var node = Assert.Single(await service.GetPlacesAsync(2026, "대한민국", "구례군"));

        Assert.Equal(PlaceIdentity.StableId("대한민국", "구례군", "원기교"), node.PlaceId);
        Assert.Equal("원기교", node.Title);
    }

    [Fact]
    public async Task MissingPlace_IsUnclassified_WhileMissingCountryUsesOther()
    {
        var service = CreateService(
            Photo("unclassified.jpg", 2026, "대한민국", "구례군", null),
            Photo("other-country.jpg", 2026, null, "구례군", "원기교"));

        var countries = await service.GetCountriesAsync(2026);

        Assert.Contains(countries, item => item.IsUnclassified && item.Count == 1);
        Assert.Contains(countries, item => item.Title == "기타" && item.Count == 1);
    }

    [Fact]
    public async Task Pending_UsesMissingRegisteredPlaceId_NotUploadStatusOrRawAddress()
    {
        var registeredPlaceId = Guid.NewGuid();
        var rawOnly = Photo("raw-only.jpg", 2026, "대한민국", "구례군", "원기교");
        var noLocation = Photo("no-location.jpg", 2026, "대한민국", "구례군", null);
        var registered = new PhotoDto
        {
            FileId = Guid.NewGuid().ToString(),
            Filename = "registered.jpg",
            CaptureDatetime = rawOnly.CaptureDatetime,
            Country = "대한민국",
            City = "구례군",
            PlaceName = "전체 주소",
            PlaceDisplayName = "피아골",
            MemorykeeperPlaceId = registeredPlaceId,
            Status = "pending",
        };
        var service = CreateService(rawOnly, noLocation, registered);

        var summary = await service.GetSidebarSummaryAsync();
        var pending = await service.QueryAsync(new GalleryHierarchyQuery { PendingOnly = true });

        Assert.Equal(2, summary.PendingCount);
        Assert.Equal(2, pending.Count);
        Assert.Contains(pending, photo => photo.Filename == "raw-only.jpg");
        Assert.Contains(pending, photo => photo.Filename == "no-location.jpg");
        Assert.DoesNotContain(pending, photo => photo.Filename == "registered.jpg");
    }

    [Fact]
    public async Task Recent_UsesBackendCreatedAtOrderingIdentityAndTopCount()
    {
        var olderCapture = Photo("newly-imported.jpg", 2010, "대한민국", "서울", "한강");
        var newerCapture = Photo("older-import.jpg", 2026, "대한민국", "서울", "남산");
        var snapshot = new GalleryPhotoCatalogSnapshot
        {
            Photos = [newerCapture, olderCapture],
            RecentPhotoFileIds = [olderCapture.FileId, newerCapture.FileId],
        };
        var service = new GalleryHierarchyService(
            new FixedCatalog(snapshot),
            NullLogger<GalleryHierarchyService>.Instance);

        var summary = await service.GetSidebarSummaryAsync();
        var recent = await service.QueryAsync(new GalleryHierarchyQuery { RecentOnly = true });

        Assert.Equal(2, summary.RecentCount);
        Assert.Equal(["newly-imported.jpg", "older-import.jpg"], recent.Select(photo => photo.Filename));
    }

    [Fact]
    public async Task CreatedAt_PreservesFormerImportedDateYearFallback()
    {
        var photo = new PhotoDto
        {
            FileId = Guid.NewGuid().ToString(),
            Filename = "fallback-date.jpg",
            Country = "대한민국",
            City = "구례군",
            PlaceName = "원기교",
            CreatedAt = new DateTimeOffset(2024, 2, 1, 10, 0, 0, TimeSpan.FromHours(9)),
        };
        var service = CreateService(photo);

        var summary = await service.GetSidebarSummaryAsync();
        var result = await service.QueryAsync(new GalleryHierarchyQuery { Year = 2024 });

        Assert.Contains(summary.Years, item => item.Year == 2024 && item.Count == 1);
        Assert.Single(result);
    }

    [Fact]
    public async Task MissingCaptureDate_IsKeptInAllButNotAssignedAnArbitraryYear()
    {
        var undated = new PhotoDto
        {
            FileId = Guid.NewGuid().ToString(),
            Filename = "undated.jpg",
            Country = "대한민국",
            City = "구례군",
            PlaceName = "원기교",
        };
        var service = CreateService(undated);

        var summary = await service.GetSidebarSummaryAsync();
        var all = await service.QueryAsync(new GalleryHierarchyQuery());
        var roots = await service.GetPlaceBrowseRootsAsync();

        Assert.Equal(1, summary.TotalCount);
        Assert.Empty(summary.Years);
        Assert.Single(all);
        Assert.Empty(roots);
    }

    [Fact]
    public async Task DuplicateFileId_IsCountedOnce_AndOnlyCatalogRowsAreUsed()
    {
        var fileId = Guid.NewGuid().ToString();
        var photo = Photo(
            "backend-only.jpg",
            2026,
            "대한민국",
            "구례군",
            "원기교",
            fileId: fileId);
        var service = CreateService(photo, photo);

        var summary = await service.GetSidebarSummaryAsync();
        var results = await service.QueryAsync(new GalleryHierarchyQuery());

        Assert.Equal(1, summary.TotalCount);
        Assert.Single(results);
    }

    [Fact]
    public async Task VisitMapProjection_UsesSameYearCountryCityPlaceIdentityAndCounts()
    {
        var target = Photo("target.jpg", 2026, "대한민국", "구례군", "원기교");
        var samePlaceOtherYear = Photo("older.jpg", 2025, "대한민국", "구례군", "원기교");
        var otherCity = Photo("suncheon.jpg", 2026, "대한민국", "순천시", "순천만");
        var overseas = Photo("osaka.jpg", 2026, "일본", "오사카", "도톤보리");
        var snapshot = new GalleryPhotoCatalogSnapshot
        {
            Photos = [target, samePlaceOtherYear, otherCity, overseas],
            ApiBaseUrl = "http://localhost:8000",
            LocationMetadataByFileId = new Dictionary<string, GalleryPhotoLocationMetadataDto>
            {
                [target.FileId] = Location(35.2274, 127.5905),
                [samePlaceOtherYear.FileId] = Location(35.2275, 127.5906),
                [otherCity.FileId] = Location(34.9506, 127.4872),
                [overseas.FileId] = Location(34.6687, 135.5013),
            },
        };
        var service = new GalleryHierarchyService(
            new FixedCatalog(snapshot),
            NullLogger<GalleryHierarchyService>.Instance);

        var placeNode = Assert.Single(
            await service.GetPlacesAsync(2026, "대한민국", "구례군"));
        var year = await service.QueryVisitRecordsAsync(new GalleryHierarchyQuery { Year = 2026 });
        var country = await service.QueryVisitRecordsAsync(new GalleryHierarchyQuery
        {
            Year = 2026,
            Country = "대한민국",
        });
        var city = await service.QueryVisitRecordsAsync(new GalleryHierarchyQuery
        {
            Year = 2026,
            Country = "대한민국",
            City = "구례군",
        });
        var place = await service.QueryVisitRecordsAsync(new GalleryHierarchyQuery
        {
            Year = 2026,
            PlaceId = placeNode.PlaceId,
        });
        var allYearsForPlace = await service.QueryVisitRecordsAsync(new GalleryHierarchyQuery
        {
            PlaceId = placeNode.PlaceId,
        });

        Assert.Equal(3, year.AllMapPlaces.Count);
        Assert.Equal(2, country.AllMapPlaces.Count);
        Assert.Single(city.AllMapPlaces);
        var selectedPlace = Assert.Single(place.AllMapPlaces);
        Assert.Equal(placeNode.PlaceId, selectedPlace.PlaceId);
        Assert.Equal(placeNode.Title, selectedPlace.PlaceName);
        Assert.Equal(placeNode.Count, selectedPlace.PhotoCount);
        Assert.True(PlaceIdentity.HasValidCoordinates(selectedPlace.Latitude, selectedPlace.Longitude));
        Assert.Single(selectedPlace.AllPhotos);

        var acrossYears = Assert.Single(allYearsForPlace.AllMapPlaces);
        Assert.Equal(2, acrossYears.PhotoCount);
        Assert.Equal([2026, 2025], acrossYears.CaptureYears);
    }

    [Fact]
    public async Task DeletedAndTombstoneRows_AreExcludedFromGalleryAndVisitMap()
    {
        var active = Photo("active.jpg", 2026, "대한민국", "구례군", "원기교");
        var deleted = new PhotoDto
        {
            FileId = Guid.NewGuid().ToString(),
            Filename = "deleted.jpg",
            CaptureDatetime = active.CaptureDatetime,
            Country = active.Country,
            City = active.City,
            PlaceName = active.PlaceName,
            Status = "deleted",
        };
        var tombstone = new PhotoDto
        {
            FileId = Guid.NewGuid().ToString(),
            Filename = "tombstone.jpg",
            CaptureDatetime = active.CaptureDatetime,
            Country = active.Country,
            City = active.City,
            PlaceName = active.PlaceName,
            Status = "tombstone",
        };
        var service = CreateService(active, deleted, tombstone);

        var summary = await service.GetSidebarSummaryAsync();
        var gallery = await service.QueryAsync(new GalleryHierarchyQuery());
        var visitMap = await service.QueryVisitRecordsAsync(new GalleryHierarchyQuery());

        Assert.Equal(1, summary.TotalCount);
        Assert.Single(gallery);
        Assert.Single(visitMap.AllMapPlaces);
        Assert.Equal(1, visitMap.AllMapPlaces[0].PhotoCount);
    }

    [Fact]
    public async Task VisitUnclassified_KeepsAllPhotosAndOnlyGpsCapableMarker_WithoutEmptyState()
    {
        var withGps = Photo("with-gps.jpg", 2012, "대한민국", "서울특별시", null);
        var withoutGps = Photo("without-gps.jpg", 2012, "대한민국", "서울특별시", null);
        var snapshot = new GalleryPhotoCatalogSnapshot
        {
            Photos = [withGps, withoutGps],
            LocationMetadataByFileId = new Dictionary<string, GalleryPhotoLocationMetadataDto>
            {
                [withGps.FileId] = Location(37.51, 127.05),
            },
        };
        var service = new GalleryHierarchyService(
            new FixedCatalog(snapshot),
            NullLogger<GalleryHierarchyService>.Instance);

        var result = await service.QueryVisitRecordsAsync(new GalleryHierarchyQuery
        {
            Year = 2012,
            UnclassifiedOnly = true,
        });

        var unclassified = Assert.Single(result.TimelinePlaces);
        Assert.True(unclassified.IsUnclassified);
        Assert.Equal(2, unclassified.PhotoCount);
        Assert.Equal(2, unclassified.AllPhotos.Count);
        Assert.Single(result.AllMapPlaces.Where(VisitRecordPlaceScoping.CanDisplayMarker));
        Assert.True(VisitRecordPlaceScoping.HasAnyPhotos(result.TimelinePlaces));
    }

    [Fact]
    public async Task VisitUnclassified_WithNoPhotos_UsesRealEmptyState()
    {
        var service = CreateService(new PhotoDto
        {
            FileId = Guid.NewGuid().ToString(),
            Filename = "registered.jpg",
            CaptureDatetime = new DateTimeOffset(2012, 5, 1, 12, 0, 0, TimeSpan.FromHours(9)),
            Country = "대한민국",
            City = "서울특별시",
            PlaceName = "장소",
            MemorykeeperPlaceId = Guid.NewGuid(),
        });

        var result = await service.QueryVisitRecordsAsync(new GalleryHierarchyQuery
        {
            Year = 2012,
            UnclassifiedOnly = true,
        });

        Assert.Empty(result.TimelinePlaces);
        Assert.False(VisitRecordPlaceScoping.HasAnyPhotos(result.TimelinePlaces));
    }

    private static GalleryHierarchyService CreateService(params PhotoDto[] photos) =>
        new(
            new FixedCatalog(new GalleryPhotoCatalogSnapshot { Photos = photos }),
            NullLogger<GalleryHierarchyService>.Instance);

    private static GalleryPhotoLocationMetadataDto Location(double latitude, double longitude) => new()
    {
        Latitude = latitude,
        Longitude = longitude,
    };

    private static PhotoDto Photo(
        string filename,
        int year,
        string? country,
        string? city,
        string? placeName,
        string? province = null,
        string? district = null,
        string? fileId = null) =>
        new()
        {
            FileId = fileId ?? Guid.NewGuid().ToString(),
            Filename = filename,
            CaptureDatetime = new DateTimeOffset(year, 8, 15, 14, 6, 28, TimeSpan.FromHours(9)),
            Country = country,
            Province = province,
            City = city,
            District = district,
            PlaceName = placeName,
            HasGps = true,
            ThumbnailUrl = "/thumbnail",
            PreviewUrl = "/preview",
        };

    private sealed class FixedCatalog : IGalleryPhotoCatalog
    {
        private readonly GalleryPhotoCatalogSnapshot _snapshot;

        public FixedCatalog(GalleryPhotoCatalogSnapshot snapshot) => _snapshot = snapshot;

        public Task<GalleryPhotoCatalogSnapshot> QueryAsync(
            int? year = null,
            string? country = null,
            string? keyword = null,
            CancellationToken cancellationToken = default) => Task.FromResult(_snapshot);
    }
}
