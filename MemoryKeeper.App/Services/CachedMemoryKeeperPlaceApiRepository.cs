using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;

namespace MemoryKeeper.App.Services;

/// <summary>
/// Shares immutable place catalog reads while leaving all detail and mutation calls direct.
/// MemoryKeeperPlaceService remains the owner of successful-mutation invalidation.
/// </summary>
public sealed class CachedMemoryKeeperPlaceApiRepository : IMemoryKeeperPlaceApiRepository, IDisposable
{
    private readonly IMemoryKeeperPlaceApiRepository _inner;
    private readonly GenerationAwareAsyncCache<MemoryKeeperPlaceListApiDto> _places;
    private readonly GenerationAwareAsyncCache<MemoryKeeperPlaceHierarchyListApiDto> _hierarchy;
    private readonly GenerationAwareAsyncCache<MemoryKeeperPlaceDuplicateCandidateListApiDto> _duplicates;

    public CachedMemoryKeeperPlaceApiRepository(
        IMemoryKeeperPlaceApiRepository inner,
        ICatalogInvalidation invalidation)
    {
        _inner = inner;
        var surfaces = CatalogSurface.Gallery | CatalogSurface.Places;
        _places = new GenerationAwareAsyncCache<MemoryKeeperPlaceListApiDto>(
            invalidation, surfaces, _inner.GetPlacesAsync);
        _hierarchy = new GenerationAwareAsyncCache<MemoryKeeperPlaceHierarchyListApiDto>(
            invalidation, surfaces, _inner.GetPlaceHierarchyAsync);
        _duplicates = new GenerationAwareAsyncCache<MemoryKeeperPlaceDuplicateCandidateListApiDto>(
            invalidation, surfaces, _inner.GetDuplicateCandidatesAsync);
    }

    public Task<MemoryKeeperPlaceListApiDto> GetPlacesAsync(CancellationToken cancellationToken = default) =>
        _places.GetAsync(cancellationToken);

    public Task<MemoryKeeperPlaceHierarchyListApiDto> GetPlaceHierarchyAsync(CancellationToken cancellationToken = default) =>
        _hierarchy.GetAsync(cancellationToken);

    public Task<MemoryKeeperPlaceDuplicateCandidateListApiDto> GetDuplicateCandidatesAsync(CancellationToken cancellationToken = default) =>
        _duplicates.GetAsync(cancellationToken);

    public Task<MemoryKeeperPlaceApiDto> GetPlaceAsync(Guid placeId, CancellationToken cancellationToken = default) =>
        _inner.GetPlaceAsync(placeId, cancellationToken);

    public Task<MemoryKeeperPlaceApiDto> CreatePlaceAsync(MemoryKeeperPlaceCreateApiRequest request, CancellationToken cancellationToken = default) =>
        _inner.CreatePlaceAsync(request, cancellationToken);

    public Task<MemoryKeeperPlaceApiDto> UpdatePlaceAsync(Guid placeId, MemoryKeeperPlaceUpdateApiRequest request, CancellationToken cancellationToken = default) =>
        _inner.UpdatePlaceAsync(placeId, request, cancellationToken);

    public Task DeletePlaceAsync(Guid placeId, int expectedRevision, CancellationToken cancellationToken = default) =>
        _inner.DeletePlaceAsync(placeId, expectedRevision, cancellationToken);

    public Task<MemoryKeeperPlacePhotoPageApiDto> GetPlacePhotosAsync(Guid placeId, int limit, int offset, CancellationToken cancellationToken = default) =>
        _inner.GetPlacePhotosAsync(placeId, limit, offset, cancellationToken);

    public Task<MemoryKeeperPlaceMergePreviewApiDto> GetMergePreviewAsync(Guid sourcePlaceId, Guid targetPlaceId, CancellationToken cancellationToken = default) =>
        _inner.GetMergePreviewAsync(sourcePlaceId, targetPlaceId, cancellationToken);

    public Task<MemoryKeeperPlaceMergeApiResult> MergePlacesAsync(MemoryKeeperPlaceMergeApiRequest request, CancellationToken cancellationToken = default) =>
        _inner.MergePlacesAsync(request, cancellationToken);

    public Task<MemoryKeeperPlaceMatchApiResult> MatchAsync(MemoryKeeperPlaceMatchApiRequest request, CancellationToken cancellationToken = default) =>
        _inner.MatchAsync(request, cancellationToken);

    public Task<MemoryKeeperPlaceReclassifyApiResult> ReclassifyAsync(Guid placeId, bool reassignFromOtherPlaces, CancellationToken cancellationToken = default) =>
        _inner.ReclassifyAsync(placeId, reassignFromOtherPlaces, cancellationToken);

    public Task<MemoryKeeperRadiusImpactApiResult> GetRadiusImpactAsync(MemoryKeeperRadiusImpactApiRequest request, CancellationToken cancellationToken = default) =>
        _inner.GetRadiusImpactAsync(request, cancellationToken);

    public Task<MemoryKeeperFilePlaceUpdateApiResult> AssignFilePlaceAsync(string fileId, Guid? placeId, int expectedRevision, CancellationToken cancellationToken = default) =>
        _inner.AssignFilePlaceAsync(fileId, placeId, expectedRevision, cancellationToken);

    public void Dispose()
    {
        _places.Dispose();
        _hierarchy.Dispose();
        _duplicates.Dispose();
    }
}
