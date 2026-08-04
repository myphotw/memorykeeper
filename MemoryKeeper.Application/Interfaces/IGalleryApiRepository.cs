using MemoryKeeper.Application.DTOs;
using GalleryDtos = MemoryKeeper.Application.DTOs.Gallery;

namespace MemoryKeeper.Application.Interfaces;

/// <summary>
/// TC-Backend Gallery API port (V1.0). Does not use local SQLite.
/// </summary>
public interface IGalleryApiRepository
{
    Task<PagedResult<GalleryDtos.PhotoDto>> GetPhotosAsync(
        int page = 1,
        int pageSize = 20,
        string sort = "capture_datetime_desc",
        string? serviceName = null,
        CancellationToken cancellationToken = default);

    Task<GalleryDtos.PhotoDetailDto> GetPhotoAsync(Guid fileId, CancellationToken cancellationToken = default);

    Task<PagedResult<GalleryDtos.PhotoDto>> SearchAsync(
        int? year = null,
        string? country = null,
        string? city = null,
        string? camera = null,
        string? tag = null,
        bool? favorite = null,
        string? serviceName = null,
        DateTimeOffset? dateFrom = null,
        DateTimeOffset? dateTo = null,
        string? keyword = null,
        int page = 1,
        int pageSize = 20,
        string sort = "capture_datetime_desc",
        string? province = null,
        string? district = null,
        string? place = null,
        CancellationToken cancellationToken = default);

    Task<GalleryDtos.MapResultDto> GetMapAsync(
        int? year = null,
        string? serviceName = null,
        CancellationToken cancellationToken = default);

    Task<GalleryDtos.TimelineResultDto> GetTimelineAsync(
        string? serviceName = null,
        CancellationToken cancellationToken = default);

    Task<GalleryDtos.StatisticsDto> GetStatisticsAsync(
        string? serviceName = null,
        CancellationToken cancellationToken = default);
}
