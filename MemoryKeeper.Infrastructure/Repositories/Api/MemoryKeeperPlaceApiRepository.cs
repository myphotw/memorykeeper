using System.Net;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Infrastructure.Services.Api;

namespace MemoryKeeper.Infrastructure.Repositories.Api;

/// <summary>Authenticated tc-backend MemoryKeeper Place API adapter.</summary>
public sealed class MemoryKeeperPlaceApiRepository : IMemoryKeeperPlaceApiRepository
{
    private const string PlacesRoot = "/api/memorykeeper/places";
    private readonly BaseApiClient _apiClient;

    public MemoryKeeperPlaceApiRepository(BaseApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public async Task<MemoryKeeperPlaceListApiDto> GetPlacesAsync(CancellationToken cancellationToken = default)
    {
        const int limit = 500;
        var items = new List<MemoryKeeperPlaceApiDto>();
        var total = 0;
        for (var offset = 0; offset == 0 || offset < total; offset += limit)
        {
            var page = (await _apiClient.GetAsync<MemoryKeeperPlaceListApiDto>(
                $"{PlacesRoot}?limit={limit}&offset={offset}", cancellationToken).ConfigureAwait(false)).Data
                ?? new MemoryKeeperPlaceListApiDto();
            total = page.Total;
            items.AddRange(page.Items);
            if (page.Items.Count == 0 || items.Count >= total)
            {
                break;
            }
        }

        return new MemoryKeeperPlaceListApiDto
        {
            Items = items,
            Total = total,
            Limit = limit,
            Offset = 0,
        };
    }

    public async Task<MemoryKeeperPlaceApiDto> GetPlaceAsync(Guid placeId, CancellationToken cancellationToken = default) =>
        Require((await _apiClient.GetAsync<MemoryKeeperPlaceApiDto>(PlacePath(placeId), cancellationToken)
            .ConfigureAwait(false)).Data, "장소 상세 응답이 비어 있습니다.");

    public async Task<MemoryKeeperPlaceApiDto> CreatePlaceAsync(
        MemoryKeeperPlaceCreateApiRequest request,
        CancellationToken cancellationToken = default) =>
        Require((await _apiClient.PostAsync<MemoryKeeperPlaceApiDto>(PlacesRoot, request, cancellationToken)
            .ConfigureAwait(false)).Data, "장소 생성 응답이 비어 있습니다.");

    public async Task<MemoryKeeperPlaceApiDto> UpdatePlaceAsync(
        Guid placeId,
        MemoryKeeperPlaceUpdateApiRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Require((await _apiClient.PatchAsync<MemoryKeeperPlaceApiDto>(
                PlacePath(placeId), request, cancellationToken).ConfigureAwait(false)).Data,
                "장소 수정 응답이 비어 있습니다.");
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            throw RevisionConflict(ex);
        }
    }

    public async Task DeletePlaceAsync(Guid placeId, CancellationToken cancellationToken = default)
    {
        await _apiClient.DeleteAsync<object>(PlacePath(placeId), cancellationToken).ConfigureAwait(false);
    }

    public async Task<MemoryKeeperPlaceMatchApiResult> MatchAsync(
        MemoryKeeperPlaceMatchApiRequest request,
        CancellationToken cancellationToken = default) =>
        Require((await _apiClient.PostAsync<MemoryKeeperPlaceMatchApiResult>(
            $"{PlacesRoot}/match", request, cancellationToken).ConfigureAwait(false)).Data,
            "장소 매칭 응답이 비어 있습니다.");

    public async Task<MemoryKeeperPlaceReclassifyApiResult> ReclassifyAsync(
        Guid placeId,
        bool reassignFromOtherPlaces,
        CancellationToken cancellationToken = default) =>
        Require((await _apiClient.PostAsync<MemoryKeeperPlaceReclassifyApiResult>(
            $"{PlacePath(placeId)}/reclassify",
            new MemoryKeeperPlaceReclassifyApiRequest
            {
                ReassignFromOtherPlaces = reassignFromOtherPlaces,
            },
            cancellationToken).ConfigureAwait(false)).Data, "장소 재분류 응답이 비어 있습니다.");

    public async Task<MemoryKeeperRadiusImpactApiResult> GetRadiusImpactAsync(
        MemoryKeeperRadiusImpactApiRequest request,
        CancellationToken cancellationToken = default) =>
        Require((await _apiClient.PostAsync<MemoryKeeperRadiusImpactApiResult>(
            $"{PlacesRoot}/radius-impact", request, cancellationToken).ConfigureAwait(false)).Data,
            "반경 영향 응답이 비어 있습니다.");

    public async Task<MemoryKeeperFilePlaceUpdateApiResult> AssignFilePlaceAsync(
        string fileId,
        Guid? placeId,
        int expectedRevision,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var path = $"/api/memorykeeper/files/{Uri.EscapeDataString(fileId)}/place";
            return Require((await _apiClient.PatchAsync<MemoryKeeperFilePlaceUpdateApiResult>(path,
                new MemoryKeeperFilePlaceUpdateApiRequest
                {
                    MemorykeeperPlaceId = placeId,
                    ExpectedRevision = expectedRevision,
                }, cancellationToken).ConfigureAwait(false)).Data, "사진 장소 변경 응답이 비어 있습니다.");
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            throw RevisionConflict(ex);
        }
    }

    private static string PlacePath(Guid placeId) => $"{PlacesRoot}/{placeId:D}";

    private static T Require<T>(T? value, string message) where T : class =>
        value ?? throw new InvalidOperationException(message);

    private static MemoryKeeperPlaceRevisionConflictException RevisionConflict(ApiException inner) =>
        new("다른 기기에서 장소 정보가 변경되었습니다. 목록을 새로 고친 뒤 다시 시도하세요.", inner);
}
