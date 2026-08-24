using MemoryKeeper.Application.DTOs.Gallery;

namespace MemoryKeeper.Application.DTOs;

public sealed class PhotoExportCatalogItemDto
{
    public string FileId { get; init; } = string.Empty;
    public string Filename { get; init; } = string.Empty;
    public string Year { get; init; } = string.Empty;
    public string Country { get; init; } = string.Empty;
    public string Region { get; init; } = string.Empty;
    public string Place { get; init; } = string.Empty;
    public DateTimeOffset? CaptureDatetime { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
}

public sealed class PhotoExportProgressDto
{
    public int Total { get; init; }
    public int Completed { get; init; }
    public int Failed { get; init; }
    public string CurrentFileName { get; init; } = string.Empty;
}

public sealed class PhotoExportResultDto
{
    public int TotalCount { get; init; }
    public int ExportedCount { get; init; }
    public int MetadataPartialCount { get; init; }
    public int CopyFailedCount { get; init; }
    public string DestinationPath { get; init; } = string.Empty;
}

public sealed class PhotoExportSourceDetailDto
{
    public required MemoryKeeper.Application.DTOs.Gallery.PhotoDetailDto Detail { get; init; }
    public string OriginalUrl { get; init; } = string.Empty;
}
