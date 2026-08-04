using System.Globalization;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Domain.Enums;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using Directory = MetadataExtractor.Directory;

namespace MemoryKeeper.Infrastructure.Metadata;

public sealed class ExifReader
{
    private const int TagOffsetTime = 0x9010;
    private const int TagOffsetTimeOriginal = 0x9011;
    private const int TagOffsetTimeDigitized = 0x9012;

    public MetadataModel Read(string filePath)
    {
        IReadOnlyList<Directory> directories;
        try
        {
            directories = ImageMetadataReader.ReadMetadata(filePath);
        }
        catch (ImageProcessingException)
        {
            return new MetadataModel { FilePath = filePath };
        }
        catch (IOException)
        {
            return new MetadataModel { FilePath = filePath };
        }

        var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
        var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
        var gps = directories.OfType<GpsDirectory>().FirstOrDefault();

        double? gpsLat = null;
        double? gpsLon = null;
        if (gps is not null && gps.TryGetGeoLocation(out var geo))
        {
            gpsLat = geo.Latitude;
            gpsLon = geo.Longitude;
        }

        double? altitude = null;
        if (gps is not null && gps.TryGetDouble(GpsDirectory.TagAltitude, out var alt))
        {
            altitude = alt;
            if (gps.TryGetInt32(GpsDirectory.TagAltitudeRef, out var altRef) && altRef == 1)
            {
                altitude = -Math.Abs(altitude.Value);
            }
        }

        return new MetadataModel
        {
            FilePath = filePath,
            DateTimeOriginalRaw = TryGetString(subIfd, ExifDirectoryBase.TagDateTimeOriginal),
            OffsetTimeOriginalRaw = TryGetString(subIfd, TagOffsetTimeOriginal),
            CreateDateRaw = TryGetString(ifd0, ExifDirectoryBase.TagDateTime),
            OffsetTimeRaw = TryGetString(subIfd, TagOffsetTime) ?? TryGetString(ifd0, TagOffsetTime),
            DateTimeDigitizedRaw = TryGetString(subIfd, ExifDirectoryBase.TagDateTimeDigitized),
            OffsetTimeDigitizedRaw = TryGetString(subIfd, TagOffsetTimeDigitized),
            GpsLatitudeRaw = gps?.GetObject(GpsDirectory.TagLatitude),
            GpsLatitudeRef = TryGetString(gps, GpsDirectory.TagLatitudeRef),
            GpsLongitudeRaw = gps?.GetObject(GpsDirectory.TagLongitude),
            GpsLongitudeRef = TryGetString(gps, GpsDirectory.TagLongitudeRef),
            GpsAltitude = altitude,
            GpsLatitudeDecimal = gpsLat,
            GpsLongitudeDecimal = gpsLon,
            Orientation = TryGetInt(ifd0, ExifDirectoryBase.TagOrientation)
                          ?? TryGetInt(subIfd, ExifDirectoryBase.TagOrientation),
            Width = TryGetInt(subIfd, ExifDirectoryBase.TagExifImageWidth)
                    ?? TryGetInt(ifd0, ExifDirectoryBase.TagImageWidth),
            Height = TryGetInt(subIfd, ExifDirectoryBase.TagExifImageHeight)
                     ?? TryGetInt(ifd0, ExifDirectoryBase.TagImageHeight),
            CameraMaker = TryGetString(ifd0, ExifDirectoryBase.TagMake),
            CameraModel = TryGetString(ifd0, ExifDirectoryBase.TagModel),
            Lens = TryGetStringByName(directories, "Lens Model"),
            Iso = FormatIso(subIfd),
            Exposure = FormatExposure(subIfd),
            FNumber = FormatFNumber(subIfd),
            FocalLength = FormatFocalLength(subIfd),
            TagDump = BuildTagDump(directories)
        };
    }

