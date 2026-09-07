namespace MemoryKeeper.Application;

public enum PendingPlaceAssignmentOutcomeKind
{
    Success,
    PartialSuccess,
    Failed,
    RevertedByPostProcessing,
    FinalStateMismatch,
}

/// <summary>
/// Interprets a cleanup-place mutation from its authoritative final state rather than
/// treating the write response by itself as success.
/// </summary>
public static class PendingPlaceAssignmentOutcomeEvaluator
{
    public static PendingPlaceAssignmentOutcome Evaluate(PendingPlaceAssignmentVerification value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var requested = Math.Max(0, value.RequestedCount);
        var finalMatched = Math.Clamp(value.PostReloadWithPlaceIdCount, 0, requested);

        if (requested > 0 && value.AssignedCount == 0 && value.UpdatedIdCount == 0)
        {
            var message = value.CreatedNewPlace
                ? $"새 장소 '{value.PlaceDisplayName}'는 생성되었지만 선택한 사진의 장소는 등록되지 않았습니다. 최신 목록을 다시 불러왔습니다."
                : value.RadiusExpanded
                ? $"'{value.PlaceDisplayName}'의 장소 범위는 {value.PreviousRadiusMeters:0}m에서 {value.CurrentRadiusMeters:0}m로 변경되었지만 선택한 사진의 장소는 등록되지 않았습니다. 최신 목록을 다시 불러왔습니다."
                : "선택한 사진의 장소가 등록되지 않았습니다.";
            return Outcome(
                PendingPlaceAssignmentOutcomeKind.Failed,
                finalMatched,
                requested,
                message);
        }

        if (value.FinalStateVerificationFailureCount > 0)
        {
            return Outcome(
                PendingPlaceAssignmentOutcomeKind.FinalStateMismatch,
                finalMatched,
                requested,
                "요청은 처리되었지만 일부 사진의 최종 상태를 확인하지 못했습니다. 최신 목록을 다시 불러왔습니다.");
        }

        if (requested > 0
            && finalMatched == requested
            && value.PostReloadRemainingSelectedCount == 0)
        {
            var message = value.RadiusExpanded
                ? $"'{value.PlaceDisplayName}'의 장소 범위를 {value.PreviousRadiusMeters:0}m에서 {value.CurrentRadiusMeters:0}m로 확장하고 {requested}장의 장소를 등록했습니다."
                : $"{requested}장의 장소를 '{value.PlaceDisplayName}'으로 등록했습니다.";
            return Outcome(PendingPlaceAssignmentOutcomeKind.Success, finalMatched, requested, message);
        }

        if (requested > 0
            && finalMatched == requested
            && value.PostReloadRemainingSelectedCount > 0)
        {
            return Outcome(
                PendingPlaceAssignmentOutcomeKind.FinalStateMismatch,
                finalMatched,
                requested,
                "요청은 처리되었지만 일부 사진이 장소 정리 목록에 남아 있습니다. 최신 목록을 다시 불러왔습니다.");
        }

        if (finalMatched > 0)
        {
            return Outcome(
                PendingPlaceAssignmentOutcomeKind.PartialSuccess,
                finalMatched,
                requested,
                $"{requested}장 중 {finalMatched}장의 장소를 등록했습니다. {requested - finalMatched}장은 처리되지 않았습니다.");
        }

        if (value.ReclassUnassignedCount > 0)
        {
            return Outcome(
                PendingPlaceAssignmentOutcomeKind.RevertedByPostProcessing,
                0,
                requested,
                "선택한 사진의 위치가 등록 장소 범위를 벗어나 장소 등록이 유지되지 않았습니다.");
        }

        return Outcome(
            PendingPlaceAssignmentOutcomeKind.FinalStateMismatch,
            finalMatched,
            requested,
            "요청은 처리되었지만 일부 사진이 예상한 최종 상태로 반영되지 않았습니다. 최신 목록을 다시 불러왔습니다.");
    }

    private static PendingPlaceAssignmentOutcome Outcome(
        PendingPlaceAssignmentOutcomeKind kind,
        int succeeded,
        int requested,
        string message) => new()
        {
            Kind = kind,
            SucceededCount = succeeded,
            FailedCount = Math.Max(0, requested - succeeded),
            UserMessage = message,
        };
}

public sealed class PendingPlaceAssignmentVerification
{
    public int RequestedCount { get; init; }

    public int AssignedCount { get; init; }

    public int UpdatedIdCount { get; init; }

    public int ReclassUnassignedCount { get; init; }

    public int PostReloadWithPlaceIdCount { get; init; }

    public int PostReloadRemainingSelectedCount { get; init; }

    public int FinalStateVerificationFailureCount { get; init; }

    public required string PlaceDisplayName { get; init; }

    public bool RadiusExpanded { get; init; }

    public bool CreatedNewPlace { get; init; }

    public double PreviousRadiusMeters { get; init; }

    public double CurrentRadiusMeters { get; init; }
}

public sealed class PendingPlaceAssignmentOutcome
{
    public PendingPlaceAssignmentOutcomeKind Kind { get; init; }

    public int SucceededCount { get; init; }

    public int FailedCount { get; init; }

    public string UserMessage { get; init; } = string.Empty;

    public bool IsSuccess => Kind is PendingPlaceAssignmentOutcomeKind.Success
        or PendingPlaceAssignmentOutcomeKind.PartialSuccess;
}
