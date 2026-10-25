using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Application.Interfaces;

/// <summary>NAS canonical source for MemoryKeeper registered places.</summary>
public interface IMemoryKeeperPlaceApiRepository
{
    Task<MemoryKeeperPlaceListApiDto> GetPlacesAsync(CancellationToken cancellationToken = default);
    Task<MemoryKeeperPlaceHierarchyListApiDto> GetPlaceHierarchyAsync(CancellationToken cancellationToken = default);
    Task<MemoryKeeperPlaceApiDto> GetPlaceAsync(Guid placeId, CancellationToken cancellationToken = default);
    Task<MemoryKeeperPlaceApiDto> CreatePlaceAsync(MemoryKeeperPlaceCreateApiRequest request, CancellationToken cancellationToken = default);
    Task<MemoryKeeperPlaceApiDto> UpdatePlaceAsync(Guid placeId, MemoryKeeperPlaceUpdateApiRequest request, CancellationToken cancellationToken = default);
    Task DeletePlaceAsync(Guid placeId, int expectedRevision, CancellationToken cancellationToken = default);
    Task<MemoryKeeperPlacePhotoPageApiDto> GetPlacePhotosAsync(Guid placeId, int limit, int offset, CancellationToken cancellationToken = default);
    Task<MemoryKeeperPlaceDuplicateCandidateListApiDto> GetDuplicateCandidatesAsync(CancellationToken cancellationToken = default);
    Task<MemoryKeeperPlaceMergePreviewApiDto> GetMergePreviewAsync(Guid sourcePlaceId, Guid targetPlaceId, CancellationToken cancellationToken = default);
    Task<MemoryKeeperPlaceMergeApiResult> MergePlacesAsync(MemoryKeeperPlaceMergeApiRequest request, CancellationToken cancellationToken = default);
    Task<MemoryKeeperPlaceMatchApiResult> MatchAsync(MemoryKeeperPlaceMatchApiRequest request, CancellationToken cancellationToken = default);
    Task<MemoryKeeperPlaceReclassifyApiResult> ReclassifyAsync(Guid placeId, bool reassignFromOtherPlaces, CancellationToken cancellationToken = default);
    Task<MemoryKeeperRadiusImpactApiResult> GetRadiusImpactAsync(MemoryKeeperRadiusImpactApiRequest request, CancellationToken cancellationToken = default);
    Task<MemoryKeeperFilePlaceUpdateApiResult> AssignFilePlaceAsync(string fileId, Guid? placeId, int expectedRevision, CancellationToken cancellationToken = default);
}
