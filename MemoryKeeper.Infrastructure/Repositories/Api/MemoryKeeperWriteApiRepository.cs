using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Infrastructure.Services.Api;

namespace MemoryKeeper.Infrastructure.Repositories.Api;

/// <summary>Authenticated tc-backend adapter for MemoryKeeper file state, tags and pending APIs.</summary>
public sealed class MemoryKeeperWriteApiRepository : IMemoryKeeperWriteApiRepository
{
    private const string Root = "/api/memorykeeper";
    private readonly BaseApiClient _apiClient;

    public MemoryKeeperWriteApiRepository(BaseApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public async Task<MemoryKeeperFileMetadataPatchResponse> PatchMetadataAsync(
        string fileId,
        MemoryKeeperFileMetadataPatchRequest request,
        CancellationToken cancellationToken = default)
    {
        var payload = MetadataPayload(request);
        return Require((await _apiClient.PatchAsync<MemoryKeeperFileMetadataPatchResponse>(
            $"{FilePath(fileId)}/metadata", payload, cancellationToken).ConfigureAwait(false)).Data,
            "사진 정보 수정 응답이 비어 있습니다.");
    }

    public async Task<MemoryKeeperDeleteResultDto> DeleteFileAsync(
        string fileId,
        CancellationToken cancellationToken = default) =>
        Require((await _apiClient.DeleteAsync<MemoryKeeperDeleteResultDto>(
            FilePath(fileId), cancellationToken).ConfigureAwait(false)).Data,
            "사진 삭제 응답이 비어 있습니다.");

    public async Task<MemoryKeeperTagListDto> GetTagsAsync(CancellationToken cancellationToken = default)
    {
        const int limit = 500;
        var items = new List<MemoryKeeperTagDto>();
        var total = 0;
        for (var offset = 0; offset == 0 || offset < total; offset += limit)
        {
            var page = (await _apiClient.GetAsync<MemoryKeeperTagListDto>(
                $"{Root}/tags?limit={limit}&offset={offset}", cancellationToken).ConfigureAwait(false)).Data
                ?? new MemoryKeeperTagListDto();
            total = page.Total;
            items.AddRange(page.Items);
            if (page.Items.Count == 0 || items.Count >= total)
            {
                break;
            }
        }

        return new MemoryKeeperTagListDto { Items = items, Total = total };
    }

    public async Task<MemoryKeeperTagDto> CreateTagAsync(
        MemoryKeeperTagCreateRequest request,
        CancellationToken cancellationToken = default) =>
        Require((await _apiClient.PostAsync<MemoryKeeperTagDto>(
            $"{Root}/tags", request, cancellationToken).ConfigureAwait(false)).Data,
            "태그 생성 응답이 비어 있습니다.");

    public async Task<MemoryKeeperTagDto> UpdateTagAsync(
        int tagId,
        MemoryKeeperTagUpdateRequest request,
        CancellationToken cancellationToken = default) =>
        Require((await _apiClient.PatchAsync<MemoryKeeperTagDto>(
            $"{Root}/tags/{tagId}", TagUpdatePayload(request), cancellationToken).ConfigureAwait(false)).Data,
            "태그 수정 응답이 비어 있습니다.");

    public async Task DeleteTagAsync(
        int tagId,
        int expectedRevision,
        CancellationToken cancellationToken = default)
    {
        await _apiClient.DeleteAsync<object>(
            $"{Root}/tags/{tagId}?expected_revision={expectedRevision}", cancellationToken).ConfigureAwait(false);
    }

    public async Task<MemoryKeeperTagDto> MergeTagAsync(
        int sourceTagId,
        MemoryKeeperTagMergeRequest request,
        CancellationToken cancellationToken = default) =>
        Require((await _apiClient.PostAsync<MemoryKeeperTagDto>(
            $"{Root}/tags/{sourceTagId}/merge", request, cancellationToken).ConfigureAwait(false)).Data,
            "태그 병합 응답이 비어 있습니다.");

    public async Task<MemoryKeeperFileTagMutationResponse> AssignFileTagAsync(
        string fileId,
        int tagId,
        int expectedRevision,
        CancellationToken cancellationToken = default) =>
        Require((await _apiClient.PostAsync<MemoryKeeperFileTagMutationResponse>(
            $"{FilePath(fileId)}/tags/{tagId}",
            new MemoryKeeperFileTagMutationRequest { ExpectedRevision = expectedRevision },
            cancellationToken).ConfigureAwait(false)).Data,
            "사진 태그 추가 응답이 비어 있습니다.");

    public async Task<MemoryKeeperFileTagMutationResponse> RemoveFileTagAsync(
        string fileId,
        int tagId,
        int expectedRevision,
        CancellationToken cancellationToken = default) =>
        Require((await _apiClient.DeleteAsync<MemoryKeeperFileTagMutationResponse>(
            $"{FilePath(fileId)}/tags/{tagId}?expected_revision={expectedRevision}",
            cancellationToken).ConfigureAwait(false)).Data,
            "사진 태그 삭제 응답이 비어 있습니다.");

    public async Task<MemoryKeeperPendingListDto> GetPendingAsync(
        bool includeSuggestions = true,
        CancellationToken cancellationToken = default)
    {
        const int pageSize = 200;
        var items = new List<MemoryKeeperPendingItemDto>();
        var total = 0;
        for (var page = 1; page == 1 || items.Count < total; page++)
        {
            var response = (await _apiClient.GetAsync<MemoryKeeperPendingListDto>(
                $"{Root}/pending?page={page}&page_size={pageSize}&include_suggestions={includeSuggestions.ToString().ToLowerInvariant()}",
                cancellationToken).ConfigureAwait(false)).Data ?? new MemoryKeeperPendingListDto();
            total = response.Total;
            items.AddRange(response.Items.Select(item => WithAbsoluteThumbnail(item, _apiClient.ApiBaseUrl)));
            if (response.Items.Count == 0 || items.Count >= total)
            {
                break;
            }
        }

        return new MemoryKeeperPendingListDto
        {
            Items = items,
            Total = total,
            Page = 1,
            PageSize = pageSize,
        };
    }

    public async Task<MemoryKeeperPendingAssignResponse> AssignPendingPlaceAsync(
        MemoryKeeperPendingAssignRequest request,
        CancellationToken cancellationToken = default) =>
        Require((await _apiClient.PostAsync<MemoryKeeperPendingAssignResponse>(
            $"{Root}/pending/assign-place", request, cancellationToken).ConfigureAwait(false)).Data,
            "미완성 추억 장소 지정 응답이 비어 있습니다.");

    private static Dictionary<string, object?> MetadataPayload(MemoryKeeperFileMetadataPatchRequest request)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["expected_revision"] = request.ExpectedRevision,
        };
        foreach (var field in request.ChangedFields)
        {
            payload[field] = field switch
            {
                "favorite" => request.Favorite,
                "memo" => request.Memo,
                "gps_lat" => request.GpsLat,
                "gps_lon" => request.GpsLon,
                "country" => request.Country,
                "province" => request.Province,
                "city" => request.City,
                "district" => request.District,
                "place_name" => request.PlaceName,
                _ => throw new ArgumentException($"지원하지 않는 사진 정보 필드입니다: {field}", nameof(request)),
            };
        }

