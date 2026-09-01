using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Application.Interfaces;

public interface IFastGalleryApiRepository
{
    Task<FastGalleryPhotoPageDto> GetPhotosAsync(FastGalleryPhotoQuery query, CancellationToken cancellationToken = default);
    Task<FastGallerySummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default);
    Task<FastGalleryHierarchyDto> GetHierarchyAsync(CancellationToken cancellationToken = default);
}
