using MemoryKeeper.Domain.Enums;

namespace MemoryKeeper.Application.DTOs;

public sealed class GalleryMediaDto
{
    public Guid Id { get; init; }

    /// <summary>Original TC-Backend <c>file_id</c> (Guid or content hash).</summary>
    public string BackendFileId { get; init; } = string.Empty;

    public string FileName { get; init; } = string.Empty;

    public string AbsoluteLibraryPath { get; init; } = string.Empty;

    public DateTimeOffset? CapturedAt { get; init; }

    public DateOnly? EffectiveCaptureDate { get; init; }

    public int? EffectiveCaptureYear { get; init; }

    public string EffectiveCapturePrecision { get; init; } = string.Empty;

    public int? SourceCaptureYear { get; init; }

    public string SourceCaptureYearBasis { get; init; } = string.Empty;

    public Guid? PlaceId { get; init; }

    public string PhotoCategory { get; init; } = MemoryKeeperPhotoCategories.Normal;

    public int PhotoCategoryRevision { get; init; }

    public bool HasPhotoCategoryRevision { get; init; }

    public MediaType MediaType { get; init; }

    public bool IsFavorite { get; init; }

    /// <summary>Remote thumbnail URL (TC-Backend). Optional.</summary>
    public string? ThumbnailUrl { get; init; }

    /// <summary>Remote preview URL (TC-Backend). Optional.</summary>
    public string? PreviewUrl { get; init; }
}
