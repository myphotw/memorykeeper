using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Application.Services;

public enum GalleryPlaceAssignmentStage
{
    InitialStateQuery,
    RadiusUpdate,
    RevisionRefresh,
    Assign,
    Invalidate,
    VerifyQuery,
    VerifyMatch,
    Completed,
}

public sealed record GalleryPlaceAssignmentDiagnosticSnapshot(
    GalleryPlaceAssignmentStage Stage,
    int SelectedCount,
    int? ReturnedCount = null,
    int? RevisionMapCount = null,
    int? VerifiedCount = null,
    int? MismatchCount = null);

public sealed class GalleryPlaceAssignmentResult
{
    public required Guid TargetPlaceId { get; init; }
    public required int RequestedCount { get; init; }
    public required IReadOnlyList<MemoryKeeperFilePlaceStateDto> FinalStates { get; init; }

    public bool IsVerified =>
        FinalStates.Count == RequestedCount
        && FinalStates.All(item => item.MemorykeeperPlaceId == TargetPlaceId);
}

/// <summary>
/// Shared application workflow for arbitrary MemoryKeeper file batches. It deliberately
/// changes only the registered Place relation; raw location metadata is never patched.
/// </summary>
public sealed class GalleryPlaceAssignmentWorkflow
{
    public const int MaximumBatchSize = 500;

    private readonly IMemoryKeeperWriteApiRepository _repository;
    private readonly ICatalogInvalidation _invalidation;
    private readonly ILogger<GalleryPlaceAssignmentWorkflow>? _logger;

    public GalleryPlaceAssignmentWorkflow(
        IMemoryKeeperWriteApiRepository repository,
        ICatalogInvalidation invalidation,
        ILogger<GalleryPlaceAssignmentWorkflow>? logger = null)
    {
        _repository = repository;
        _invalidation = invalidation;
        _logger = logger;
    }

    public Task<MemoryKeeperFilePlaceStateQueryResponse> QueryStatesAsync(
        IReadOnlyCollection<string> fileIds,
        CancellationToken cancellationToken = default)
    {
        var ids = ValidateFileIds(fileIds);
        return _repository.QueryFilePlaceStatesAsync(
            new MemoryKeeperFilePlaceStateQueryRequest { FileIds = ids },
            cancellationToken);
    }

    public PlaceRadiusExpansionPlan PlanRadius(
        double centerLatitude,
        double centerLongitude,
        double currentRadiusMeters,
        IReadOnlyCollection<PlaceRadiusPhotoSelection> selections) =>
        PlaceRadiusExpansionPlanner.Create(
            centerLatitude,
            centerLongitude,
            currentRadiusMeters,
            selections);

