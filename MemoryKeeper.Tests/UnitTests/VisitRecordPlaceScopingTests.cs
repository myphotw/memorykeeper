using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Time;

namespace MemoryKeeper.Tests.UnitTests;

public class VisitRecordPlaceScopingTests
{
    [Fact]
    public void CalculateMarkerScale_IncreasesWithVisitsAndPhotos()
    {
        var small = VisitRecordPlaceScoping.CalculateMarkerScale(1, 1);
        var large = VisitRecordPlaceScoping.CalculateMarkerScale(10, 120);
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
        var newer = new VisitRecordPreviewPhotoDto
        {
            MediaId = Guid.NewGuid(),
            FileName = "2021.jpg",
            AbsoluteLibraryPath = @"D:\2021.jpg",
            IsFavorite = true,
            CapturedAt = new DateTimeOffset(2021, 8, 1, 12, 0, 0, TimeSpan.Zero),
            CaptureYear = 2021
        };

        var place = new VisitRecordPlaceDto
        {
            PlaceId = placeId,
            PlaceName = "Test",
            PhotoCount = 2,
            VisitRecordCount = 2,
            FavoriteCount = 1,
            CaptureYears = [2020, 2021],
            AllPhotos = [older, newer],
            PreviewPhotos = [older, newer],
            RepresentativeMediaId = older.MediaId,
            RepresentativeAbsolutePath = older.AbsoluteLibraryPath,
            FirstCapturedDate = older.CapturedAt,
            LastCapturedDate = newer.CapturedAt,
            Latitude = 1,
            Longitude = 2,
            MarkerScale = 1
        };

        var scoped = VisitRecordPlaceScoping.ScopeToYear(place, 2020);
        Assert.Equal(1, scoped.PhotoCount);
        Assert.Equal(1, scoped.VisitRecordCount);
        Assert.Equal(0, scoped.FavoriteCount);
        Assert.Equal([2020], scoped.CaptureYears);
        Assert.Single(scoped.AllPhotos);
        Assert.Equal(older.MediaId, scoped.RepresentativeMediaId);
    }

    [Fact]
    public void ScopeToYear_UnclassifiedYearsRemainDistinct()
    {
        var place = new VisitRecordPlaceDto
        {
            PlaceId = LibraryConstants.UnclassifiedPlaceId,
            PlaceName = LibraryConstants.UnclassifiedTitle,
            IsUnclassified = true,
            PhotoCount = 2,
            VisitRecordCount = 2,
            CaptureYears = [2009, 2012],
            AllPhotos =
            [
                new VisitRecordPreviewPhotoDto
                {
                    MediaId = Guid.NewGuid(),
                    FileName = "a.jpg",
                    AbsoluteLibraryPath = @"D:\a.jpg",
                    CapturedAt = new DateTimeOffset(2009, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    CaptureYear = 2009
                },
                new VisitRecordPreviewPhotoDto
                {
                    MediaId = Guid.NewGuid(),
                    FileName = "b.jpg",
                    AbsoluteLibraryPath = @"D:\b.jpg",
                    CapturedAt = new DateTimeOffset(2012, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    CaptureYear = 2012
                }
            ],
            PreviewPhotos = [],
            Latitude = 0,
            Longitude = 0,
            MarkerScale = 1
        };

        var y2009 = VisitRecordPlaceScoping.ScopeToYear(place, 2009);
        var y2012 = VisitRecordPlaceScoping.ScopeToYear(place, 2012);
        Assert.Equal(1, y2009.PhotoCount);
        Assert.Equal(1, y2012.PhotoCount);
        Assert.Equal([2009], y2009.CaptureYears);
        Assert.Equal([2012], y2012.CaptureYears);
    }
}
