using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Application.Time;

namespace MemoryKeeper.Tests.UnitTests;

public class VisitRecordQueryServiceTests
{
    [Fact]
    public void CalculateMarkerScale_IncreasesWithVisitsAndPhotos()
    {
        var small = VisitRecordQueryService.CalculateMarkerScale(1, 1);
        var large = VisitRecordQueryService.CalculateMarkerScale(10, 120);
        Assert.True(large > small);
        Assert.InRange(small, 0.6, 1.7);
        Assert.InRange(large, 0.6, 1.7);
    }

    [Fact]
    public void ScopeToYear_FiltersPhotosAndCountsToThatYear()
    {
        var placeId = Guid.NewGuid();
        var older = new VisitRecordPreviewPhotoDto
        {
            MediaId = Guid.NewGuid(),
            FileName = "2020.jpg",
            AbsoluteLibraryPath = @"D:\2020.jpg",
            CapturedAt = new DateTimeOffset(2020, 5, 1, 12, 0, 0, TimeSpan.Zero),
            CaptureYear = 2020
        };
        var newerA = new VisitRecordPreviewPhotoDto
        {
            MediaId = Guid.NewGuid(),
            FileName = "2024a.jpg",
            AbsoluteLibraryPath = @"D:\2024a.jpg",
            IsFavorite = true,
            CapturedAt = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero),
            CaptureYear = 2024
        };
        var newerB = new VisitRecordPreviewPhotoDto
        {
            MediaId = Guid.NewGuid(),
            FileName = "2024b.jpg",
            AbsoluteLibraryPath = @"D:\2024b.jpg",
            CapturedAt = new DateTimeOffset(2024, 7, 1, 12, 0, 0, TimeSpan.Zero),
            CaptureYear = 2024
        };

        var place = new VisitRecordPlaceDto
        {
            PlaceId = placeId,
            PlaceName = "오사카",
            PhotoCount = 3,
            VisitRecordCount = 3,
            FavoriteCount = 1,
            CaptureYears = [2024, 2020],
            AllPhotos = [newerA, newerB, older],
            PreviewPhotos = [newerA, newerB, older]
        };

        var scoped = VisitRecordQueryService.ScopeToYear(place, 2020);

        Assert.Equal(1, scoped.PhotoCount);
        Assert.Equal(1, scoped.VisitRecordCount);
        Assert.Equal(0, scoped.FavoriteCount);
        Assert.Equal([2020], scoped.CaptureYears);
        Assert.Single(scoped.AllPhotos);
        Assert.Equal("2020.jpg", scoped.AllPhotos[0].FileName);
        Assert.Single(scoped.PreviewPhotos);
    }

    [Fact]
    public void MediaDate_ResolveYear_UsesLocalCalendarYear()
    {
        // 2023-12-31 16:00 UTC == 2024-01-01 01:00 KST (UTC+9)
        var utc = new DateTime(2023, 12, 31, 16, 0, 0, DateTimeKind.Utc);
        var year = MediaDate.ResolveYear(utc, utc);
        Assert.Equal(DateTimeHelper.ToLocal(utc).Year, year);
    }

    [Fact]
    public void ScopeToYear_UnclassifiedYearsRemainDistinct()
    {
        var photos = new[]
        {
            new VisitRecordPreviewPhotoDto
            {
                MediaId = Guid.NewGuid(),
                FileName = "2009.jpg",
                AbsoluteLibraryPath = @"D:\2009.jpg",
                CapturedAt = new DateTimeOffset(2009, 2, 20, 11, 0, 0, TimeSpan.Zero),
                CaptureYear = 2009
            },
            new VisitRecordPreviewPhotoDto
            {
                MediaId = Guid.NewGuid(),
                FileName = "2012.jpg",
                AbsoluteLibraryPath = @"D:\2012.jpg",
                CapturedAt = new DateTimeOffset(2012, 5, 5, 17, 0, 0, TimeSpan.Zero),
                CaptureYear = 2012
            }
        };

        var place = new VisitRecordPlaceDto
        {
            PlaceId = VisitRecordQueryService.UnclassifiedPlaceId,
            PlaceName = "미분류",
            IsUnclassified = true,
            PhotoCount = 2,
            CaptureYears = [2012, 2009],
            AllPhotos = photos,
            PreviewPhotos = photos
        };

        var y2009 = VisitRecordQueryService.ScopeToYear(place, 2009);
        var y2012 = VisitRecordQueryService.ScopeToYear(place, 2012);

        Assert.True(y2009.IsUnclassified);
        Assert.Equal(1, y2009.PhotoCount);
        Assert.Equal("2009.jpg", y2009.AllPhotos[0].FileName);
        Assert.Equal(1, y2012.PhotoCount);
        Assert.Equal("2012.jpg", y2012.AllPhotos[0].FileName);
    }
}