    public async Task<PlaceDto?> ExpandExistingRadiusAsync(
        MemoryKeeperPlaceService placeService,
        PlaceDto place,
        double radiusMeters,
        Func<MemoryKeeperRadiusImpactApiResult, CancellationToken, Task<bool>> confirmOverlapAsync,
        CancellationToken cancellationToken = default)
    {
        var operation = await placeService.UpdateWithRadiusImpactAsync(
            place,
            CopyWithRadius(place, radiusMeters),
            confirmOverlapAsync,
            reassignFromOtherPlaces: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return operation.Cancelled ? null : operation.UpdatedPlace;
    }

    public Task<PlaceDto> CreateTargetPlaceAsync(
        MemoryKeeperPlaceService placeService,
        CreatePlaceRequest request,
        CancellationToken cancellationToken = default) =>
        placeService.CreatePlaceAsync(CopyWithoutAutomaticReclassification(request), cancellationToken);

    public async Task<GalleryPlaceAssignmentResult> AssignAsync(
        IReadOnlyCollection<string> fileIds,
        Guid targetPlaceId,
        Action<GalleryPlaceAssignmentStage>? reportStage = null,
        CancellationToken cancellationToken = default,
        Action<GalleryPlaceAssignmentDiagnosticSnapshot>? reportDiagnostic = null)
    {
        if (targetPlaceId == Guid.Empty)
        {
            throw new ArgumentException("대상 장소가 필요합니다.", nameof(targetPlaceId));
        }

        var ids = ValidateFileIds(fileIds);
        var stage = GalleryPlaceAssignmentStage.RevisionRefresh;
        try
        {
            reportStage?.Invoke(stage);
            reportDiagnostic?.Invoke(new GalleryPlaceAssignmentDiagnosticSnapshot(stage, ids.Count));
            var latest = await QueryStatesAsync(ids, cancellationToken).ConfigureAwait(false);
            reportDiagnostic?.Invoke(new GalleryPlaceAssignmentDiagnosticSnapshot(
                stage,
                ids.Count,
                ReturnedCount: latest.Items.Count));
            EnsureCompleteSnapshot(ids, latest.Items);
            var revisions = latest.Items.ToDictionary(
                item => item.FileId,
                item => item.PlaceMatchRevision,
                StringComparer.Ordinal);
            var diagnostic = new GalleryPlaceAssignmentDiagnosticSnapshot(
                stage,
                ids.Count,
                ReturnedCount: latest.Items.Count,
                RevisionMapCount: revisions.Count);
            reportDiagnostic?.Invoke(diagnostic);
            LogStageCompleted(stage, ids.Count, latest.Items.Count, targetPlaceId, revisions.Count);

            stage = GalleryPlaceAssignmentStage.Assign;
            reportStage?.Invoke(stage);
            diagnostic = diagnostic with { Stage = stage };
            reportDiagnostic?.Invoke(diagnostic);
            await _repository.AssignFilePlacesAsync(new MemoryKeeperFilesAssignPlaceRequest
            {
                FileIds = ids,
                MemorykeeperPlaceId = targetPlaceId,
                ExpectedPlaceRevisions = revisions,
            }, cancellationToken).ConfigureAwait(false);
            LogStageCompleted(stage, ids.Count, returnedCount: null, targetPlaceId, revisions.Count);

            stage = GalleryPlaceAssignmentStage.Invalidate;
            reportStage?.Invoke(stage);
            diagnostic = diagnostic with { Stage = stage };
            reportDiagnostic?.Invoke(diagnostic);
            _invalidation.Invalidate(CatalogSurface.AllRelated);
            LogStageCompleted(stage, ids.Count, returnedCount: null, targetPlaceId, revisions.Count);

            stage = GalleryPlaceAssignmentStage.VerifyQuery;
            reportStage?.Invoke(stage);
            diagnostic = diagnostic with
            {
                Stage = stage,
                ReturnedCount = null,
                VerifiedCount = null,
                MismatchCount = null,
            };
            reportDiagnostic?.Invoke(diagnostic);
            var final = await QueryStatesAsync(ids, cancellationToken).ConfigureAwait(false);
            diagnostic = diagnostic with { ReturnedCount = final.Items.Count };
            reportDiagnostic?.Invoke(diagnostic);
            LogStageCompleted(stage, ids.Count, final.Items.Count, targetPlaceId, revisions.Count);

            stage = GalleryPlaceAssignmentStage.VerifyMatch;
            reportStage?.Invoke(stage);
            diagnostic = diagnostic with { Stage = stage };
            reportDiagnostic?.Invoke(diagnostic);
            EnsureCompleteSnapshot(ids, final.Items);
            var verifiedCount = final.Items.Count(item => item.MemorykeeperPlaceId == targetPlaceId);
            var mismatchCount = final.Items.Count(item => item.MemorykeeperPlaceId != targetPlaceId)
                + Math.Abs(ids.Count - final.Items.Count);
            diagnostic = diagnostic with
            {
                VerifiedCount = verifiedCount,
                MismatchCount = mismatchCount,
            };
            reportDiagnostic?.Invoke(diagnostic);
            var result = new GalleryPlaceAssignmentResult
            {
                TargetPlaceId = targetPlaceId,
                RequestedCount = ids.Count,
                FinalStates = final.Items,
            };
            if (!result.IsVerified)
            {
                _logger?.LogWarning(
                    "Gallery place edit verification mismatch. Stage={Stage} SelectedCount={SelectedCount} ReturnedCount={ReturnedCount} VerifiedCount={VerifiedCount} MismatchCount={MismatchCount} TargetPlaceId={TargetPlaceId}",
                    GetDiagnosticStageName(stage),
                    ids.Count,
                    final.Items.Count,
                    verifiedCount,
                    mismatchCount,
                    targetPlaceId);
                throw new InvalidOperationException("일부 사진의 최종 장소 상태를 확인하지 못했습니다.");
            }

            _logger?.LogInformation(
                "Gallery place edit stage completed. Stage={Stage} SelectedCount={SelectedCount} ReturnedCount={ReturnedCount} VerifiedCount={VerifiedCount} MismatchCount={MismatchCount} TargetPlaceId={TargetPlaceId}",
                GetDiagnosticStageName(stage),
                ids.Count,
                final.Items.Count,
                verifiedCount,
                mismatchCount,
                targetPlaceId);

            stage = GalleryPlaceAssignmentStage.Completed;
            reportStage?.Invoke(stage);
            diagnostic = diagnostic with { Stage = stage };
            reportDiagnostic?.Invoke(diagnostic);
            LogStageCompleted(stage, ids.Count, final.Items.Count, targetPlaceId, revisions.Count);
            return result;
        }
        catch (Exception ex)
        {
            _logger?.LogError(
                "Gallery place edit stage failed. Stage={Stage} SelectedCount={SelectedCount} TargetPlaceId={TargetPlaceId} ExceptionType={ExceptionType}",
                GetDiagnosticStageName(stage),
                ids.Count,
                targetPlaceId,
                ex.GetType().Name);
            throw;
        }
    }

    public static string GetDiagnosticStageName(GalleryPlaceAssignmentStage stage) => stage switch
    {
        GalleryPlaceAssignmentStage.InitialStateQuery => "initial-state-query",
        GalleryPlaceAssignmentStage.RadiusUpdate => "radius-update",
        GalleryPlaceAssignmentStage.RevisionRefresh => "revision-refresh",
        GalleryPlaceAssignmentStage.Assign => "assign",
        GalleryPlaceAssignmentStage.Invalidate => "invalidate",
        GalleryPlaceAssignmentStage.VerifyQuery => "verify-query",
        GalleryPlaceAssignmentStage.VerifyMatch => "verify-match",
        GalleryPlaceAssignmentStage.Completed => "completed",
        _ => "unknown",
    };

    private void LogStageCompleted(
        GalleryPlaceAssignmentStage stage,
        int selectedCount,
        int? returnedCount,
        Guid targetPlaceId,
        int revisionMapCount) =>
        _logger?.LogInformation(
            "Gallery place edit stage completed. Stage={Stage} SelectedCount={SelectedCount} ReturnedCount={ReturnedCount} RevisionMapCount={RevisionMapCount} TargetPlaceId={TargetPlaceId}",
            GetDiagnosticStageName(stage),
            selectedCount,
            returnedCount,
            revisionMapCount,
            targetPlaceId);

    private static IReadOnlyList<string> ValidateFileIds(IReadOnlyCollection<string> fileIds)
    {
        ArgumentNullException.ThrowIfNull(fileIds);
        var ids = fileIds
            .Select(id => id?.Trim() ?? string.Empty)
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (ids.Count == 0)
        {
            throw new ArgumentException("선택한 사진이 없습니다.", nameof(fileIds));
        }
        if (ids.Count > MaximumBatchSize)
        {
            throw new ArgumentOutOfRangeException(nameof(fileIds), $"한 번에 최대 {MaximumBatchSize}장까지 변경할 수 있습니다.");
        }
        return ids;
    }

    private static void EnsureCompleteSnapshot(
        IReadOnlyCollection<string> requestedIds,
        IReadOnlyCollection<MemoryKeeperFilePlaceStateDto> states)
    {
        var returned = states.Select(item => item.FileId).ToHashSet(StringComparer.Ordinal);
        if (returned.Count != requestedIds.Count || requestedIds.Any(id => !returned.Contains(id)))
        {
            throw new InvalidOperationException("선택한 사진의 최신 장소 상태를 모두 확인하지 못했습니다.");
        }
    }

    private static UpdatePlaceRequest CopyWithRadius(PlaceDto place, double radius) => new()
    {
        Id = place.Id,
        Revision = place.Revision,
        DisplayName = place.DisplayName,
        CanonicalName = place.CanonicalName,
        Country = place.Country,
        Province = place.Province,
        City = place.City,
        District = place.District,
        Address = place.Address,
        PostalCode = place.PostalCode,
        GooglePlaceId = place.GooglePlaceId,
        Category = place.Category,
        Latitude = place.Latitude,
        Longitude = place.Longitude,
        Radius = radius,
        IsActive = place.IsActive,
        IsFavorite = place.IsFavorite,
        ReclassifyMedia = true,
        ReassignFromOtherPlaces = false,
    };

    private static CreatePlaceRequest CopyWithoutAutomaticReclassification(CreatePlaceRequest request) => new()
    {
        DisplayName = request.DisplayName,
        CanonicalName = request.CanonicalName,
        Country = request.Country,
        Province = request.Province,
        City = request.City,
        District = request.District,
        Address = request.Address,
        PostalCode = request.PostalCode,
        GooglePlaceId = request.GooglePlaceId,
        Category = request.Category,
        Latitude = request.Latitude,
        Longitude = request.Longitude,
        Radius = request.Radius,
        IsActive = request.IsActive,
        IsFavorite = request.IsFavorite,
        ReclassifyMedia = false,
        ReassignFromOtherPlaces = false,
    };
}
