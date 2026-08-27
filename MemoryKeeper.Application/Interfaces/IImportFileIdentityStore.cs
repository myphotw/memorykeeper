using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Application.Interfaces;

public interface IImportFileIdentityStore
{
    Task<IReadOnlyList<ImportFileIdentityDto>> ResolveAsync(
        IReadOnlyList<string> filePaths,
        IProgress<ImportPreflightProgressDto>? progress = null,
        bool forceRecheck = false,
        CancellationToken cancellationToken = default);
}