    public MediaMetadataDto ToDto(MetadataModel model, MediaType mediaType)
    {
        DateTimeOffset? fileCreatedAt = null;
        DateTimeOffset? fileModifiedAt = null;
        try
        {
            fileCreatedAt = new DateTimeOffset(DateTime.SpecifyKind(File.GetCreationTime(model.FilePath), DateTimeKind.Local));
            fileModifiedAt = new DateTimeOffset(DateTime.SpecifyKind(File.GetLastWriteTime(model.FilePath), DateTimeKind.Local));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        var date = DateResolver.Resolve(model, fileCreatedAt, fileModifiedAt);
        var gps = GpsParser.Parse(model);

        return new MediaMetadataDto
        {
            MediaType = mediaType,
            CapturedAt = date.CapturedAt,
            CaptureDateSource = date.Source.ToString(),
            DateTimeOriginal = date.DateTimeOriginal,
            FileCreatedAt = fileCreatedAt,
            FileModifiedAt = fileModifiedAt,
            Latitude = gps.Latitude,
            Longitude = gps.Longitude,
            Altitude = gps.Altitude,
            GpsFormat = gps.Format,
            Orientation = model.Orientation,
            Width = model.Width,
            Height = model.Height,
            CameraMaker = model.CameraMaker,
            CameraModel = model.CameraModel,
            Lens = model.Lens,
            Iso = model.Iso,
            Exposure = model.Exposure,
            FNumber = model.FNumber,
            FocalLength = model.FocalLength,
            TagDump = model.TagDump
        };
    }

    private static IReadOnlyDictionary<string, string> BuildTagDump(IEnumerable<Directory> directories)
    {
        var dump = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in directories)
        {
            foreach (var tag in directory.Tags)
            {
                var key = $"{directory.Name}:{tag.Name}";
                dump.TryAdd(key, tag.Description ?? tag.ToString() ?? string.Empty);
            }
        }
        return dump;
    }

    private static string? TryGetString(Directory? directory, int tagType)
    {
        if (directory is null || !directory.ContainsTag(tagType)) return null;
        try
        {
            var value = directory.GetString(tagType);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        catch { return null; }
    }

    private static string? TryGetStringByName(IEnumerable<Directory> directories, string tagName)
    {
        foreach (var directory in directories)
        {
            foreach (var tag in directory.Tags)
            {
                if (string.Equals(tag.Name, tagName, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(tag.Description))
                {
                    return tag.Description.Trim();
                }
            }
        }
        return null;
    }

    private static int? TryGetInt(Directory? directory, int tagType)
        => directory is not null && directory.TryGetInt32(tagType, out var value) ? value : null;

    private static string? FormatIso(ExifSubIfdDirectory? subIfd)
    {
        var iso = TryGetInt(subIfd, ExifDirectoryBase.TagIsoEquivalent);
        return iso is int value && value > 0 ? value.ToString(CultureInfo.InvariantCulture) : null;
    }

    private static string? FormatExposure(ExifSubIfdDirectory? subIfd)
    {
        if (subIfd is null) return null;
        try
        {
            if (subIfd.TryGetRational(ExifDirectoryBase.TagExposureTime, out var rational) && rational.Denominator != 0)
            {
                var seconds = rational.Numerator / (double)rational.Denominator;
                if (seconds <= 0) return null;
                if (seconds >= 1) return seconds.ToString("0.###", CultureInfo.InvariantCulture) + "s";
                var den = (int)Math.Round(1d / seconds);
                return den > 0 ? $"1/{den}s" : seconds.ToString("0.####", CultureInfo.InvariantCulture) + "s";
            }
        }
        catch { }
        return TryGetString(subIfd, ExifDirectoryBase.TagExposureTime);
    }

    private static string? FormatFNumber(ExifSubIfdDirectory? subIfd)
    {
        if (subIfd is null) return null;
        try
        {
            if (subIfd.TryGetRational(ExifDirectoryBase.TagFNumber, out var rational) && rational.Denominator != 0)
            {
                return (rational.Numerator / (double)rational.Denominator).ToString("0.#", CultureInfo.InvariantCulture);
            }
        }
        catch { }
        return TryGetString(subIfd, ExifDirectoryBase.TagFNumber);
    }

    private static string? FormatFocalLength(ExifSubIfdDirectory? subIfd)
    {
        if (subIfd is null) return null;
        try
        {
            if (subIfd.TryGetRational(ExifDirectoryBase.TagFocalLength, out var rational) && rational.Denominator != 0)
            {
                return (rational.Numerator / (double)rational.Denominator).ToString("0.#", CultureInfo.InvariantCulture);
            }
        }
        catch { }
        return TryGetString(subIfd, ExifDirectoryBase.TagFocalLength);
    }
}
