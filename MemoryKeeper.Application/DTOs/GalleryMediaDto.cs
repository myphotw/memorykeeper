using MemoryKeeper.Domain.Enums;

namespace MemoryKeeper.Application.DTOs;

public sealed class GalleryMediaDto
{
    public Guid Id { get; init; }

    public string FileName { get; init; } = string.Empty;

    public string AbsoluteLibraryPath { get; init; } = string.Empty;

    public DateTimeOffset? CapturedAt { get; init; }

    public Guid? PlaceId { get; init; }

    public MediaType MediaType { get; init; }

    public bool IsFavorite { get; init; }

    /// <summary>Remote thumbnail URL (TC-Backend). Optional.</summary>
    public string? ThumbnailUrl { get; init; }

    /// <summary>Remote preview URL (TC-Backend). Optional.</summary>
    public string? PreviewUrl { get; init; }
}
