using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Domain.Enums;

namespace MemoryKeeper.Tests.UnitTests;

public class MediaMetadataDtoTests
{
    [Fact]
    public void ResolveCapturedAt_UsesExifThenFileCreatedThenFileModified()
    {
        var exif = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var created = new DateTimeOffset(2021, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var modified = new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero);

        Assert.Equal(exif, MediaMetadataDto.ResolveCapturedAt(exif, created, modified));
        Assert.Equal(created, MediaMetadataDto.ResolveCapturedAt(null, created, modified));
        Assert.Equal(modified, MediaMetadataDto.ResolveCapturedAt(null, null, modified));
        Assert.Null(MediaMetadataDto.ResolveCapturedAt(null, null, null));
    }

    [Fact]
    public void MediaMetadataDto_CanCarryFileTimestampsSeparately()
    {
        var dto = new MediaMetadataDto
        {
            MediaType = MediaType.Photo,
            CapturedAt = null,
            FileCreatedAt = null,
            FileModifiedAt = null
        };

        Assert.Null(dto.CapturedAt);
        Assert.Null(dto.FileCreatedAt);
        Assert.Null(dto.FileModifiedAt);
    }
}
