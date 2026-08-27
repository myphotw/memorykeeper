using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Application.Interfaces;

public interface IImportBackendIdentityProvider
{
    Task<ImportBackendIdentitySnapshot> LoadAsync(CancellationToken cancellationToken = default);
}
