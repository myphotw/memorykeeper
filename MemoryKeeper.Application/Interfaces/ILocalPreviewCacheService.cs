using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Application.Interfaces;

/// <summary>Deletes only the app's PC-local generated preview cache.</summary>
public interface ILocalPreviewCacheService
{
    string CacheRootPath { get; }
    Task<MaintenanceResultDto> ClearAsync(CancellationToken cancellationToken = default);
}
