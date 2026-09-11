using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;

namespace MemoryKeeper.Application.Services;

/// <summary>
/// MemoryKeeper-owned file state, tags and pending operations backed exclusively by tc-backend.
/// No legacy content repository is consulted or updated.
/// </summary>
public sealed class MemoryKeeperWriteService
{
    private const int MemoMaxLength = 10_000;
    private readonly IMemoryKeeperWriteApiRepository _repository;
    private readonly ICatalogInvalidation _invalidation;
    private readonly Dictionary<Guid, (string FileId, int PlaceRevision)> _pendingRevisions = [];
    private readonly Dictionary<Guid, PendingMemoryItemDto> _placeCleanupItems = [];

    public MemoryKeeperWriteService(
        IMemoryKeeperWriteApiRepository repository,
        ICatalogInvalidation invalidation)
    {
        _repository = repository;
        _invalidation = invalidation;
    }

    public async Task<MemoryKeeperFileMetadataPatchResponse> SetFavoriteAsync(
        Guid mediaId,
        bool favorite,
        int expectedRevision,
        CancellationToken cancellationToken = default)
    {
        var response = await _repository.PatchMetadataAsync(
            FileId(mediaId),
            new MemoryKeeperFileMetadataPatchRequest
            {
                ExpectedRevision = expectedRevision,
                Favorite = favorite,
                ChangedFields = Fields("favorite"),
            },
            cancellationToken).ConfigureAwait(false);
        _invalidation.Invalidate(
            CatalogSurface.Gallery | CatalogSurface.Home | CatalogSurface.Favorites
            | CatalogSurface.Visits | CatalogSurface.Travel);
        return response;
    }

    public async Task<MemoryKeeperFileMetadataPatchResponse> SetMemoAsync(
        Guid mediaId,
        string? memo,
        int expectedRevision,
        CancellationToken cancellationToken = default)
    {
        var normalized = string.IsNullOrWhiteSpace(memo) ? null : memo.Trim();
        if (normalized?.Length > MemoMaxLength)
        {
            throw new ArgumentException($"메모는 {MemoMaxLength:N0}자까지 입력할 수 있습니다.", nameof(memo));
        }

        var response = await _repository.PatchMetadataAsync(
            FileId(mediaId),
            new MemoryKeeperFileMetadataPatchRequest
            {
                ExpectedRevision = expectedRevision,
                Memo = normalized,
                ChangedFields = Fields("memo"),
            },
            cancellationToken).ConfigureAwait(false);
        _invalidation.Invalidate(CatalogSurface.Gallery | CatalogSurface.Home);
        return response;
    }

    public async Task<MemoryKeeperFileMetadataPatchResponse> SetRawLocationAsync(
        Guid mediaId,
        int expectedRevision,
        double? latitude,
        double? longitude,
        string? country,
        string? province,
        string? city,
        string? district,
        string? placeName,
        CancellationToken cancellationToken = default)
    {
        if ((latitude is null) != (longitude is null))
        {
            throw new ArgumentException("GPS 위도와 경도는 함께 입력하거나 함께 비워야 합니다.");
        }
        if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
        {
            throw new ArgumentException("GPS 좌표 범위를 확인하세요.");
        }

        ValidateLength(country, 100, "국가");
        ValidateLength(province, 100, "시/도");
        ValidateLength(city, 100, "시/군/구");
        ValidateLength(district, 100, "세부 지역");
        ValidateLength(placeName, 200, "원본 주소/장소명");

        var response = await _repository.PatchMetadataAsync(
            FileId(mediaId),
            new MemoryKeeperFileMetadataPatchRequest
            {
                ExpectedRevision = expectedRevision,
                GpsLat = latitude,
                GpsLon = longitude,
                Country = NullIfBlank(country),
                Province = NullIfBlank(province),
                City = NullIfBlank(city),
                District = NullIfBlank(district),
                PlaceName = NullIfBlank(placeName),
                ChangedFields = Fields(
                    "gps_lat", "gps_lon", "country", "province", "city", "district", "place_name"),
            },
            cancellationToken).ConfigureAwait(false);
        _invalidation.Invalidate();
        return response;
    }

