using System.Globalization;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Infrastructure.Services.Api;

namespace MemoryKeeper.Infrastructure.Repositories.Api;

public sealed class FastTravelApiRepository : IFastTravelApiRepository
{
    private readonly BaseApiClient _apiClient;
    public FastTravelApiRepository(BaseApiClient apiClient) => _apiClient = apiClient;

    public async Task<FastTravelAggregatesDto> GetAggregatesAsync(CancellationToken cancellationToken = default) =>
        (await _apiClient.GetAsync<FastTravelAggregatesDto>("/api/memorykeeper/travel/aggregates", cancellationToken).ConfigureAwait(false)).Data
        ?? new FastTravelAggregatesDto();

    public async Task<FastTravelMemoriesDto> GetMemoriesAsync(DateOnly referenceDate, int limit, CancellationToken cancellationToken = default)
    {
        var safeLimit = Math.Clamp(limit, 1, 100);
        var path = $"/api/memorykeeper/travel/memories?reference_date={Uri.EscapeDataString(referenceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))}&limit={safeLimit}";
        return (await _apiClient.GetAsync<FastTravelMemoriesDto>(path, cancellationToken).ConfigureAwait(false)).Data
               ?? new FastTravelMemoriesDto();
    }
}
