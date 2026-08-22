namespace MemoryKeeper.Application.DTOs;

public sealed record VisitRecordPlaceDto
{
    public Guid PlaceId { get; init; }

    public string PlaceName { get; init; } = string.Empty;

    public string Country { get; init; } = string.Empty;

    public string City { get; init; } = string.Empty;

    public double Latitude { get; init; }

    public double Longitude { get; init; }

    public int PhotoCount { get; init; }

    public int VisitRecordCount { get; init; }

    public int FavoriteCount { get; init; }

    public bool HasFavorite => FavoriteCount > 0;

    public Guid? RepresentativeMediaId { get; init; }

    public string? RepresentativeAbsolutePath { get; init; }

    public DateTimeOffset? FirstCapturedDate { get; init; }

    public DateTimeOffset? LastCapturedDate { get; init; }

    /// <summary>Capture/import years that have photos at this place (MK-046).</summary>
    public IReadOnlyList<int> CaptureYears { get; init; } = [];

    public IReadOnlyList<string> TopTags { get; init; } = [];

    /// <summary>All place photos with resolvable library paths (year-filterable).</summary>
    public IReadOnlyList<VisitRecordPreviewPhotoDto> AllPhotos { get; init; } = [];

    /// <summary>Preview strip (subset of <see cref="AllPhotos"/>).</summary>
    public IReadOnlyList<VisitRecordPreviewPhotoDto> PreviewPhotos { get; init; } = [];

    public double MarkerScale { get; init; } = 1.0;

    /// <summary>True for the synthetic 미분류 bucket. GPS availability alone controls marker visibility.</summary>
    public bool IsUnclassified { get; init; }
}

public sealed record VisitRecordPreviewPhotoDto
{
    public Guid MediaId { get; init; }

    /// <summary>Original TC-Backend file_id (SHA-256 or Guid string).</summary>
    public string BackendFileId { get; init; } = string.Empty;

    public string FileName { get; init; } = string.Empty;

    /// <summary>Absolute HTTP thumbnail URL for WinUI BitmapImage.</summary>
    public string ThumbnailUrl { get; init; } = string.Empty;

    /// <summary>Legacy alias — same as <see cref="ThumbnailUrl"/> for Backend photos.</summary>
    public string AbsoluteLibraryPath { get; init; } = string.Empty;

    public bool IsFavorite { get; init; }

    public DateTimeOffset? CapturedAt { get; init; }

    /// <summary>Local calendar year used for visit-map year grouping.</summary>
    public int CaptureYear { get; init; }
}

public sealed class VisitRecordQueryResult
{
    public IReadOnlyList<VisitRecordPlaceDto> TimelinePlaces { get; init; } = [];

    public IReadOnlyList<VisitRecordPlaceDto> AllMapPlaces { get; init; } = [];

    public IReadOnlyList<MemorySearchChipDto> Chips { get; init; } = [];
}
