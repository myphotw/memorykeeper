namespace MemoryKeeper.Application.DTOs;

public sealed class RelatedPhotoDto
{
    public Guid MediaId { get; init; }

    public string FileName { get; init; } = string.Empty;

    public string AbsoluteLibraryPath { get; init; } = string.Empty;

    public DateTimeOffset? CapturedAt { get; init; }

    public bool IsFavorite { get; init; }
}

public sealed class PhotoDetailDto
{
    public Guid MediaId { get; init; }

    public string? ThumbnailPath { get; init; }

    /// <summary>Absolute HTTP thumbnail URL (TC-Backend).</summary>
    public string? ThumbnailUrl { get; init; }

    /// <summary>Absolute HTTP preview URL (TC-Backend). Prefer for Viewer display.</summary>
    public string? PreviewUrl { get; init; }

    /// <summary>Absolute original URL/path. Export / original-view only — not auto-loaded in Viewer.</summary>
    public string OriginalPath { get; init; } = string.Empty;

    public string RelativePath { get; init; } = string.Empty;

    /// <summary>Display path for legacy callers; prefer <see cref="PreviewUrl"/> then <see cref="ThumbnailUrl"/>.</summary>
    public string AbsoluteLibraryPath { get; init; } = string.Empty;

    public string FileName { get; init; } = string.Empty;

    public DateTimeOffset? CapturedAt { get; init; }

    public string Country { get; init; } = string.Empty;

    public string Province { get; init; } = string.Empty;

    public string City { get; init; } = string.Empty;

    public string Address { get; init; } = string.Empty;

    public double? Latitude { get; init; }

    public double? Longitude { get; init; }

    public Guid? PlaceId { get; init; }

    public string PlaceName { get; init; } = string.Empty;

    public string? CanonicalName { get; init; }

    public string? GooglePlaceId { get; init; }

    public bool HasGps { get; init; }

    public bool IsFavorite { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }

    public string? CameraMaker { get; init; }

    public string? CameraModel { get; init; }

    public string? Lens { get; init; }

    public string? Iso { get; init; }

    public string? Exposure { get; init; }

    public string? FNumber { get; init; }

    public string? FocalLength { get; init; }

    public long? FileSizeBytes { get; init; }

    public string Memo { get; init; } = string.Empty;

    public int TagCount => Tags.Count;

    public IReadOnlyList<TagDto> Tags { get; init; } = [];

    public IReadOnlyList<RelatedPhotoDto> RelatedPhotos { get; init; } = [];
}
