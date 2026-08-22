using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;

namespace MemoryKeeper.Application.Services;

/// <summary>
/// MemoryKeeper registered-place operations backed exclusively by tc-backend.
/// The legacy SQLite PlaceService remains available for legacy/import code, but is
/// deliberately not consulted or merged here.
/// </summary>
public sealed class MemoryKeeperPlaceService
{
    private readonly IMemoryKeeperPlaceApiRepository _repository;
    private readonly ICatalogInvalidation _invalidation;

    public MemoryKeeperPlaceService(
        IMemoryKeeperPlaceApiRepository repository,
        ICatalogInvalidation invalidation)
    {
        _repository = repository;
        _invalidation = invalidation;
    }

    public async Task<IReadOnlyList<PlaceDto>> GetPlaceListAsync(CancellationToken cancellationToken = default) =>
        (await _repository.GetPlacesAsync(cancellationToken).ConfigureAwait(false)).Items
        .Select(Map)
        .OrderBy(place => place.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public async Task<IReadOnlyList<PlaceDto>> GetFavoritePlacesAsync(CancellationToken cancellationToken = default) =>
        (await GetPlaceListAsync(cancellationToken).ConfigureAwait(false))
        .Where(place => place.IsFavorite)
        .ToList();

    public async Task<IReadOnlyList<PlaceDto>> GetRecentPlacesAsync(int take, CancellationToken cancellationToken = default) =>
        (await GetPlaceListAsync(cancellationToken).ConfigureAwait(false))
        .Where(place => place.LastUsedAt.HasValue)
        .OrderByDescending(place => place.LastUsedAt)
        .Take(Math.Max(0, take))
        .ToList();

    public async Task<PlaceDto> GetPlaceAsync(Guid placeId, CancellationToken cancellationToken = default) =>
        Map(await _repository.GetPlaceAsync(placeId, cancellationToken).ConfigureAwait(false));

    public async Task<PlaceDto?> MatchPlaceAsync(
        double latitude,
        double longitude,
        string? providerPlaceId = null,
        string? canonicalName = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _repository.MatchAsync(new MemoryKeeperPlaceMatchApiRequest
        {
            Latitude = latitude,
            Longitude = longitude,
            ProviderPlaceId = NullIfBlank(providerPlaceId),
            CanonicalName = NullIfBlank(canonicalName),
        }, cancellationToken).ConfigureAwait(false);
        return result.Matched && result.Place is not null ? Map(result.Place) : null;
    }

    public Task<PlaceDto> CreatePlaceAsync(
        CreatePlaceRequest request,
        CancellationToken cancellationToken = default) =>
        CreatePlaceAsync(request, geographyFallback: null, cancellationToken);

    public async Task<PlaceDto> CreatePlaceAsync(
        CreatePlaceRequest request,
        PlaceGeographyFallback? geographyFallback,
        CancellationToken cancellationToken = default)
    {
        var created = await _repository.CreatePlaceAsync(new MemoryKeeperPlaceCreateApiRequest
        {
            DisplayName = RequireName(request.DisplayName),
            CanonicalName = NullIfBlank(request.CanonicalName),
            Address = NullIfBlank(FirstNotBlank(request.Address, geographyFallback?.Address)),
            PostalCode = NullIfBlank(request.PostalCode),
            Country = NullIfBlank(FirstNotBlank(request.Country, geographyFallback?.Country)),
            Province = NullIfBlank(FirstNotBlank(request.Province, geographyFallback?.Province)),
            City = NullIfBlank(FirstNotBlank(request.City, geographyFallback?.City)),
            District = NullIfBlank(FirstNotBlank(request.District, geographyFallback?.District)),
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            RadiusM = request.Radius ?? 100,
            ProviderPlaceId = NullIfBlank(request.GooglePlaceId),
            Category = NullIfBlank(request.Category),
            Active = request.IsActive,
            Favorite = request.IsFavorite,
        }, cancellationToken).ConfigureAwait(false);
        _invalidation.Invalidate();
        return Map(created);
    }

    public async Task<PlaceDto> UpdatePlaceAsync(UpdatePlaceRequest request, CancellationToken cancellationToken = default)
    {
        var updated = await _repository.UpdatePlaceAsync(
            request.Id,
            ToApiUpdateRequest(request),
            cancellationToken).ConfigureAwait(false);
        _invalidation.Invalidate();
        return Map(updated);
    }

    /// <summary>
    /// Preserves the original Place editor transaction: preview spatial impact, let the
    /// caller confirm overlaps, PATCH using the current revision, then reclassify once.
    /// No server mutation occurs when overlap confirmation is cancelled.
    /// </summary>
    public async Task<MemoryKeeperPlaceUpdateOperationResult> UpdateWithRadiusImpactAsync(
        PlaceDto original,
        UpdatePlaceRequest request,
        Func<MemoryKeeperRadiusImpactApiResult, CancellationToken, Task<bool>> confirmOverlapAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(confirmOverlapAsync);
        if (original.Id != request.Id)
        {
            throw new ArgumentException("수정 대상 장소가 일치하지 않습니다.", nameof(request));
        }

        var geometryChanged = HasGeometryChanged(original, request);
        MemoryKeeperRadiusImpactApiResult? impact = null;
        if (geometryChanged)
        {
            impact = await GetRadiusImpactAsync(
                request.Latitude,
                request.Longitude,
                request.Radius,
                request.Id,
                cancellationToken);
            if (impact.OverlappingPlaces.Count > 0
                && !await confirmOverlapAsync(impact, cancellationToken))
            {
                return new MemoryKeeperPlaceUpdateOperationResult
                {
                    Cancelled = true,
                    GeometryChanged = true,
                    RadiusImpact = impact,
                };
            }
        }

        MemoryKeeperPlaceApiDto updated;
        MemoryKeeperPlaceReclassifyApiResult? reclassified = null;
        var patched = false;
        try
        {
            updated = await _repository.UpdatePlaceAsync(
                request.Id,
                ToApiUpdateRequest(request),
                cancellationToken);
            patched = true;

            // Backend deliberately retains relations when a place is made inactive and
            // rejects reclassification for inactive places. Do not invent a detach policy.
            if (geometryChanged && updated.Active)
            {
                reclassified = await _repository.ReclassifyAsync(
                    updated.Id,
                    reassignFromOtherPlaces: true,
                    cancellationToken);
            }
        }
        finally
        {
            // Cancelled operations return before this block. Once PATCH succeeds (or a
            // later reclassify fails), every consumer must discard stale joined names.
            if (patched)
            {
                _invalidation.Invalidate();
            }
        }

        return new MemoryKeeperPlaceUpdateOperationResult
        {
            GeometryChanged = geometryChanged,
            ReclassificationSkippedBecauseInactive = geometryChanged && !updated.Active,
            UpdatedPlace = Map(updated),
            RadiusImpact = impact,
            Reclassification = reclassified is null
                ? new PlaceReclassificationResult { PlaceId = updated.Id }
                : Map(reclassified),
        };
    }

    public Task<PlaceDto> SetPlaceFavoriteAsync(PlaceDto place, bool favorite, CancellationToken cancellationToken = default) =>
        PatchFlagsAsync(place, favorite: favorite, active: null, cancellationToken);

    public Task<PlaceDto> SetPlaceActiveAsync(PlaceDto place, bool active, CancellationToken cancellationToken = default) =>
        PatchFlagsAsync(place, favorite: null, active: active, cancellationToken);

    private async Task<PlaceDto> PatchFlagsAsync(
        PlaceDto place,
        bool? favorite,
        bool? active,
        CancellationToken cancellationToken)
    {
        var updated = await _repository.UpdatePlaceAsync(place.Id, new MemoryKeeperPlaceUpdateApiRequest
        {
            Revision = place.Revision,
            DisplayName = place.DisplayName,
            CanonicalName = place.CanonicalName,
            Address = NullIfBlank(place.Address),
            PostalCode = NullIfBlank(place.PostalCode),
            Country = NullIfBlank(place.Country),
            Province = NullIfBlank(place.Province),
            City = NullIfBlank(place.City),
            District = NullIfBlank(place.District),
            Latitude = place.Latitude,
            Longitude = place.Longitude,
            RadiusM = place.Radius,
            ProviderPlaceId = NullIfBlank(place.GooglePlaceId),
            Category = NullIfBlank(place.Category),
            Favorite = favorite ?? place.IsFavorite,
            Active = active ?? place.IsActive,
        }, cancellationToken).ConfigureAwait(false);
        _invalidation.Invalidate();
        return Map(updated);
    }

    public async Task<(bool Succeeded, string Message, int MediaCount)> DeletePlaceAsync(
        Guid placeId,
        CancellationToken cancellationToken = default)
    {
        await _repository.DeletePlaceAsync(placeId, cancellationToken).ConfigureAwait(false);
        _invalidation.Invalidate();
        return (true, "장소를 삭제했습니다. 연결된 사진은 원본 위치 정보가 유지된 미등록 상태로 전환됩니다.", 0);
    }

    public async Task<PlaceReclassificationResult> ReclassifyMediaAsync(
        Guid placeId,
        bool reassignFromOtherPlaces,
        CancellationToken cancellationToken = default)
    {
        var result = await _repository.ReclassifyAsync(placeId, reassignFromOtherPlaces, cancellationToken)
            .ConfigureAwait(false);
        _invalidation.Invalidate();
        return Map(result);
    }

    public Task<MemoryKeeperRadiusImpactApiResult> GetRadiusImpactAsync(
        double latitude,
        double longitude,
        double radiusMeters,
        Guid? placeId = null,
        CancellationToken cancellationToken = default) =>
        _repository.GetRadiusImpactAsync(new MemoryKeeperRadiusImpactApiRequest
        {
            PlaceId = placeId,
            Latitude = latitude,
            Longitude = longitude,
            RadiusM = radiusMeters,
        }, cancellationToken);

    public async Task AssignFilePlaceAsync(
        Guid mediaId,
        Guid? placeId,
        int expectedRevision,
        CancellationToken cancellationToken = default)
    {
        var fileId = BackendFileIdCodec.ToApiFileId(mediaId);
        await _repository.AssignFilePlaceAsync(fileId, placeId, expectedRevision, cancellationToken)
            .ConfigureAwait(false);
        _invalidation.Invalidate();
    }

    private static PlaceDto Map(MemoryKeeperPlaceApiDto place) => new()
    {
        Id = place.Id,
        DisplayName = place.DisplayName,
        CanonicalName = place.CanonicalName,
        Address = place.Address ?? string.Empty,
        PostalCode = place.PostalCode ?? string.Empty,
        Country = place.Country ?? string.Empty,
        Province = place.Province ?? string.Empty,
        City = place.City ?? string.Empty,
        District = place.District ?? string.Empty,
        Latitude = place.Latitude,
        Longitude = place.Longitude,
        Radius = place.RadiusM,
        GooglePlaceId = place.ProviderPlaceId,
        Category = place.Category,
        IsActive = place.Active,
        IsFavorite = place.Favorite,
        UsageCount = place.UsageCount,
        MediaCount = place.UsageCount,
        LastUsedAt = place.LastUsedAt?.UtcDateTime,
        Revision = place.Revision,
        CreatedAt = place.CreatedAt,
        UpdatedAt = place.UpdatedAt,
    };

    private static PlaceReclassificationResult Map(MemoryKeeperPlaceReclassifyApiResult result) => new()
    {
        PlaceId = result.PlaceId,
        AssignedCount = result.Assigned,
        ReassignedFromOtherCount = result.Reassigned,
        UnassignedCount = result.UnassignedOutsideRadius,
    };

    private static MemoryKeeperPlaceUpdateApiRequest ToApiUpdateRequest(UpdatePlaceRequest request) => new()
    {
        Revision = request.Revision,
        DisplayName = RequireName(request.DisplayName),
        CanonicalName = NullIfBlank(request.CanonicalName),
        Address = NullIfBlank(request.Address),
        PostalCode = NullIfBlank(request.PostalCode),
        Country = NullIfBlank(request.Country),
        Province = NullIfBlank(request.Province),
        City = NullIfBlank(request.City),
        District = NullIfBlank(request.District),
        Latitude = request.Latitude,
        Longitude = request.Longitude,
        RadiusM = request.Radius,
        ProviderPlaceId = NullIfBlank(request.GooglePlaceId),
        Category = NullIfBlank(request.Category),
        Active = request.IsActive,
        Favorite = request.IsFavorite,
    };

    private static bool HasGeometryChanged(PlaceDto original, UpdatePlaceRequest request) =>
        Math.Abs(original.Latitude - request.Latitude) > 0.0000001
        || Math.Abs(original.Longitude - request.Longitude) > 0.0000001
        || Math.Abs(original.Radius - request.Radius) > 0.01;

    private static string RequireName(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("장소 이름을 입력하세요.")
            : value.Trim();

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? FirstNotBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
