using System.Globalization;
using MemoryKeeper.Application.Time;
using MemoryKeeper.Domain.Entities;

namespace MemoryKeeper.Infrastructure.Database;

internal static class MediaQueryFilters
{
    public static (DateTime Start, DateTime End) GetYearRange(int year)
    {
        var start = DateTimeHelper.YearStartUtc(year);
        return (start, start.AddYears(1));
    }

    public static bool MatchesYear(Media media, int year)
    {
        var (start, end) = GetYearRange(year);
        if (media.CapturedAt is DateTime captured)
        {
            return captured >= start && captured < end;
        }

        return media.ImportedAt >= start && media.ImportedAt < end;
    }

    public static bool MatchesOnThisDay(Media media, int month, int day, int lookbackYears)
    {
        if (media.CapturedAt is not DateTime captured)
        {
            return false;
        }

        if (captured.Month != month || captured.Day != day)
        {
            return false;
        }

        var currentYear = DateTime.UtcNow.Year;
        var minYear = currentYear - Math.Max(1, lookbackYears);
        return captured.Year >= minYear && captured.Year < currentYear;
    }

    /// <summary>
    /// Parses legacy SQLite TEXT date values (DateTimeOffset or DateTime) into UTC DateTime.
    /// </summary>
    public static DateTime? ParseUtcDateTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var offset))
        {
            return offset.UtcDateTime;
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateTime))
        {
            return DateTime.SpecifyKind(
                dateTime.Kind == DateTimeKind.Unspecified ? dateTime : dateTime.ToUniversalTime(),
                DateTimeKind.Utc);
        }

        return null;
    }

    public static DateTime ParseUtcDateTimeRequired(string? value)
        => ParseUtcDateTime(value) ?? DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
}