        if (payload.Count == 1)
        {
            throw new ArgumentException("변경할 사진 정보가 없습니다.", nameof(request));
        }

        return payload;
    }

    private static Dictionary<string, object> TagUpdatePayload(MemoryKeeperTagUpdateRequest request)
    {
        var payload = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["revision"] = request.Revision,
        };
        if (request.Name is not null)
        {
            payload["name"] = request.Name;
        }
        if (request.Favorite.HasValue)
        {
            payload["favorite"] = request.Favorite.Value;
        }
        if (payload.Count == 1)
        {
            throw new ArgumentException("변경할 태그 정보가 없습니다.", nameof(request));
        }

        return payload;
    }

    private static string FilePath(string fileId) =>
        $"{Root}/files/{Uri.EscapeDataString(fileId)}";

    private static MemoryKeeperPendingItemDto WithAbsoluteThumbnail(
        MemoryKeeperPendingItemDto item,
        string apiBaseUrl)
    {
        var thumbnail = item.ThumbnailUrl;
        if (!string.IsNullOrWhiteSpace(thumbnail)
            && !Uri.TryCreate(thumbnail, UriKind.Absolute, out _))
        {
            thumbnail = apiBaseUrl.TrimEnd('/') + "/" + thumbnail.TrimStart('/');
        }

        return new MemoryKeeperPendingItemDto
        {
            FileId = item.FileId,
            ThumbnailUrl = thumbnail,
            CaptureDatetime = item.CaptureDatetime,
            GpsLat = item.GpsLat,
            GpsLon = item.GpsLon,
            Country = item.Country,
            Province = item.Province,
            City = item.City,
            District = item.District,
            PlaceName = item.PlaceName,
            MemorykeeperPlaceId = item.MemorykeeperPlaceId,
            PlaceRevision = item.PlaceRevision,
            SuggestedPlaceId = item.SuggestedPlaceId,
            SuggestedPlaceName = item.SuggestedPlaceName,
            SuggestedMatchSource = item.SuggestedMatchSource,
        };
    }

    private static T Require<T>(T? value, string message) where T : class =>
        value ?? throw new InvalidOperationException(message);
}
