namespace MemoryKeeper.Infrastructure.Metadata;

/// <summary>
/// Raw EXIF model extracted from an image file (AstroJournal ExifInfo-inspired, independent).
/// </summary>
public sealed class MetadataModel
{
    public string FilePath { get; init; } = string.Empty;

    public string? DateTimeOriginalRaw { get; init; }

    public string? OffsetTimeOriginalRaw { get; init; }

    public string? CreateDateRaw { get; init; }

    public string? OffsetTimeRaw { get; init; }

    public string? DateTimeDigitizedRaw { get; init; }

    public string? OffsetTimeDigitizedRaw { get; init; }

    public object? GpsLatitudeRaw { get; init; }

    public string? GpsLatitudeRef { get; init; }

    public object? GpsLongitudeRaw { get; init; }

    public string? GpsLongitudeRef { get; init; }

    public double? GpsAltitude { get; init; }

    /// <summary>
    /// Decimal GPS already resolved by MetadataExtractor when available.
    /// </summary>
    public double? GpsLatitudeDecimal { get; init; }

    public double? GpsLongitudeDecimal { get; init; }

    public int? Orientation { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }

    public string? CameraMaker { get; init; }

    public string? CameraModel { get; init; }

    public string? Lens { get; init; }

    public string? Iso { get; init; }

    public string? Exposure { get; init; }

    public string? FNumber { get; init; }

    public string? FocalLength { get; init; }

    public IReadOnlyDictionary<string, string> TagDump { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
