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

    private static GalleryHierarchyService CreateService(params PhotoDto[] photos) =>
        new(
            new FixedCatalog(new GalleryPhotoCatalogSnapshot { Photos = photos }),
            NullLogger<GalleryHierarchyService>.Instance);

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
