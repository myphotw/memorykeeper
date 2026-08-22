using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Application.Interfaces;

public interface IBackendChangeFeed
{
    Task<BackendChangeFeedDto> GetChangesAsync(
        long cursor,
        int limit = 500,
        CancellationToken cancellationToken = default);
}
