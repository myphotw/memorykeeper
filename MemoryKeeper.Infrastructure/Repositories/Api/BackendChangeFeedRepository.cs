using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Infrastructure.Services.Api;

namespace MemoryKeeper.Infrastructure.Repositories.Api;

public sealed class BackendChangeFeedRepository : IBackendChangeFeed
{
    private readonly BaseApiClient _apiClient;

    public BackendChangeFeedRepository(BaseApiClient apiClient)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    }

    public async Task<BackendChangeFeedDto> GetChangesAsync(
        long cursor,
        int limit = 500,
        CancellationToken cancellationToken = default)
    {
        var serviceName = Uri.EscapeDataString(_apiClient.ServiceName);
        var path = $"/api/common/changes?cursor={Math.Max(0, cursor)}&limit={Math.Clamp(limit, 1, 500)}&service_name={serviceName}";
        var response = await _apiClient.GetAsync<BackendChangeFeedDto>(path, cancellationToken)
            .ConfigureAwait(false);
        return response.Data ?? new BackendChangeFeedDto { NextCursor = cursor };
    }
}
