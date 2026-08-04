using System.Globalization;
using System.Text.RegularExpressions;

namespace MemoryKeeper.Infrastructure.Metadata;

public enum CaptureDateSource
{
    None,
    DateTimeOriginal,
    CreateDate,
    DateTimeDigitized,
    FileCreated,
    FileModified
}

public sealed class DateResolveResult
{
    public DateTimeOffset? CapturedAt { get; init; }

    public CaptureDateSource Source { get; init; }

    public string? DateTimeOriginal { get; init; }
}

/// <summary>
/// Resolves capture datetime with EXIF-first priority (AstroJournal-inspired).
/// Priority: DateTimeOriginal(+Offset) → CreateDate → DateTimeDigitized → FileCreated → FileModified.
/// </summary>
public static class DateResolver
{
    public static DateResolveResult Resolve(
        MetadataModel model,
        DateTimeOffset? fileCreatedAt,
        DateTimeOffset? fileModifiedAt)
    {
        var original = TryParseExifDate(model.DateTimeOriginalRaw, model.OffsetTimeOriginalRaw);
        if (original is not null)
        {
            return new DateResolveResult
            {
                CapturedAt = original,
                Source = CaptureDateSource.DateTimeOriginal,
                DateTimeOriginal = model.DateTimeOriginalRaw
            };
        }

        var createDate = TryParseExifDate(model.CreateDateRaw, model.OffsetTimeRaw ?? model.OffsetTimeOriginalRaw);
        if (createDate is not null)
        {
            return new DateResolveResult
            {
                CapturedAt = createDate,
                Source = CaptureDateSource.CreateDate,
                DateTimeOriginal = model.DateTimeOriginalRaw
            };
        }

        var digitized = TryParseExifDate(model.DateTimeDigitizedRaw, model.OffsetTimeDigitizedRaw);
        if (digitized is not null)
        {
            return new DateResolveResult
            {
                CapturedAt = digitized,
                Source = CaptureDateSource.DateTimeDigitized,
                DateTimeOriginal = model.DateTimeOriginalRaw
            };
        }

        if (fileCreatedAt is not null)
        {
            return new DateResolveResult
            {
                CapturedAt = fileCreatedAt,
                Source = CaptureDateSource.FileCreated,
                DateTimeOriginal = model.DateTimeOriginalRaw
            };
        }

        if (fileModifiedAt is not null)
        {
            return new DateResolveResult
            {
                CapturedAt = fileModifiedAt,
                Source = CaptureDateSource.FileModified,
                DateTimeOriginal = model.DateTimeOriginalRaw
            };
        }

        return new DateResolveResult
        {
            CapturedAt = null,
            Source = CaptureDateSource.None,
            DateTimeOriginal = model.DateTimeOriginalRaw
        };
    }

    public static DateTimeOffset? TryParseExifDate(string? dateRaw, string? offsetRaw)
    {
        if (string.IsNullOrWhiteSpace(dateRaw))
        {
            return null;
        }

        var trimmed = dateRaw.Trim();

        // Already ISO-like
        if (DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var iso))
        {
            return iso;
        }

        // EXIF classic: "yyyy:MM:dd HH:mm:ss"
        var match = Regex.Match(
            trimmed,
            @"^(?<y>\d{4}):(?<mo>\d{2}):(?<d>\d{2})[ T](?<h>\d{2}):(?<mi>\d{2}):(?<s>\d{2})");
        if (!match.Success)
        {
            return null;
        }

        var year = int.Parse(match.Groups["y"].Value, CultureInfo.InvariantCulture);
        var month = int.Parse(match.Groups["mo"].Value, CultureInfo.InvariantCulture);
        var day = int.Parse(match.Groups["d"].Value, CultureInfo.InvariantCulture);
        var hour = int.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture);
        var minute = int.Parse(match.Groups["mi"].Value, CultureInfo.InvariantCulture);
        var second = int.Parse(match.Groups["s"].Value, CultureInfo.InvariantCulture);

        var offset = TryParseOffset(offsetRaw) ?? TimeSpan.Zero;
        try
        {
            var local = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified);
            return new DateTimeOffset(local, offset);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    public static TimeSpan? TryParseOffset(string? offsetRaw)
    {
        if (string.IsNullOrWhiteSpace(offsetRaw))
        {
            return null;
        }

        var text = offsetRaw.Trim();
        if (text is "Z" or "z")
        {
            return TimeSpan.Zero;
        }

        // +09:00 / -05:00 / +0900
        var match = Regex.Match(text, @"^(?<sign>[+-])(?<h>\d{2}):?(?<m>\d{2})$");
        if (!match.Success)
        {
            return null;
        }

        var hours = int.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture);
        var minutes = int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture);
        var span = new TimeSpan(hours, minutes, 0);
        return match.Groups["sign"].Value == "-" ? -span : span;
    }
}
