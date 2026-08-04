using MemoryKeeper.Infrastructure.Metadata;

namespace MemoryKeeper.Tests.UnitTests;

public class ExifEngineTests
{
    [Fact]
    public void DateResolver_PrefersDateTimeOriginalWithOffset()
    {
        var model = new MetadataModel
        {
            DateTimeOriginalRaw = "2024:05:01 10:20:30",
            OffsetTimeOriginalRaw = "+09:00",
            CreateDateRaw = "2020:01:01 00:00:00",
            DateTimeDigitizedRaw = "2021:01:01 00:00:00"
        };

        var result = DateResolver.Resolve(
            model,
            fileCreatedAt: new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero),
            fileModifiedAt: new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(CaptureDateSource.DateTimeOriginal, result.Source);
        Assert.Equal(new DateTimeOffset(2024, 5, 1, 10, 20, 30, TimeSpan.FromHours(9)), result.CapturedAt);
    }

    [Fact]
    public void DateResolver_FallsBackToCreateDateThenDigitizedThenFileCreated()
    {
        var createOnly = DateResolver.Resolve(
            new MetadataModel { CreateDateRaw = "2018:03:02 10:40:20" },
            fileCreatedAt: new DateTimeOffset(2019, 1, 1, 0, 0, 0, TimeSpan.Zero),
            fileModifiedAt: null);
        Assert.Equal(CaptureDateSource.CreateDate, createOnly.Source);

        var digitized = DateResolver.Resolve(
            new MetadataModel { DateTimeDigitizedRaw = "2017:01:02 03:04:05" },
            fileCreatedAt: new DateTimeOffset(2019, 1, 1, 0, 0, 0, TimeSpan.Zero),
            fileModifiedAt: null);
        Assert.Equal(CaptureDateSource.DateTimeDigitized, digitized.Source);

        var fileCreated = DateResolver.Resolve(
            new MetadataModel(),
            fileCreatedAt: new DateTimeOffset(2019, 1, 1, 0, 0, 0, TimeSpan.Zero),
            fileModifiedAt: new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(CaptureDateSource.FileCreated, fileCreated.Source);
    }

    [Fact]
    public void CoordinateConverter_ParsesDecimalDmsAndRationalText()
    {
        Assert.Equal(37.5, CoordinateConverter.ToDecimalDegrees(37.5, "N"));
        Assert.Equal(-37.5, CoordinateConverter.ToDecimalDegrees(37.5, "S"));

        var dms = CoordinateConverter.FromDms(37, 30, 0, negative: false);
        Assert.Equal(37.5, dms);

        var fromText = CoordinateConverter.ToDecimalDegrees("37/1,30/1,0/1", "N");
        Assert.Equal(37.5, fromText);

        Assert.True(CoordinateConverter.IsValidLatitude(37.5));
        Assert.False(CoordinateConverter.IsValidLatitude(100));
    }

    [Fact]
    public void GpsParser_UsesDecimalWhenAvailable_ElseParsesRaw()
    {
        var decimalModel = new MetadataModel
        {
            GpsLatitudeDecimal = 35.6812,
            GpsLongitudeDecimal = 139.7671
        };
        var decimalResult = GpsParser.Parse(decimalModel);
        Assert.True(decimalResult.HasGps);
        Assert.Equal("Decimal", decimalResult.Format);

        var rawModel = new MetadataModel
        {
            GpsLatitudeRaw = "37/1,30/1,0/1",
            GpsLatitudeRef = "N",
            GpsLongitudeRaw = "127/1,0/1,0/1",
            GpsLongitudeRef = "E"
        };
        var rawResult = GpsParser.Parse(rawModel);
        Assert.True(rawResult.HasGps);
        Assert.Equal(37.5, rawResult.Latitude);
        Assert.Equal(127.0, rawResult.Longitude);
    }
}
