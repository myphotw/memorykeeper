using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Application.Interfaces;

/// <summary>NAS Gallery-only source for read-only photo export.</summary>
public interface IPhotoExportSource
{
    Task<IReadOnlyList<PhotoExportCatalogItemDto>> GetCatalogAsync(CancellationToken cancellationToken = default);
    Task<PhotoExportSourceDetailDto> GetDetailAsync(string fileId, CancellationToken cancellationToken = default);
    Task DownloadOriginalAsync(string fileId, string originalUrl, Stream destination, CancellationToken cancellationToken = default);
}
