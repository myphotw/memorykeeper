using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;

namespace MemoryKeeper.App.Services;

/// <summary>
/// PC-only read-through snapshot for the startup Gallery reads shared by Home,
/// the top-level Gallery, and the embedded Settings Gallery.
/// </summary>
public sealed class CachedFastGalleryApiRepository : IFastGalleryApiRepository, IDisposable
{
    private const int FirstPageSize = 50;
    private readonly IFastGalleryApiRepository _inner;
    private readonly GenerationAwareAsyncCache<FastGalleryPhotoPageDto> _firstPage;
    private readonly GenerationAwareAsyncCache<FastGallerySummaryDto> _summary;
    private readonly GenerationAwareAsyncCache<FastGalleryHierarchyDto> _hierarchy;

    public CachedFastGalleryApiRepository(
        IFastGalleryApiRepository inner,
        ICatalogInvalidation invalidation)
    {
        _inner = inner;
        _firstPage = new GenerationAwareAsyncCache<FastGalleryPhotoPageDto>(
            invalidation,
            CatalogSurface.Home | CatalogSurface.Gallery,
            token => _inner.GetPhotosAsync(new FastGalleryPhotoQuery { Limit = FirstPageSize }, token));
        _summary = new GenerationAwareAsyncCache<FastGallerySummaryDto>(
            invalidation,
            CatalogSurface.Home | CatalogSurface.Gallery,
            _inner.GetSummaryAsync);
        _hierarchy = new GenerationAwareAsyncCache<FastGalleryHierarchyDto>(
            invalidation,
            CatalogSurface.Gallery | CatalogSurface.Places,
            _inner.GetHierarchyAsync);
    }

    public Task<FastGalleryPhotoPageDto> GetPhotosAsync(
        FastGalleryPhotoQuery query,
        CancellationToken cancellationToken = default) =>
        IsDefaultFirstPage(query)
            ? _firstPage.GetAsync(cancellationToken)
            : _inner.GetPhotosAsync(query, cancellationToken);

    public Task<FastGallerySummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default) =>
        _summary.GetAsync(cancellationToken);

    public Task<FastGalleryHierarchyDto> GetHierarchyAsync(CancellationToken cancellationToken = default) =>
        _hierarchy.GetAsync(cancellationToken);

    private static bool IsDefaultFirstPage(FastGalleryPhotoQuery query) =>
        query.Limit == FirstPageSize
        && string.IsNullOrEmpty(query.Cursor)
        && query.Year is null
        && string.IsNullOrEmpty(query.Country)
        && string.IsNullOrEmpty(query.Region)
        && string.IsNullOrEmpty(query.LocationKey)
        && query.PlaceId is null
        && query.Unclassified is null
        && query.DateUnclassified is null
        && query.Favorite is null
        && query.HasGps is null
        && query.DateFrom is null
        && query.DateTo is null
        && string.IsNullOrEmpty(query.PhotoCategory);

    public void Dispose()
    {
        _firstPage.Dispose();
        _summary.Dispose();
        _hierarchy.Dispose();
    }
}
