using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Application.Interfaces;

/// <summary>Authenticated NAS write contract for MemoryKeeper-owned file state, tags and pending work.</summary>
public interface IMemoryKeeperWriteApiRepository
{
    Task<MemoryKeeperFileMetadataPatchResponse> PatchMetadataAsync(string fileId, MemoryKeeperFileMetadataPatchRequest request, CancellationToken cancellationToken = default);
    Task<MemoryKeeperDeleteResultDto> DeleteFileAsync(string fileId, CancellationToken cancellationToken = default);
    Task<MemoryKeeperTagListDto> GetTagsAsync(CancellationToken cancellationToken = default);
    Task<MemoryKeeperTagDto> CreateTagAsync(MemoryKeeperTagCreateRequest request, CancellationToken cancellationToken = default);
    Task<MemoryKeeperTagDto> UpdateTagAsync(int tagId, MemoryKeeperTagUpdateRequest request, CancellationToken cancellationToken = default);
    Task DeleteTagAsync(int tagId, int expectedRevision, CancellationToken cancellationToken = default);
    Task<MemoryKeeperTagDto> MergeTagAsync(int sourceTagId, MemoryKeeperTagMergeRequest request, CancellationToken cancellationToken = default);
    Task<MemoryKeeperFileTagMutationResponse> AssignFileTagAsync(string fileId, int tagId, int expectedRevision, CancellationToken cancellationToken = default);
    Task<MemoryKeeperFileTagMutationResponse> RemoveFileTagAsync(string fileId, int tagId, int expectedRevision, CancellationToken cancellationToken = default);
    Task<MemoryKeeperPendingListDto> GetPendingAsync(bool includeSuggestions = true, CancellationToken cancellationToken = default);
    Task<MemoryKeeperPendingAssignResponse> AssignPendingPlaceAsync(MemoryKeeperPendingAssignRequest request, CancellationToken cancellationToken = default);
}
