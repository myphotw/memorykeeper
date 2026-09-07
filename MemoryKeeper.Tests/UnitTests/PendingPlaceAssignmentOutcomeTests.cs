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
    public void ReclassificationUnassignWithNoFinalMatches_ReportsConfirmedRadiusFailure()
    {
        var outcome = Evaluate(
            requested: 5,
            assigned: 5,
            updatedIds: 5,
            finalMatched: 0,
            reclassUnassigned: 5);

        Assert.Equal(PendingPlaceAssignmentOutcomeKind.RevertedByPostProcessing, outcome.Kind);
        Assert.Contains("범위를 벗어나", outcome.UserMessage, StringComparison.Ordinal);
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
    public void CleanupItemStillPresent_DoesNotReportSuccessEvenWhenPlaceIdMatches()
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

        Assert.Equal(PendingPlaceAssignmentOutcomeKind.FinalStateMismatch, outcome.Kind);
        Assert.False(outcome.IsSuccess);
        Assert.Contains("장소 정리 목록에 남아", outcome.UserMessage, StringComparison.Ordinal);
    }

    private static PendingPlaceAssignmentOutcome Evaluate(
        int requested,
        int assigned,
        int updatedIds,
        int finalMatched,
        int reclassUnassigned = 0,
        int verificationFailures = 0,
        bool radiusExpanded = false) =>
        PendingPlaceAssignmentOutcomeEvaluator.Evaluate(new PendingPlaceAssignmentVerification
        {
            RequestedCount = requested,
            AssignedCount = assigned,
            UpdatedIdCount = updatedIds,
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
