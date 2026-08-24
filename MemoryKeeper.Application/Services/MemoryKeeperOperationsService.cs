using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;

namespace MemoryKeeper.Application.Services;

public sealed class MemoryKeeperOperationsService
{
    private const string BackendResetConfirmation = "RESET_MEMORYKEEPER";
    private readonly IMemoryKeeperOperationsApiRepository _repository;
    private readonly ICatalogInvalidation _invalidation;

    public MemoryKeeperOperationsService(
        IMemoryKeeperOperationsApiRepository repository,
        ICatalogInvalidation invalidation)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _invalidation = invalidation ?? throw new ArgumentNullException(nameof(invalidation));
    }

    public async Task<AutoTagUserStatusDto> GetAutoTagStatusAsync(CancellationToken cancellationToken = default)
    {
        var status = await _repository.GetAutoTagStatusAsync(cancellationToken).ConfigureAwait(false);
        var state = status.MonthlyLimitReached
            ? AutoTagUserState.MonthlyLimitReached
            : !status.ServiceAvailable || !status.CredentialReady || !status.WorkerOnline
                ? AutoTagUserState.AttentionRequired
                : AutoTagUserState.Normal;
        var stateText = state switch
        {
            AutoTagUserState.Normal => "정상",
            AutoTagUserState.MonthlyLimitReached => "이번 달 분석량 사용 완료",
            _ => "점검 필요",
        };
        var summary = state switch
        {
            AutoTagUserState.Normal =>
                $"이번 달 {status.MonthlyUsage:N0}/{status.MonthlyLimit:N0}장, 남은 분석량 {status.MonthlyRemaining:N0}장",
            AutoTagUserState.MonthlyLimitReached =>
                "이번 달 무료 분석량을 모두 사용했습니다. 다음 달에 자동으로 다시 시작합니다.",
            _ => "자동 태그 기능을 잠시 사용할 수 없습니다. 잠시 후 다시 확인해 주세요.",
        };

        return new AutoTagUserStatusDto
        {
            Status = status,
            State = state,
            StateText = stateText,
            Summary = summary,
            QuotaWaitingText = status.QuotaWaitingCount > 0
                ? $"다음 달 분석을 기다리는 사진 {status.QuotaWaitingCount:N0}장"
                : string.Empty,
        };
    }

    public async Task<IReadOnlyList<AutoTagFailedItemViewDto>> GetFailedAutoTagsAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await _repository.GetFailedAutoTagsAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return response.Items.Select(item => new AutoTagFailedItemViewDto
        {
            JobId = item.JobId,
            FileId = item.FileId,
            FailedAt = item.FailedAt,
            RetryCount = item.RetryCount,
            Retryable = item.Retryable,
        }).ToList();
    }

    public Task<AutoTagRetryResultDto> RetryFailedAutoTagsAsync(
        int limit = 100,
        CancellationToken cancellationToken = default) =>
        _repository.RetryFailedAutoTagsAsync(limit, cancellationToken);

    public Task<AutoTagRetryResultDto> RetryAutoTagJobAsync(
        int jobId,
        CancellationToken cancellationToken = default) =>
        _repository.RetryAutoTagJobAsync(jobId, cancellationToken);

    public Task<MemoryKeeperResetPreviewDto> PreviewResetAsync(CancellationToken cancellationToken = default) =>
        _repository.PreviewResetAsync(cancellationToken);

    public async Task<MemoryKeeperResetExecuteResultDto> ExecuteResetAsync(
        string confirmationText,
        CancellationToken cancellationToken = default)
    {
        if (!IsUserConfirmationValid(confirmationText))
        {
            throw new ArgumentException("확인 문구를 정확히 입력해 주세요.", nameof(confirmationText));
        }

        var result = await _repository.ExecuteResetAsync(
            new MemoryKeeperResetExecuteRequest { Confirmation = BackendResetConfirmation },
            cancellationToken).ConfigureAwait(false);
        if (result.ResetCompleted)
        {
            _invalidation.Invalidate(CatalogSurface.AllMemoryKeeper);
        }

        return result;
    }

    public static bool IsUserConfirmationValid(string? value) =>
        string.Equals(value?.Trim(), "초기화", StringComparison.Ordinal)
        || string.Equals(value?.Trim(), "다시 시작", StringComparison.Ordinal);

    public static string BuildResetPreviewSummary(MemoryKeeperResetPreviewDto preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        return
            $"MemoryKeeper를 처음부터 다시 구성할까요?\n\n" +
            "초기화되는 항목\n" +
            $"• 사진 등록/정리 결과 {preview.MemorykeeperFileCount:N0}장\n" +
            $"• 장소 {preview.PlaceCount:N0}개\n" +
            $"• 즐겨찾기 {preview.FavoriteCount:N0}개, 메모 {preview.MemoCount:N0}개\n" +
            $"• 사용자 태그 {preview.UserTagCount:N0}개\n\n" +
            "보존되는 항목\n" +
            "• 원본 사진\n" +
            "• AstroJournal 데이터\n" +
            "• 재사용 가능한 사진 분석 결과\n\n" +
            "완료 후 사진은 자동 등록되지 않습니다.";
    }
}
