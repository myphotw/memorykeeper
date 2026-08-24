using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Infrastructure.Services.Api;

namespace MemoryKeeper.Infrastructure.Repositories.Api;

public sealed class NasPhotoExportSource : IPhotoExportSource
{
    private readonly GalleryHierarchyService _hierarchy;
    private readonly IGalleryApiRepository _gallery;
    private readonly BaseApiClient _apiClient;

    public NasPhotoExportSource(
        GalleryHierarchyService hierarchy,
        IGalleryApiRepository gallery,
        BaseApiClient apiClient)
    {
        _hierarchy = hierarchy ?? throw new ArgumentNullException(nameof(hierarchy));
        _gallery = gallery ?? throw new ArgumentNullException(nameof(gallery));
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    }

    public Task<IReadOnlyList<PhotoExportCatalogItemDto>> GetCatalogAsync(CancellationToken cancellationToken = default) =>
        _hierarchy.GetExportCatalogAsync(cancellationToken);

    public async Task<PhotoExportSourceDetailDto> GetDetailAsync(
        string fileId,
        CancellationToken cancellationToken = default)
    {
        var mediaId = BackendFileIdCodec.ToGuid(fileId);
        if (mediaId == Guid.Empty)
        {
            throw new InvalidOperationException("사진 정보를 확인할 수 없습니다.");
        }

        var detail = await _gallery.GetPhotoAsync(mediaId, cancellationToken).ConfigureAwait(false);
        var originalUrl = string.IsNullOrWhiteSpace(detail.OriginalUrl)
            ? $"/api/common/gallery/{Uri.EscapeDataString(fileId)}/original"
            : detail.OriginalUrl;
        return new PhotoExportSourceDetailDto { Detail = detail, OriginalUrl = originalUrl };
    }

    public Task DownloadOriginalAsync(
        string fileId,
        string originalUrl,
        Stream destination,
        CancellationToken cancellationToken = default) =>
        _apiClient.DownloadToAsync(
            string.IsNullOrWhiteSpace(originalUrl)
                ? $"/api/common/gallery/{Uri.EscapeDataString(fileId)}/original"
                : originalUrl,
            destination,
            cancellationToken);
}
