using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Infrastructure.Services.Api;

namespace MemoryKeeper.Infrastructure.Repositories.Api;

public sealed class FastGalleryApiRepository : IFastGalleryApiRepository
{
    private readonly BaseApiClient _apiClient;

    public FastGalleryApiRepository(BaseApiClient apiClient) => _apiClient = apiClient;

    public async Task<FastGalleryPhotoPageDto> GetPhotosAsync(FastGalleryPhotoQuery query, CancellationToken cancellationToken = default)
    {
        var path = FastGalleryRequestPathBuilder.Photos(query);
        return (await _apiClient.GetAsync<FastGalleryPhotoPageDto>(path, cancellationToken).ConfigureAwait(false)).Data
               ?? new FastGalleryPhotoPageDto();
    }

    public async Task<FastGallerySummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default) =>
        (await _apiClient.GetAsync<FastGallerySummaryDto>($"{FastGalleryRequestPathBuilder.Root}/summary", cancellationToken).ConfigureAwait(false)).Data
        ?? new FastGallerySummaryDto();

    public async Task<FastGalleryHierarchyDto> GetHierarchyAsync(CancellationToken cancellationToken = default) =>
        (await _apiClient.GetAsync<FastGalleryHierarchyDto>($"{FastGalleryRequestPathBuilder.Root}/hierarchy", cancellationToken).ConfigureAwait(false)).Data
        ?? new FastGalleryHierarchyDto();
}
