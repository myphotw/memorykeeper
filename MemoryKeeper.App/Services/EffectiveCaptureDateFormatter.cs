using System.Globalization;

namespace MemoryKeeper.App.Services;

public static class EffectiveCaptureDateFormatter
{
    public static bool IsYearOnly(string? precision, int? year) =>
        string.Equals(precision, "YEAR", StringComparison.OrdinalIgnoreCase)
        && year is > 0;

    public static string Format(
        string? precision,
        int? year,
        DateOnly? effectiveDate,
        DateTimeOffset? effectiveDateTime,
        string dateOnlyFormat,
        string dateTimeFormat,
        string missingText)
    {
        if (IsYearOnly(precision, year))
        {
            return $"{year!.Value}년";
        }

        if (string.Equals(precision, "DATE", StringComparison.OrdinalIgnoreCase)
            && effectiveDate is DateOnly date)
        {
            return date.ToString(dateOnlyFormat, CultureInfo.InvariantCulture);
        }

        return effectiveDateTime?.ToLocalTime().ToString(dateTimeFormat, CultureInfo.InvariantCulture)
               ?? missingText;
    }

    public static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? date
            : null;
}
