using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class RecentSearchQueryServiceTests
{
    [Fact]
    public async Task AddAsync_DeduplicatesMovesToFrontAndKeepsTen()
    {
        var repository = new FakeSettingRepository();
        var service = new RecentSearchQueryService(
            repository,
            NullLogger<RecentSearchQueryService>.Instance);

        for (var index = 0; index < 12; index++)
        {
            await service.AddAsync($"검색 {index}");
        }
        await service.AddAsync("검색 5");

        var result = await service.GetAsync();

        Assert.Equal(RecentSearchQueryService.MaxRecentQueries, result.Count);
        Assert.Equal("검색 5", result[0]);
        Assert.Single(result, item => item == "검색 5");
        Assert.DoesNotContain("검색 0", result);
    }

    private sealed class FakeSettingRepository : ISettingRepository
    {
        private Setting? _setting;

        public Task<Setting?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_setting?.Id == id ? _setting : null);

        public Task<Setting?> GetByKeyAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_setting?.Key == key ? _setting : null);

        public Task<IReadOnlyList<Setting>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Setting>>(_setting is null ? [] : [_setting]);

        public Task AddAsync(Setting setting, CancellationToken cancellationToken = default)
        {
            _setting = setting;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Setting setting, CancellationToken cancellationToken = default)
        {
            _setting = setting;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Setting setting, CancellationToken cancellationToken = default)
        {
            _setting = null;
            return Task.CompletedTask;
        }
    }
}
