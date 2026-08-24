using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Infrastructure.Services.Api;

namespace MemoryKeeper.Infrastructure.Repositories.Api;

public sealed class MemoryKeeperOperationsApiRepository : IMemoryKeeperOperationsApiRepository
{
    private const string AutoTagRoot = "/api/memorykeeper/auto-tags";
    private const string ResetRoot = "/api/memorykeeper/reset";
    private readonly BaseApiClient _apiClient;

    public MemoryKeeperOperationsApiRepository(BaseApiClient apiClient) =>
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));

    public async Task<AutoTagStatusDto> GetAutoTagStatusAsync(CancellationToken cancellationToken = default) =>
        Require((await _apiClient.GetAsync<AutoTagStatusDto>(
            $"{AutoTagRoot}/status", cancellationToken).ConfigureAwait(false)).Data,
            "자동 태그 상태 응답이 비어 있습니다.");

    public async Task<AutoTagFailedJobListDto> GetFailedAutoTagsAsync(
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        Require((await _apiClient.GetAsync<AutoTagFailedJobListDto>(
            $"{AutoTagRoot}/failed?page={page}&page_size={pageSize}", cancellationToken).ConfigureAwait(false)).Data,
            "자동 태그 실패 목록 응답이 비어 있습니다.");

    public async Task<AutoTagRetryResultDto> RetryFailedAutoTagsAsync(
        int limit = 100,
        CancellationToken cancellationToken = default) =>
        Require((await _apiClient.PostAsync<AutoTagRetryResultDto>(
            $"{AutoTagRoot}/retry-failed?limit={limit}", body: null, cancellationToken).ConfigureAwait(false)).Data,
            "자동 태그 재시도 응답이 비어 있습니다.");

    public async Task<AutoTagRetryResultDto> RetryAutoTagJobAsync(
        int jobId,
        CancellationToken cancellationToken = default) =>
        Require((await _apiClient.PostAsync<AutoTagRetryResultDto>(
            $"{AutoTagRoot}/jobs/{jobId}/retry", body: null, cancellationToken).ConfigureAwait(false)).Data,
            "자동 태그 재시도 응답이 비어 있습니다.");

    public async Task<MemoryKeeperResetPreviewDto> PreviewResetAsync(CancellationToken cancellationToken = default) =>
        Require((await _apiClient.PostAsync<MemoryKeeperResetPreviewDto>(
            $"{ResetRoot}/preview", body: null, cancellationToken).ConfigureAwait(false)).Data,
            "초기화 미리보기 응답이 비어 있습니다.");

    public async Task<MemoryKeeperResetExecuteResultDto> ExecuteResetAsync(
        MemoryKeeperResetExecuteRequest request,
        CancellationToken cancellationToken = default) =>
        Require((await _apiClient.PostAsync<MemoryKeeperResetExecuteResultDto>(
            $"{ResetRoot}/execute", request, cancellationToken).ConfigureAwait(false)).Data,
            "초기화 실행 응답이 비어 있습니다.");

    private static T Require<T>(T? value, string message) where T : class =>
        value ?? throw new InvalidOperationException(message);
}
