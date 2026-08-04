using System.Globalization;
using System.Text.RegularExpressions;
using MetadataExtractor;

namespace MemoryKeeper.Infrastructure.Metadata;

/// <summary>
/// Converts GPS coordinate representations (Decimal / DMS / Rational) to decimal degrees.
/// Inspired by AstroJournal rational parsing; implemented independently for MemoryKeeper.
/// </summary>
public static class CoordinateConverter
{
    public static double? ToDecimalDegrees(object? raw, string? cardinalRef)
    {
        if (raw is null)
        {
            return null;
        }

        if (raw is double d)
        {
            return ApplyCardinal(d, cardinalRef);
        }

        if (raw is float f)
        {
            return ApplyCardinal(f, cardinalRef);
        }

        if (raw is Rational[] rationals && rationals.Length >= 1)
        {
            return FromRationalArray(rationals, cardinalRef);
        }

        if (raw is IReadOnlyList<Rational> list && list.Count >= 1)
        {
            return FromRationalArray(list, cardinalRef);
        }

        var text = raw.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var decimalValue))
        {
            return ApplyCardinal(decimalValue, cardinalRef);
        }

        var fromDmsText = TryParseDmsText(text);
        return fromDmsText is null ? null : ApplyCardinal(fromDmsText.Value, cardinalRef);
    }

    public static double? FromDms(double degrees, double minutes, double seconds, bool negative)
    {
        if (degrees < 0)
        {
            negative = true;
            degrees = Math.Abs(degrees);
        }

        var value = degrees + (minutes / 60d) + (seconds / 3600d);
        return negative ? -value : value;
    }

    public static bool IsValidLatitude(double latitude) => latitude is >= -90 and <= 90;

    public static bool IsValidLongitude(double longitude) => longitude is >= -180 and <= 180;

    private static double? FromRationalArray(IReadOnlyList<Rational> rationals, string? cardinalRef)
    {
        var degrees = ToDouble(rationals[0]);
        var minutes = rationals.Count > 1 ? ToDouble(rationals[1]) : 0d;
        var seconds = rationals.Count > 2 ? ToDouble(rationals[2]) : 0d;
        if (degrees is null)
        {
            return null;
        }

        var negative = IsSouthernOrWestern(cardinalRef);
        return FromDms(degrees.Value, minutes ?? 0d, seconds ?? 0d, negative);
    }

    private static double? ToDouble(Rational rational)
    {
        if (rational.Denominator == 0)
        {
            return null;
        }

        return rational.Numerator / (double)rational.Denominator;
    }

    private static double? TryParseDmsText(string text)
    {
        // Examples: 37/1,30/1,0/1  |  37° 30' 0"  |  37 30 0
        var slashParts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (slashParts.Length >= 1 && slashParts[0].Contains('/'))
        {
            var values = new List<double>();
            foreach (var part in slashParts.Take(3))
            {
                var parsed = ParseRationalText(part);
                if (parsed is null)
                {
                    return null;
                }

                values.Add(parsed.Value);
            }

            while (values.Count < 3)
            {
                values.Add(0);
            }

            return FromDms(values[0], values[1], values[2], negative: false);
        }

        var match = Regex.Match(
            text,
            @"(-?\d+(?:\.\d+)?)\D+(\d+(?:\.\d+)?)?\D*(\d+(?:\.\d+)?)?");
        if (!match.Success)
        {
            return null;
        }

        var deg = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var min = match.Groups[2].Success
            ? double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture)
            : 0d;
        var sec = match.Groups[3].Success
            ? double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture)
            : 0d;
        return FromDms(deg, min, sec, negative: deg < 0);
    }

    private static double? ParseRationalText(string raw)
    {
        var trimmed = raw.Trim();
        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var direct))
        {
            return direct;
        }

        if (!trimmed.Contains('/'))
        {
            return null;
        }

        var parts = trimmed.Split('/');
        if (parts.Length != 2)
        {
            return null;
        }

        if (!double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ||
            !double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ||
            d == 0)
        {
            return null;
        }

        return n / d;
    }

    private static double ApplyCardinal(double value, string? cardinalRef)
    {
        if (IsSouthernOrWestern(cardinalRef))
        {
            return -Math.Abs(value);
        }

        return value;
    }

    private static bool IsSouthernOrWestern(string? cardinalRef)
    {
        if (string.IsNullOrWhiteSpace(cardinalRef))
        {
            return false;
        }

        var c = char.ToUpperInvariant(cardinalRef.Trim()[0]);
        return c is 'S' or 'W';
    }
}