    /// <summary>
    /// Supplements raw geography after an explicit place selection. Existing photo GPS is never
    /// replaced with the registered place center; the center is used only when GPS is entirely absent.
    /// Provider/place geography is patched independently from the semantic place revision.
    /// </summary>
    public async Task<MemoryKeeperFileMetadataPatchResponse?> SupplementRawLocationFromPlaceAsync(
        Guid mediaId,
        int expectedRevision,
        double? currentLatitude,
        double? currentLongitude,
        PlaceLocationPreview selectedLocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selectedLocation);

        var changedFields = new HashSet<string>(StringComparer.Ordinal);
        double? latitude = null;
        double? longitude = null;
        if (currentLatitude is null
            && currentLongitude is null
            && selectedLocation.HasCoordinates)
        {
            latitude = selectedLocation.Latitude;
            longitude = selectedLocation.Longitude;
            changedFields.Add("gps_lat");
            changedFields.Add("gps_lon");
        }

        var country = AddSupplement(changedFields, "country", selectedLocation.Country);
        var province = AddSupplement(changedFields, "province", selectedLocation.Province);
        var city = AddSupplement(changedFields, "city", selectedLocation.City);
        var district = AddSupplement(changedFields, "district", selectedLocation.District);
        var placeName = AddSupplement(
            changedFields,
            "place_name",
            FirstNotBlank(selectedLocation.Address, selectedLocation.DisplayName));

        if (changedFields.Count == 0)
        {
            return null;
        }

        ValidateLength(country, 100, "국가");
        ValidateLength(province, 100, "시/도");
        ValidateLength(city, 100, "시/군/구");
        ValidateLength(district, 100, "세부 지역");
        ValidateLength(placeName, 200, "원본 주소/장소명");

