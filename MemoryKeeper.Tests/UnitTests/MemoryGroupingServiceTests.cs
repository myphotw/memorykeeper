using MemoryKeeper.Application.Services;
using MemoryKeeper.Domain.Entities;
using MemoryKeeper.Domain.Enums;

namespace MemoryKeeper.Tests.UnitTests;

public class MemoryGroupingServiceTests
{
    [Fact]
    public void GroupWithoutGps_GroupsByDateSessionAndSequentialFileNames()
    {
        var service = new MemoryGroupingService(
            sessionGap: TimeSpan.FromMinutes(30),
            extendedSessionGap: TimeSpan.FromMinutes(60));

        var day = new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);
        var media = new List<Media>
        {
            CreatePending("IMG_001.jpg", day, gps: false),
            CreatePending("IMG_002.jpg", day.AddMinutes(10), gps: false),
            CreatePending("IMG_003.jpg", day.AddMinutes(25), gps: false),
            CreatePending("IMG_010.jpg", day.AddHours(3), gps: false),
            CreatePending("IMG_011.jpg", day.AddHours(3).AddMinutes(5), gps: false),
            CreatePending("OTHER_001.jpg", day.AddDays(1), gps: false),
            CreatePending("GPS_001.jpg", day, gps: true),
            CreateImportedWithPlace("DONE.jpg", day)
        };

        var groups = service.GroupWithoutGps(media);

        Assert.Equal(3, groups.Count);
        Assert.Equal(3, groups[0].Count);
        Assert.Equal(["IMG_001.jpg", "IMG_002.jpg", "IMG_003.jpg"], groups[0].Select(item => item.FileName));
        Assert.Equal(["IMG_010.jpg", "IMG_011.jpg"], groups[1].Select(item => item.FileName));
        Assert.Equal(["OTHER_001.jpg"], groups[2].Select(item => item.FileName));
        Assert.All(groups, group => Assert.False(MemoryGroupingService.GroupHasUnknownDate(group)));
    }

    [Fact]
    public void GroupWithoutGps_UnknownDate_UsesImportFolderAndFileName_NotImportedAtAsCaptureDate()
    {
        var service = new MemoryGroupingService(
            sessionGap: TimeSpan.FromMinutes(30),
            extendedSessionGap: TimeSpan.FromMinutes(60));

        var importBatch = new DateTimeOffset(2026, 7, 24, 8, 0, 0, TimeSpan.Zero);
        var media = new List<Media>
        {
            CreateUnknownDate("IMG_001.jpg", @"D:\trip\osaka\IMG_001.jpg", importBatch),
            CreateUnknownDate("IMG_002.jpg", @"D:\trip\osaka\IMG_002.jpg", importBatch.AddMinutes(2)),
            CreateUnknownDate("IMG_003.jpg", @"D:\trip\tokyo\IMG_003.jpg", importBatch.AddMinutes(3)),
            CreateUnknownDate("IMG_004.jpg", @"D:\trip\tokyo\IMG_004.jpg", importBatch.AddMinutes(4)),
            CreatePending("DATED.jpg", importBatch, gps: false)
        };

        var groups = service.GroupWithoutGps(media);

        Assert.Equal(3, groups.Count);

        var known = groups.Single(group => !MemoryGroupingService.GroupHasUnknownDate(group));
        Assert.Equal(["DATED.jpg"], known.Select(item => item.FileName));
        Assert.Equal("2026-07-24 사진 그룹", MemoryGroupingService.BuildGroupName(known));

        var unknownGroups = groups.Where(MemoryGroupingService.GroupHasUnknownDate).ToList();
        Assert.Equal(2, unknownGroups.Count);
        Assert.All(unknownGroups, group =>
            Assert.Equal(MemoryGroupingService.UnknownDateGroupName, MemoryGroupingService.BuildGroupName(group)));

        Assert.Contains(unknownGroups, group => group.Select(item => item.FileName).SequenceEqual(["IMG_001.jpg", "IMG_002.jpg"]));
        Assert.Contains(unknownGroups, group => group.Select(item => item.FileName).SequenceEqual(["IMG_003.jpg", "IMG_004.jpg"]));
    }

    [Fact]
    public void AreSequentialFileNames_DetectsContinuousImageNames()
    {
        Assert.True(MemoryGroupingService.AreSequentialFileNames("IMG_001.jpg", "IMG_002.jpg"));
        Assert.False(MemoryGroupingService.AreSequentialFileNames("IMG_001.jpg", "IMG_003.jpg"));
        Assert.False(MemoryGroupingService.AreSequentialFileNames("IMG_001.jpg", "DSC_002.jpg"));
    }

    [Fact]
    public void GetCapturedAt_DoesNotFallBackToImportedAt()
    {
        var media = new Media
        {
            Id = Guid.NewGuid(),
            FileName = "a.jpg",
            MediaType = MediaType.Photo,
            Status = MediaStatus.Pending,
            OriginalPath = @"D:\a.jpg",
            RelativePath = @"a.jpg",
            ContentHash = "h",
            CapturedAt = null,
            ImportedAt = DateTime.UtcNow,
            StorageId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        Assert.Null(MemoryGroupingService.GetCapturedAt(media));
        Assert.True(MemoryGroupingService.HasUnknownDate(media));
    }

    private static Media CreatePending(string fileName, DateTimeOffset capturedAt, bool gps)
    {
        return new Media
        {
            Id = Guid.NewGuid(),
            FileName = fileName,
            MediaType = MediaType.Photo,
            Status = MediaStatus.Pending,
            OriginalPath = $@"D:\src\{fileName}",
            RelativePath = $@"2026\{fileName}",
            ContentHash = Guid.NewGuid().ToString("N"),
            CapturedAt = capturedAt.UtcDateTime,
            ImportedAt = capturedAt.UtcDateTime.AddDays(1),
            Latitude = gps ? 34.6873 : null,
            Longitude = gps ? 135.5262 : null,
            PlaceId = null,
            StorageId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static Media CreateUnknownDate(string fileName, string originalPath, DateTimeOffset importedAt)
    {
        return new Media
        {
            Id = Guid.NewGuid(),
            FileName = fileName,
            MediaType = MediaType.Photo,
            Status = MediaStatus.Pending,
            OriginalPath = originalPath,
            RelativePath = $@"unknown\{fileName}",
            ContentHash = Guid.NewGuid().ToString("N"),
            CapturedAt = null,
            ImportedAt = importedAt.UtcDateTime,
            PlaceId = null,
            StorageId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static Media CreateImportedWithPlace(string fileName, DateTimeOffset capturedAt)
    {
        return new Media
        {
            Id = Guid.NewGuid(),
            FileName = fileName,
            MediaType = MediaType.Photo,
            Status = MediaStatus.Imported,
            OriginalPath = $@"D:\src\{fileName}",
            RelativePath = $@"2026\{fileName}",
            ContentHash = Guid.NewGuid().ToString("N"),
            CapturedAt = capturedAt.UtcDateTime,
            ImportedAt = capturedAt.UtcDateTime.AddDays(1),
            PlaceId = Guid.NewGuid(),
            StorageId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }
}
