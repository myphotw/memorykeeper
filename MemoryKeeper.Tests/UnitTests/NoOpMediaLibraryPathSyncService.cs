using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Domain.Entities;

namespace MemoryKeeper.Tests.UnitTests;

internal sealed class NoOpMediaLibraryPathSyncService : IMediaLibraryPathSyncService
{
    public Task<bool> SyncMediaPathAsync(
        Media media,
        Place? place,
        CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public Task<int> SyncAllAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(0);

    public Task<int> SyncPlaceMediaAsync(Guid placeId, CancellationToken cancellationToken = default)
        => Task.FromResult(0);
}
