namespace MemoryKeeper.Infrastructure.Metadata;

public sealed class GpsParseResult
{
    public double? Latitude { get; init; }

    public double? Longitude { get; init; }

    public double? Altitude { get; init; }

    public bool HasGps => Latitude is not null && Longitude is not null;

    public string Format { get; init; } = "None";
}

/// <summary>
/// Parses GPS from Decimal / DMS / Rational into decimal degrees.
/// </summary>
public static class GpsParser
{
    public static GpsParseResult Parse(MetadataModel model)
    {
        if (model.GpsLatitudeDecimal is double latDec &&
            model.GpsLongitudeDecimal is double lonDec &&
            CoordinateConverter.IsValidLatitude(latDec) &&
            CoordinateConverter.IsValidLongitude(lonDec))
        {
            return new GpsParseResult
            {
                Latitude = latDec,
                Longitude = lonDec,
                Altitude = model.GpsAltitude,
                Format = "Decimal"
            };
        }

        var latitude = CoordinateConverter.ToDecimalDegrees(model.GpsLatitudeRaw, model.GpsLatitudeRef);
        var longitude = CoordinateConverter.ToDecimalDegrees(model.GpsLongitudeRaw, model.GpsLongitudeRef);

        if (latitude is double lat &&
            longitude is double lon &&
            CoordinateConverter.IsValidLatitude(lat) &&
            CoordinateConverter.IsValidLongitude(lon))
        {
            var format = model.GpsLatitudeRaw?.ToString()?.Contains('/') == true ||
                         model.GpsLatitudeRaw?.GetType().Name.Contains("Rational", StringComparison.Ordinal) == true
                ? "Rational/DMS"
                : "Parsed";

            return new GpsParseResult
            {
                Latitude = lat,
                Longitude = lon,
                Altitude = model.GpsAltitude,
                Format = format
            };
        }

        return new GpsParseResult
        {
            Latitude = null,
            Longitude = null,
            Altitude = model.GpsAltitude,
            Format = "None"
        };
    }
}
