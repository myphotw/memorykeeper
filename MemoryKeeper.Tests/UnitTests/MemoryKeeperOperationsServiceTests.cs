using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class MemoryKeeperOperationsServiceTests
{
    [Fact]
    public async Task Status_MapsNormalLimitAndAttentionWithoutHardcodedQuota()
    {
        var repository = new FakeRepository();
        var service = new MemoryKeeperOperationsService(repository, new CatalogInvalidation());

        repository.Status = Status(limit: 731, usage: 100);
        var normal = await service.GetAutoTagStatusAsync();
        repository.Status = Status(limit: 731, usage: 731, monthlyLimitReached: true, quotaWaiting: 5);
        var limited = await service.GetAutoTagStatusAsync();
        repository.Status = Status(limit: 731, usage: 10, serviceAvailable: false);
        var attention = await service.GetAutoTagStatusAsync();

        Assert.Equal(AutoTagUserState.Normal, normal.State);
        Assert.Contains("731", normal.Summary);
        Assert.Equal(AutoTagUserState.MonthlyLimitReached, limited.State);
        Assert.Contains("다음 달", limited.QuotaWaitingText);
        Assert.Equal(AutoTagUserState.AttentionRequired, attention.State);
    }

    [Fact]
    public async Task Retry_UsesSafeViewModelAndDoesNotExposeRawErrorCode()
    {
        var repository = new FakeRepository
        {
            Failed = new AutoTagFailedJobListDto
            {
                Items = [new AutoTagFailedJobDto
                {
                    JobId = 3,
                    FileId = "file",
                    SafeErrorCode = "INTERNAL_PROVIDER_DETAIL",
                    Retryable = true,
                }],
            },
        };
        var service = new MemoryKeeperOperationsService(repository, new CatalogInvalidation());

        var item = Assert.Single(await service.GetFailedAutoTagsAsync());
        await service.RetryFailedAutoTagsAsync();
        await service.RetryAutoTagJobAsync(item.JobId);

        Assert.DoesNotContain("INTERNAL", item.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, repository.BulkRetryCount);
        Assert.Equal(3, repository.IndividualRetryJobId);
    }

    [Theory]
    [InlineData("초기화")]
    [InlineData("다시 시작")]
    public async Task Reset_RequiresUserConfirmationAndInvalidatesEverySurface(string confirmation)
    {
        var invalidation = new CatalogInvalidation();
        var repository = new FakeRepository();
        var service = new MemoryKeeperOperationsService(repository, invalidation);

        var result = await service.ExecuteResetAsync(confirmation);

        Assert.True(result.ResetCompleted);
        Assert.Equal("RESET_MEMORYKEEPER", repository.ResetRequest!.Confirmation);
        foreach (var surface in new[]
                 {
                     CatalogSurface.Gallery, CatalogSurface.Home, CatalogSurface.Visits,
                     CatalogSurface.Travel, CatalogSurface.Pending, CatalogSurface.Tags,
                     CatalogSurface.Places, CatalogSurface.Favorites,
                 })
        {
            Assert.True(invalidation.Consume(surface));
        }
    }

    [Fact]
    public async Task Reset_RejectsMissingConfirmationBeforeApiCall()
    {
        var repository = new FakeRepository();
        var service = new MemoryKeeperOperationsService(repository, new CatalogInvalidation());

        await Assert.ThrowsAsync<ArgumentException>(() => service.ExecuteResetAsync("yes"));

        Assert.Null(repository.ResetRequest);
    }

    [Fact]
    public void ResetPreviewSummary_ExplainsOriginalAndAstroPreservationWithoutTechnicalTerms()
    {
        var summary = MemoryKeeperOperationsService.BuildResetPreviewSummary(new MemoryKeeperResetPreviewDto
        {
            MemorykeeperFileCount = 10,
            PlaceCount = 2,
            FavoriteCount = 3,
            MemoCount = 4,
            UserTagCount = 5,
        });

        Assert.Contains("원본 사진", summary);
        Assert.Contains("AstroJournal 데이터", summary);
        Assert.Contains("자동 등록되지 않습니다", summary);
        Assert.DoesNotContain("database", summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("API", summary, StringComparison.OrdinalIgnoreCase);
    }

    private static AutoTagStatusDto Status(
        int limit,
        int usage,
        bool monthlyLimitReached = false,
        int quotaWaiting = 0,
        bool serviceAvailable = true) => new()
    {
        ServiceAvailable = serviceAvailable,
        CredentialReady = true,
        WorkerOnline = true,
        QuotaAvailable = !monthlyLimitReached,
        MonthlyLimitReached = monthlyLimitReached,
        QuotaWaitingCount = quotaWaiting,
        MonthlyLimit = limit,
        MonthlyUsage = usage,
        MonthlyRemaining = Math.Max(0, limit - usage),
    };

    private sealed class FakeRepository : IMemoryKeeperOperationsApiRepository
    {
        public AutoTagStatusDto Status { get; set; } = MemoryKeeperOperationsServiceTests.Status(1, 0);
        public AutoTagFailedJobListDto Failed { get; set; } = new();
        public int BulkRetryCount { get; private set; }
        public int? IndividualRetryJobId { get; private set; }
        public MemoryKeeperResetExecuteRequest? ResetRequest { get; private set; }

        public Task<AutoTagStatusDto> GetAutoTagStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(Status);
        public Task<AutoTagFailedJobListDto> GetFailedAutoTagsAsync(int page = 1, int pageSize = 50, CancellationToken cancellationToken = default) => Task.FromResult(Failed);
        public Task<AutoTagRetryResultDto> RetryFailedAutoTagsAsync(int limit = 100, CancellationToken cancellationToken = default)
        {
            BulkRetryCount++;
            return Task.FromResult(new AutoTagRetryResultDto { RequestedCount = 1, RequeuedCount = 1 });
        }
        public Task<AutoTagRetryResultDto> RetryAutoTagJobAsync(int jobId, CancellationToken cancellationToken = default)
        {
            IndividualRetryJobId = jobId;
            return Task.FromResult(new AutoTagRetryResultDto { RequestedCount = 1, RequeuedCount = 1 });
        }
        public Task<MemoryKeeperResetPreviewDto> PreviewResetAsync(CancellationToken cancellationToken = default) => Task.FromResult(new MemoryKeeperResetPreviewDto());
        public Task<MemoryKeeperResetExecuteResultDto> ExecuteResetAsync(MemoryKeeperResetExecuteRequest request, CancellationToken cancellationToken = default)
        {
            ResetRequest = request;
            return Task.FromResult(new MemoryKeeperResetExecuteResultDto { ResetCompleted = true });
        }
    }
}
