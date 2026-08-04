using MemoryKeeper.Domain.Enums;

namespace MemoryKeeper.Domain.Entities;

/// <summary>
/// Photo or video managed in the library.
/// </summary>
public class Media : BaseEntity
{
    public string FileName { get; set; } = string.Empty;

    public MediaType MediaType { get; set; }

    public MediaStatus Status { get; set; }

    /// <summary>
    /// Absolute path of the original source file. The original is never modified.
    /// </summary>
    public string OriginalPath { get; set; } = string.Empty;

    /// <summary>
    /// Relative path under Storage.PhotoRoot (forward-slash form), e.g. 2026/Osaka/IMG0001.jpg.
    /// </summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>
    /// Content hash used for duplicate detection.
    /// </summary>
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>
    /// Capture time stored as UTC DateTime (SQLite-safe).
    /// </summary>
    public DateTime? CapturedAt { get; set; }

    /// <summary>
    /// Raw EXIF DateTimeOriginal when available.
    /// </summary>
    public string? DateTimeOriginal { get; set; }

    /// <summary>
    /// Library registration time stored as UTC DateTime (SQLite-safe).
    /// </summary>
    public DateTime ImportedAt { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public double? Altitude { get; set; }

    public int? Orientation { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    public string? CameraMaker { get; set; }

    public string? CameraModel { get; set; }

    public string? Lens { get; set; }

    public string? Iso { get; set; }

    public string? Exposure { get; set; }

    public string? FNumber { get; set; }

    public string? FocalLength { get; set; }

    /// <summary>
    /// User memo for the photo (MK-042S).
    /// </summary>
    public string? Memo { get; set; }

    public Guid StorageId { get; set; }

    public Storage? Storage { get; set; }

    public Guid? PlaceId { get; set; }

    public Place? Place { get; set; }

    /// <summary>
    /// Favorite flag used as a core priority signal for memories.
    /// </summary>
    public bool IsFavorite { get; set; }
}
