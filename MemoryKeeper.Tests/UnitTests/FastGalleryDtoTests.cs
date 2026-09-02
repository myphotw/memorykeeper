using System.Text.Json;
using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Infrastructure.Repositories.Api;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class FastGalleryDtoTests
{
    [Fact]
    public void PhotosPage_DeserializesOpaqueCursorAndEffectiveDate()
    {
        const string json = """{"items":[{"common_file_id":42,"file_id":"000000000000002a","filename":"a.jpg","thumbnail_url":"/api/common/gallery/000000000000002a/thumbnail","preview_url":"/api/common/gallery/000000000000002a/preview","favorite":true,"has_gps":true,"effective_capture_datetime":"2025-01-02T03:04:05+09:00","effective_capture_date":"2025-01-02","effective_capture_year":2025,"date_basis":"EXIF"}],"next_cursor":"opaque+/=","has_more":true,"sync_cursor":null}""";
        var page = JsonSerializer.Deserialize<FastGalleryPhotoPageDto>(json)!;
        Assert.True(page.HasMore);
        Assert.Equal("opaque+/=", page.NextCursor);
        Assert.Equal(42, page.Items[0].CommonFileId);
        Assert.Equal(2025, page.Items[0].EffectiveCaptureYear);
        Assert.Equal("000000000000002a", page.Items[0].FileId);
        Assert.Equal("/api/common/gallery/000000000000002a/thumbnail", page.Items[0].ThumbnailUrl);
        Assert.Equal("/api/common/gallery/000000000000002a/preview", page.Items[0].PreviewUrl);
    }

    [Fact]
    public void Hierarchy_AcceptsNamedNestedLevels()
    {
        const string json = """{"years":[{"year":2025,"count":1,"countries":[{"country":"Japan","count":1,"regions":[{"region":"Tokyo","count":1,"places":[{"memorykeeper_place_id":"00000000-0000-0000-0000-000000000001","display_name":"Shibuya","count":1}]}]}]}]}""";
        var hierarchy = JsonSerializer.Deserialize<FastGalleryHierarchyDto>(json)!;
        Assert.Single(hierarchy.Roots);
        Assert.Single(hierarchy.Roots[0].ChildNodes);
        Assert.Single(hierarchy.Roots[0].ChildNodes[0].ChildNodes[0].ChildNodes);
    }

    [Fact]
    public void TravelAggregates_DeserializeActualDatesCountsAndRepresentatives()
    {
        const string json = """{"places":[{"memorykeeper_place_id":"00000000-0000-0000-0000-000000000001","place_display_name":"Tokyo","country":"Japan","region":"Kanto","latitude":35.6762,"longitude":139.6503,"photo_count":12,"capture_dates":["2025-01-01","2025-01-02","2025-04-10"],"visit_count":2,"representative_file_id":"000000000000002a","representative_thumbnail_url":"/thumb.jpg"}],"countries":[{"country":"Japan","photo_count":12,"capture_dates":["2025-01-01"],"visit_count":2,"representative_preview_url":"/preview.jpg"}]}""";
        var aggregates = JsonSerializer.Deserialize<FastTravelAggregatesDto>(json)!;
        Assert.Equal(2, aggregates.Places[0].VisitCount);
        Assert.Equal(12, aggregates.Countries[0].PhotoCount);
        Assert.Equal(new DateOnly(2025, 4, 10), aggregates.Places[0].CaptureDates[^1]);
        Assert.Equal("/thumb.jpg", aggregates.Places[0].RepresentativeThumbnailUrl);
        Assert.Equal(35.6762, aggregates.Places[0].Latitude);
        Assert.Equal(139.6503, aggregates.Places[0].Longitude);
    }

    [Fact]
    public void TravelMemories_DeserializeCategoryAndNullUrls()
    {
        const string json = """{"items":[{"common_file_id":9,"file_id":"0000000000000009","effective_capture_date":"2024-09-01","memorykeeper_place_id":"00000000-0000-0000-0000-000000000001","place_display_name":"Busan","country":"대한민국","thumbnail_url":null,"preview_url":"/preview.jpg","category":"exact_anniversary"}]}""";
        var memories = JsonSerializer.Deserialize<FastTravelMemoriesDto>(json)!;
        Assert.Single(memories.Items);
        Assert.Equal("exact_anniversary", memories.Items[0].Category);
        Assert.Null(memories.Items[0].ThumbnailUrl);
    }

    [Fact]
    public void TravelMemories_DeserializeTopLevelCandidateGroups()
    {
        const string json = """{"exact_anniversary":[{"file_id":"0000000000000009","effective_capture_date":"2024-09-01"}],"previous_year_period":[{"file_id":"000000000000000a","effective_capture_date":"2025-08-28"}]}""";
        var memories = JsonSerializer.Deserialize<FastTravelMemoriesDto>(json)!;
        Assert.Single(memories.ExactAnniversary);
        Assert.Single(memories.PreviousYearPeriod);
    }

    [Fact]
    public void TravelMemories_EmptyResponseRemainsEmpty()
    {
        var memories = JsonSerializer.Deserialize<FastTravelMemoriesDto>("{}")!;

        Assert.Empty(memories.Items);
        Assert.Empty(memories.ExactAnniversary);
        Assert.Empty(memories.PreviousYearPeriod);
    }

    [Fact]
    public void MediaUrlResolver_UsesFileIdWhenThumbnailFieldIsMissing()
    {
        var resolved = BackendMediaUrlResolver.ResolveThumbnailUrl(
            "http://memorykeeper.local:8000",
            "abc/123",
            null);

        Assert.Equal(
            "http://memorykeeper.local:8000/api/common/gallery/abc%2F123/thumbnail",
            resolved);
    }

    [Fact]
    public void MediaUrlResolver_PrefersExplicitPreviewBeforeSynthesizedThumbnail()
    {
        var resolved = BackendMediaUrlResolver.ResolveDisplayUrl(
            "http://memorykeeper.local:8000",
            "sha256",
            null,
            "/api/common/gallery/sha256/preview");

        Assert.Equal(
            "http://memorykeeper.local:8000/api/common/gallery/sha256/preview",
            resolved);
    }

    [Fact]
    public void TravelMemories_MixedPayloadProjectsAllCategoriesAndDeduplicatesFileIds()
    {
        const string json = """{"items":[{"file_id":"0000000000000001","effective_capture_date":"2024-09-01","memorykeeper_place_id":"00000000-0000-0000-0000-000000000001","category":"exact_anniversary"}],"exact_anniversary":[{"file_id":"0000000000000001","effective_capture_date":"2024-09-01","memorykeeper_place_id":"00000000-0000-0000-0000-000000000001"}],"previous_year_period":[{"file_id":"0000000000000002","effective_capture_date":"2025-08-28","memorykeeper_place_id":"00000000-0000-0000-0000-000000000002"}]}""";
        var response = JsonSerializer.Deserialize<FastTravelMemoriesDto>(json)!;

        var candidates = FastGalleryTravelRecordsRepository.ProjectMemoryCandidates(
            response,
            "https://backend.example");

        Assert.Equal(2, candidates.Count);
        Assert.Contains(candidates, item => item.Category == "exact_anniversary");
        Assert.Contains(candidates, item => item.Category == "previous_year_period");
        Assert.Equal(2, candidates.Select(item => item.MediaId).Distinct().Count());
    }

    [Fact]
    public void MediaUrlResolver_RebasesProtectedAbsoluteMediaToConfiguredBackendOrigin()
    {
        var resolved = BackendMediaUrlResolver.ToAbsoluteUrl(
            "https://backend.example:8443",
            "http://192.168.0.20:8000/api/common/gallery/sha256/thumbnail?size=small");

        Assert.Equal(
            "https://backend.example:8443/api/common/gallery/sha256/thumbnail?size=small",
            resolved);
    }

    [Theory]
    [InlineData("/api/common/gallery/id/thumbnail", "https://backend.example:8443/api/common/gallery/id/thumbnail")]
    [InlineData("https://backend.example:8443/api/common/gallery/id/thumbnail", "https://backend.example:8443/api/common/gallery/id/thumbnail")]
    [InlineData("http://192.168.0.20:8000/api/common/gallery/id/thumbnail", "https://backend.example:8443/api/common/gallery/id/thumbnail")]
    [InlineData("https://cdn.example/images/photo.jpg", "https://cdn.example/images/photo.jpg")]
    public void MediaUrlResolver_NormalizesOnlyProtectedApiMedia(string source, string expected)
    {
        Assert.Equal(expected, BackendMediaUrlResolver.ToAbsoluteUrl("https://backend.example:8443", source));
    }

    [Fact]
    public void HomeDashboardProjection_UpdatesAuthoritativePlacesWithoutDiscardingShellPhotos()
    {
        var retainedPhoto = new DashboardPhotoDto { MediaId = Guid.NewGuid(), FileName = "recent.jpg" };
        var shell = new HomeDashboardDto
        {
            RecentImports = [retainedPhoto],
            Statistics = new DashboardStatisticsDto { PhotoCount = 8, PlaceCount = 0 },
        };
        var aggregate = new TravelPlaceAggregateRaw
        {
            PlaceId = Guid.NewGuid(),
            PlaceName = "부산",
            Country = "대한민국",
            PhotoCount = 3,
            VisitCount = 1,
            VisitDates = [new DateTime(2026, 1, 2)],
        };

        var updated = HomeDashboardProjection.ApplyAuthoritativePlaceAggregates(shell, [aggregate]);

        Assert.Equal(1, updated.Statistics.PlaceCount);
        Assert.Single(updated.RecentVisits);
        Assert.Single(updated.HeroMemories);
        Assert.Equal(retainedPhoto.MediaId, Assert.Single(updated.RecentImports).MediaId);
    }

    [Fact]
    public void MediaUrlResolver_KeepsExternalCdnUrlUnchanged()
    {
        var resolved = BackendMediaUrlResolver.ToAbsoluteUrl(
            "https://backend.example:8443",
            "https://cdn.example/images/photo.jpg");

        Assert.Equal("https://cdn.example/images/photo.jpg", resolved);
    }

    [Fact]
    public void FastTravelRepository_ProjectsOptionalCoordinates()
    {
        var source = File.ReadAllText(FindSourceFile(
            "MemoryKeeper.Infrastructure",
            "Repositories",
            "Api",
            "FastGalleryTravelRecordsRepository.cs"));

        Assert.Contains("Latitude = item.Latitude ?? 0d", source, StringComparison.Ordinal);
        Assert.Contains("Longitude = item.Longitude ?? 0d", source, StringComparison.Ordinal);
    }

    private static string FindSourceFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Source file was not found: {Path.Combine(parts)}");
    }
}