        var response = await _repository.PatchMetadataAsync(
            FileId(mediaId),
            new MemoryKeeperFileMetadataPatchRequest
            {
                ExpectedRevision = expectedRevision,
                GpsLat = latitude,
                GpsLon = longitude,
                Country = country,
                Province = province,
                City = city,
                District = district,
                PlaceName = placeName,
                ChangedFields = changedFields,
            },
            cancellationToken).ConfigureAwait(false);
        _invalidation.Invalidate();
        return response;
    }

    public async Task<MemoryKeeperDeleteResultDto> DeleteFileAsync(
        Guid mediaId,
        CancellationToken cancellationToken = default)
    {
        var response = await _repository.DeleteFileAsync(FileId(mediaId), cancellationToken).ConfigureAwait(false);
        _invalidation.Invalidate(CatalogSurface.AllRelated | CatalogSurface.Tags);
        return response;
    }

    public Task<MemoryKeeperTagListDto> GetTagsAsync(CancellationToken cancellationToken = default) =>
        _repository.GetTagsAsync(cancellationToken);

    public Task<MemoryKeeperTagCatalogListDto> GetTagCatalogAsync(
        string? query = null,
        CancellationToken cancellationToken = default) =>
        _repository.GetTagCatalogAsync(query, cancellationToken);

    public async Task<MemoryKeeperTagCatalogItemDto> RenameCatalogTagAsync(
        string identity,
        int revision,
        string name,
        CancellationToken cancellationToken = default)
    {
        var result = await _repository.RenameCatalogTagAsync(
            identity,
            new MemoryKeeperTagCatalogRenameRequest
            {
                Name = RequireName(name),
                Revision = revision,
            },
            cancellationToken).ConfigureAwait(false);
        InvalidateTags();
        return result;
    }

    public async Task DeleteCatalogTagAsync(
        string identity,
        int revision,
        CancellationToken cancellationToken = default)
    {
        await _repository.DeleteCatalogTagAsync(identity, revision, cancellationToken).ConfigureAwait(false);
        InvalidateTags();
    }

    public async Task<MemoryKeeperTagDto> CreateTagAsync(
        string name,
        bool favorite,
        CancellationToken cancellationToken = default)
    {
        var result = await _repository.CreateTagAsync(new MemoryKeeperTagCreateRequest
        {
            Name = RequireName(name),
            Favorite = favorite,
        }, cancellationToken).ConfigureAwait(false);
        InvalidateTags();
        return result;
    }

    public async Task<MemoryKeeperTagDto> UpdateTagAsync(
        int tagId,
        int revision,
        string? name,
        bool? favorite,
        CancellationToken cancellationToken = default)
    {
        var result = await _repository.UpdateTagAsync(tagId, new MemoryKeeperTagUpdateRequest
        {
            Revision = revision,
            Name = name is null ? null : RequireName(name),
            Favorite = favorite,
        }, cancellationToken).ConfigureAwait(false);
        InvalidateTags();
        return result;
    }

    public async Task DeleteTagAsync(
        int tagId,
        int revision,
        CancellationToken cancellationToken = default)
    {
        await _repository.DeleteTagAsync(tagId, revision, cancellationToken).ConfigureAwait(false);
        InvalidateTags();
    }

    public async Task<MemoryKeeperTagDto> MergeTagAsync(
        MemoryKeeperTagDto source,
        MemoryKeeperTagDto target,
        CancellationToken cancellationToken = default)
    {
        if (source.Id == target.Id)
        {
            throw new ArgumentException("서로 다른 태그를 선택하세요.");
        }

        var result = await _repository.MergeTagAsync(source.Id, new MemoryKeeperTagMergeRequest
        {
            SourceRevision = source.Revision,
            TargetTagId = target.Id,
            TargetRevision = target.Revision,
        }, cancellationToken).ConfigureAwait(false);
        InvalidateTags();
        return result;
    }

    public async Task<int> AssignFileTagAsync(
        Guid mediaId,
        int tagId,
        int expectedRevision,
        CancellationToken cancellationToken = default)
    {
        var result = await _repository.AssignFileTagAsync(
            FileId(mediaId), tagId, expectedRevision, cancellationToken).ConfigureAwait(false);
        InvalidateTags();
        return result.Revision;
    }

    public async Task<int> RestoreFileCatalogTagAsync(
        Guid mediaId,
        string identity,
        int expectedRevision,
        CancellationToken cancellationToken = default)
    {
        var result = await _repository.RestoreFileCatalogTagAsync(
            FileId(mediaId), RequireIdentity(identity), expectedRevision, cancellationToken).ConfigureAwait(false);
        InvalidateTags();
        return result.Revision;
    }

    public async Task<int> HideFileCatalogTagAsync(
        Guid mediaId,
        string identity,
        int expectedRevision,
        CancellationToken cancellationToken = default)
    {
        var result = await _repository.HideFileCatalogTagAsync(
            FileId(mediaId), RequireIdentity(identity), expectedRevision, cancellationToken).ConfigureAwait(false);
        InvalidateTags();
        return result.Revision;
    }

    public async Task<int> RemoveFileTagAsync(
        Guid mediaId,
        int tagId,
        int expectedRevision,
        CancellationToken cancellationToken = default)
    {
        var result = await _repository.RemoveFileTagAsync(
            FileId(mediaId), tagId, expectedRevision, cancellationToken).ConfigureAwait(false);
        InvalidateTags();
        return result.Revision;
    }

    public async Task<PendingMemoryOverviewDto> GetPendingMemoriesAsync(
        CancellationToken cancellationToken = default)
    {
        var pending = await _repository.GetPendingAsync(includeSuggestions: true, cancellationToken)
            .ConfigureAwait(false);
        _pendingRevisions.Clear();
        var mapped = pending.Items.Select(MapPending).ToList();
        foreach (var item in mapped)
        {
            _pendingRevisions[item.MediaId] = (item.BackendFileId, item.PlaceRevision);
        }

        return BuildPendingOverview(mapped, pending.Total, pending.Page, pending.PageSize);
    }

    public async Task<PendingMemoryOverviewDto> GetPlaceCleanupMemoriesAsync(
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var response = await _repository.GetPlaceCleanupAsync(page, pageSize, cancellationToken)
            .ConfigureAwait(false);

        if (page == 1)
        {
            _placeCleanupItems.Clear();
            _pendingRevisions.Clear();
        }

        foreach (var item in response.Items.Select(MapPending))
        {
            _placeCleanupItems[item.MediaId] = item;
            _pendingRevisions[item.MediaId] = (item.BackendFileId, item.PlaceRevision);
        }

        return BuildPendingOverview(
            _placeCleanupItems.Values.ToList(),
            response.Total,
            response.Page == 0 ? page : response.Page,
            response.PageSize == 0 ? pageSize : response.PageSize);
    }

    public Task<PlaceCleanupGroupListDto> GetPlaceCleanupGroupsAsync(
        int limit = 5,
        string? cursor = null,
        CancellationToken cancellationToken = default) =>
        _repository.GetPlaceCleanupGroupsAsync(Math.Clamp(limit, 1, 5), cursor, cancellationToken);

    public Task<CaptureDateCleanupGroupListDto> GetCaptureDateCleanupGroupsAsync(
        int limit = 5,
        string? cursor = null,
        CancellationToken cancellationToken = default) =>
        _repository.GetCaptureDateCleanupGroupsAsync(Math.Clamp(limit, 1, 5), cursor, cancellationToken);

    public async Task<(IReadOnlyList<PendingMemoryItemDto> Items, string? NextCursor, bool HasMore, int TotalPhotos)>
        GetPlaceCleanupGroupPhotosAsync(
            string groupId,
            int limit = 50,
            string? cursor = null,
            CancellationToken cancellationToken = default)
    {
        var response = await _repository.GetPlaceCleanupGroupPhotosAsync(
            groupId, limit, cursor, cancellationToken).ConfigureAwait(false);
        if (cursor is null)
        {
            _pendingRevisions.Clear();
        }

        var mapped = response.Items.Select(MapPending).ToList();
        foreach (var item in mapped)
        {
            _pendingRevisions[item.MediaId] = (item.BackendFileId, item.PlaceRevision);
        }

        return (mapped, response.NextCursor, response.HasMore, response.TotalPhotos);
    }

    public async Task<(IReadOnlyList<PendingMemoryItemDto> Items, string? NextCursor, bool HasMore, int TotalPhotos)>
        GetCaptureDateCleanupGroupPhotosAsync(
            string groupId,
            int limit = 50,
            string? cursor = null,
            CancellationToken cancellationToken = default)
    {
        var response = await _repository.GetCaptureDateCleanupGroupPhotosAsync(
            groupId, limit, cursor, cancellationToken).ConfigureAwait(false);
        return (response.Items.Select(MapPending).ToList(), response.NextCursor, response.HasMore, response.TotalPhotos);
    }

    public async Task<MemoryKeeperCaptureDateMutationResponse> SetCaptureDateAsync(
        IReadOnlyDictionary<Guid, int> expectedDateRevisions,
        DateOnly? userCaptureDate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedDateRevisions);
        if (expectedDateRevisions.Count is 0 or > 500)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedDateRevisions),
                "촬영일은 한 번에 1장부터 500장까지 변경할 수 있습니다.");
        }

        var revisions = expectedDateRevisions.ToDictionary(
            pair => FileId(pair.Key),
            pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
        if (revisions.Any(pair => pair.Value < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(expectedDateRevisions), "촬영일 revision은 0 이상이어야 합니다.");
        }

        var response = await _repository.SetCaptureDateAsync(
            new MemoryKeeperCaptureDateMutationRequest
            {
                FileIds = revisions.Keys.ToList(),
                UserCaptureDate = userCaptureDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                ExpectedDateRevisions = revisions,
            },
            cancellationToken).ConfigureAwait(false);
        _invalidation.Invalidate(CatalogSurface.AllRelated);
        return response;
    }

    private static PendingMemoryOverviewDto BuildPendingOverview(
        IReadOnlyList<PendingMemoryItemDto> mapped,
        int total,
        int page,
        int pageSize)
    {
        var withGps = mapped
            .Where(item => item.HasGps)
            .OrderByDescending(item => item.CapturedAt)
            .ToList();
        var groups = mapped
            .Where(item => !item.HasGps)
            .GroupBy(item => item.CapturedAt?.ToLocalTime().Date)
            .OrderByDescending(group => group.Key)
            .Select(MapPendingGroup)
            .ToList();
        return new PendingMemoryOverviewDto
        {
            Items = mapped,
            ReclassificationCandidates = withGps,
            Groups = groups,
            Total = total,
            Page = page,
            PageSize = pageSize,
        };
    }

    public async Task<AssignMediaPlaceResult> AssignPlaceAsync(
        AssignMediaPlaceRequest request,
        CancellationToken cancellationToken = default)
    {
        var selected = request.MediaIds.Distinct().ToList();
        var missing = selected.Where(id => !_pendingRevisions.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException("장소 정리 목록이 변경되었습니다. 목록을 새로 불러온 뒤 다시 시도하세요.");
        }

        var fileIds = selected.Select(id => _pendingRevisions[id].FileId).ToList();
        if (request.ExpectedPlaceRevisions is not null)
        {
            var missingRevisions = selected
                .Where(id => !request.ExpectedPlaceRevisions.ContainsKey(id))
                .ToList();
            if (missingRevisions.Count > 0)
            {
                throw new InvalidOperationException(
                    "자동 장소 분류 후 일부 사진의 최신 revision을 확인하지 못했습니다.");
            }
        }

        var revisions = selected.ToDictionary(
            id => _pendingRevisions[id].FileId,
            id => request.ExpectedPlaceRevisions is null
                ? _pendingRevisions[id].PlaceRevision
                : request.ExpectedPlaceRevisions[id],
            StringComparer.OrdinalIgnoreCase);
        var result = await _repository.AssignPendingPlaceAsync(new MemoryKeeperPendingAssignRequest
        {
            FileIds = fileIds,
            MemorykeeperPlaceId = request.PlaceId,
            ExpectedRevisions = revisions,
        }, cancellationToken).ConfigureAwait(false);
        var mediaIdByFileId = selected.ToDictionary(
            id => _pendingRevisions[id].FileId,
            id => id,
            StringComparer.OrdinalIgnoreCase);
        var updatedMediaIds = result.Items
            .Where(item => mediaIdByFileId.ContainsKey(item.FileId))
            .Select(item => mediaIdByFileId[item.FileId])
            .Distinct()
            .ToList();
        if (updatedMediaIds.Count == 0 && result.AssignedCount == selected.Count)
        {
            updatedMediaIds = selected;
        }

        foreach (var mediaId in updatedMediaIds)
        {
            _pendingRevisions.Remove(mediaId);
        }

        _invalidation.Invalidate();
        return new AssignMediaPlaceResult
        {
            PlaceId = request.PlaceId,
            UpdatedCount = result.AssignedCount,
            UpdatedMediaIds = updatedMediaIds,
        };
    }

    private static PendingMemoryItemDto MapPending(MemoryKeeperPendingItemDto item) => new()
    {
        BackendFileId = item.FileId,
        MediaId = BackendFileIdCodec.ToGuid(item.FileId),
        FileName = item.FileId,
        AbsoluteLibraryPath = item.ThumbnailUrl ?? string.Empty,
        CapturedAt = item.EffectiveCaptureDatetime ?? item.CaptureDatetime,
        RawCapturedAt = item.RawCaptureDatetime,
        UserCapturedAt = item.UserCaptureDatetime,
        UserCapturePrecision = item.UserCapturePrecision ?? string.Empty,
        EffectiveCaptureDate = item.EffectiveCaptureDate ?? string.Empty,
        EffectiveCaptureYear = item.EffectiveCaptureYear,
        DateBasis = item.DateBasis ?? string.Empty,
        DateCleanupRequired = item.DateCleanupRequired,
        DateCleanupReason = item.DateCleanupReason ?? string.Empty,
        DateRevision = item.DateRevision,
        Latitude = item.GpsLat,
        Longitude = item.GpsLon,
        Country = item.Country ?? string.Empty,
        Province = item.Province ?? string.Empty,
        City = item.City ?? string.Empty,
        District = item.District ?? string.Empty,
        RawPlaceName = item.PlaceName ?? string.Empty,
        MemorykeeperPlaceId = item.MemorykeeperPlaceId,
        PlaceRevision = item.PlaceRevision,
        SuggestedPlaceId = item.SuggestedPlaceId,
        SuggestedPlaceName = item.SuggestedPlaceName ?? string.Empty,
    };

    private static PendingMemoryGroupDto MapPendingGroup(IGrouping<DateTime?, PendingMemoryItemDto> group)
    {
        var items = group.ToList();
        var first = items[0];
        var dates = items.Where(item => item.CapturedAt.HasValue).Select(item => item.CapturedAt!.Value).OrderBy(date => date).ToList();
        return new PendingMemoryGroupDto
        {
            GroupId = first.MediaId,
            GroupName = group.Key?.ToString("yyyy년 M월 d일") ?? "날짜 미상",
            MediaCount = items.Count,
            HasUnknownDate = group.Key is null,
            FirstCapturedDate = dates.Count > 0 ? dates[0] : null,
            LastCapturedDate = dates.Count > 0 ? dates[^1] : null,
            EstimatedCountry = first.Country,
            EstimatedCity = first.City,
            EstimatedAddress = first.RawPlaceName,
            EstimatedLocationSummary = first.GeographyText,
            ProcessingStatus = "미처리",
            MediaItems = items,
        };
    }

    private void InvalidateTags() =>
        _invalidation.Invalidate(
            CatalogSurface.Gallery | CatalogSurface.Home | CatalogSurface.Travel | CatalogSurface.Tags);

    private static string RequireIdentity(string identity)
    {
        if (string.IsNullOrWhiteSpace(identity))
        {
            throw new ArgumentException("태그 identity가 필요합니다.", nameof(identity));
        }

        return identity.Trim();
    }

    private static IReadOnlySet<string> Fields(params string[] values) =>
        new HashSet<string>(values, StringComparer.Ordinal);

    private static string FileId(Guid mediaId) => BackendFileIdCodec.ToApiFileId(mediaId);

    private static string RequireName(string value)
    {
        var normalized = string.Join(" ", (value ?? string.Empty).Split(
            ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return string.IsNullOrWhiteSpace(normalized)
            ? throw new ArgumentException("태그 이름을 입력하세요.", nameof(value))
            : normalized;
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? AddSupplement(ISet<string> fields, string field, string? value)
    {
        var normalized = NullIfBlank(value);
        if (normalized is not null)
        {
            fields.Add(field);
        }

        return normalized;
    }

    private static string? FirstNotBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static void ValidateLength(string? value, int maxLength, string fieldName)
    {
        if (value?.Trim().Length > maxLength)
        {
            throw new ArgumentException($"{fieldName}은(는) {maxLength:N0}자까지 입력할 수 있습니다.");
        }
    }
}
