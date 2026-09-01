using System.Text.Json;
using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class FastGalleryDtoTests
{
    [Fact]
    public void PhotosPage_DeserializesOpaqueCursorAndEffectiveDate()
    {
        const string json = """{"items":[{"common_file_id":42,"file_id":"000000000000002a","filename":"a.jpg","favorite":true,"has_gps":true,"effective_capture_datetime":"2025-01-02T03:04:05+09:00","effective_capture_date":"2025-01-02","effective_capture_year":2025,"date_basis":"EXIF"}],"next_cursor":"opaque+/=","has_more":true,"sync_cursor":null}""";
        var page = JsonSerializer.Deserialize<FastGalleryPhotoPageDto>(json)!;
        Assert.True(page.HasMore);
        Assert.Equal("opaque+/=", page.NextCursor);
        Assert.Equal(42, page.Items[0].CommonFileId);
        Assert.Equal(2025, page.Items[0].EffectiveCaptureYear);
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
        const string json = """{"places":[{"memorykeeper_place_id":"00000000-0000-0000-0000-000000000001","place_display_name":"Tokyo","country":"Japan","region":"Kanto","photo_count":12,"capture_dates":["2025-01-01","2025-01-02","2025-04-10"],"visit_count":2,"representative_file_id":"000000000000002a","representative_thumbnail_url":"/thumb.jpg"}],"countries":[{"country":"Japan","photo_count":12,"capture_dates":["2025-01-01"],"visit_count":2,"representative_preview_url":"/preview.jpg"}]}""";
        var aggregates = JsonSerializer.Deserialize<FastTravelAggregatesDto>(json)!;
        Assert.Equal(2, aggregates.Places[0].VisitCount);
        Assert.Equal(12, aggregates.Countries[0].PhotoCount);
        Assert.Equal(new DateOnly(2025, 4, 10), aggregates.Places[0].CaptureDates[^1]);
        Assert.Equal("/thumb.jpg", aggregates.Places[0].RepresentativeThumbnailUrl);
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
}
