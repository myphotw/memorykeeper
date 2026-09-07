using MemoryKeeper.Application;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class PendingPlaceAssignmentOutcomeTests
{
    [Fact]
    public void AssignedCountZero_NeverReportsSuccess()
    {
        var outcome = Evaluate(requested: 5, assigned: 0, updatedIds: 0, finalMatched: 0);

        Assert.Equal(PendingPlaceAssignmentOutcomeKind.Failed, outcome.Kind);
        Assert.False(outcome.IsSuccess);
        Assert.Contains("등록되지 않았습니다", outcome.UserMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthoritativePartialState_ReportsActualCounts()
    {
        var outcome = Evaluate(requested: 5, assigned: 3, updatedIds: 3, finalMatched: 3);

        Assert.Equal(PendingPlaceAssignmentOutcomeKind.PartialSuccess, outcome.Kind);
        Assert.True(outcome.IsSuccess);
        Assert.Contains("5장 중 3장", outcome.UserMessage, StringComparison.Ordinal);
        Assert.Equal(2, outcome.FailedCount);
    }

    [Fact]
    public void AutomaticReclassificationUnassignDoesNotOverrideSuccessfulManualFinalState()
    {
        var outcome = Evaluate(
            requested: 5,
            assigned: 5,
            updatedIds: 5,
            finalMatched: 5,
            reclassUnassigned: 5);

        Assert.Equal(PendingPlaceAssignmentOutcomeKind.Success, outcome.Kind);
        Assert.True(outcome.FinalStateMatched);
        Assert.DoesNotContain("범위를 벗어나", outcome.UserMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void FinalMismatchWithoutConfirmedCause_DoesNotGuessRadiusFailure()
    {
        var outcome = Evaluate(requested: 5, assigned: 5, updatedIds: 5, finalMatched: 0);

        Assert.Equal(PendingPlaceAssignmentOutcomeKind.FinalStateMismatch, outcome.Kind);
        Assert.DoesNotContain("범위를 벗어나", outcome.UserMessage, StringComparison.Ordinal);
        Assert.Contains("최종 상태", outcome.UserMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void RadiusExpansionSuccess_ReportsBeforeAndAfterRadius()
    {
        var outcome = Evaluate(
            requested: 5,
            assigned: 5,
            updatedIds: 5,
            finalMatched: 5,
            radiusExpanded: true);

        Assert.Equal(PendingPlaceAssignmentOutcomeKind.Success, outcome.Kind);
        Assert.Contains("5장의 장소를 '집'으로 등록했습니다", outcome.UserMessage, StringComparison.Ordinal);
        Assert.Contains("100m에서 220m", outcome.UserMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void VerificationFailure_DoesNotReportSuccess()
    {
        var outcome = Evaluate(
            requested: 5,
            assigned: 5,
            updatedIds: 5,
            finalMatched: 4,
            verificationFailures: 1);

        Assert.Equal(PendingPlaceAssignmentOutcomeKind.FinalStateMismatch, outcome.Kind);
        Assert.False(outcome.IsSuccess);
        Assert.Contains("확인하지 못했습니다", outcome.UserMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthoritativeTargetPlaceMatchWinsOverLaggingCleanupList()
    {
        var outcome = PendingPlaceAssignmentOutcomeEvaluator.Evaluate(new PendingPlaceAssignmentVerification
        {
            RequestedCount = 1,
            AssignedCount = 1,
            UpdatedIdCount = 1,
            PostReloadWithPlaceIdCount = 1,
            PostReloadRemainingSelectedCount = 1,
            PlaceDisplayName = "집",
        });

        Assert.Equal(PendingPlaceAssignmentOutcomeKind.Success, outcome.Kind);
        Assert.True(outcome.IsSuccess);
        Assert.True(outcome.FinalStateMatched);
    }

    [Fact]
    public void ConfirmedConflicts_ReportExactPartialCount()
    {
        var outcome = Evaluate(
            requested: 5,
            assigned: 3,
            updatedIds: 3,
            finalMatched: 3,
            conflictCount: 2);

        Assert.Equal(PendingPlaceAssignmentOutcomeKind.PartialSuccess, outcome.Kind);
        Assert.Equal(2, outcome.ConflictCount);
        Assert.Contains("5장 중 3장", outcome.UserMessage, StringComparison.Ordinal);
        Assert.Contains("다른 변경이 먼저 반영되어 2장", outcome.UserMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void FinalPlaceMismatchDoesNotGuessConflictOrRadiusFailure()
    {
        var outcome = Evaluate(requested: 5, assigned: 5, updatedIds: 5, finalMatched: 3);

        Assert.Equal(PendingPlaceAssignmentOutcomeKind.PartialSuccess, outcome.Kind);
        Assert.DoesNotContain("다른 변경", outcome.UserMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("범위를 벗어나", outcome.UserMessage, StringComparison.Ordinal);
        Assert.Contains("예상한 최종 상태", outcome.UserMessage, StringComparison.Ordinal);
    }

    private static PendingPlaceAssignmentOutcome Evaluate(
        int requested,
        int assigned,
        int updatedIds,
        int finalMatched,
        int reclassUnassigned = 0,
        int verificationFailures = 0,
        bool radiusExpanded = false,
        int conflictCount = 0) =>
        PendingPlaceAssignmentOutcomeEvaluator.Evaluate(new PendingPlaceAssignmentVerification
        {
            RequestedCount = requested,
            AssignedCount = assigned,
            UpdatedIdCount = updatedIds,
            ConflictCount = conflictCount,
            ReclassUnassignedCount = reclassUnassigned,
            PostReloadWithPlaceIdCount = finalMatched,
            PostReloadRemainingSelectedCount = 0,
            FinalStateVerificationFailureCount = verificationFailures,
            PlaceDisplayName = "집",
            RadiusExpanded = radiusExpanded,
            CreatedNewPlace = false,
            PreviousRadiusMeters = 100,
            CurrentRadiusMeters = 220,
        });
}
