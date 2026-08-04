using MemoryKeeper.Domain.Enums;

namespace MemoryKeeper.Application.DTOs;

public sealed class MediaMetadataDto
{
    /// <summary>
    /// Resolved capture time using EXIF-first priority then file timestamps.
    /// </summary>
    public DateTimeOffset? CapturedAt { get; init; }

    /// <summary>
    /// DateTimeOriginal / CreateDate / DateTimeDigitized / FileCreated / FileModified / None.
    /// </summary>
    public string CaptureDateSource { get; init; } = "None";

    /// <summary>
    /// Raw EXIF DateTimeOriginal string when present.
    /// </summary>
    public string? DateTimeOriginal { get; init; }

    public DateTimeOffset? FileCreatedAt { get; init; }

    public DateTimeOffset? FileModifiedAt { get; init; }

    public double? Latitude { get; init; }

    public double? Longitude { get; init; }

    public double? Altitude { get; init; }

    public string GpsFormat { get; init; } = "None";

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

    public MediaType MediaType { get; init; }

    public static DateTimeOffset? ResolveCapturedAt(
        DateTimeOffset? exifCapturedAt,
        DateTimeOffset? fileCreatedAt,
        DateTimeOffset? fileModifiedAt)
    {
        return exifCapturedAt ?? fileCreatedAt ?? fileModifiedAt;
    }
}
