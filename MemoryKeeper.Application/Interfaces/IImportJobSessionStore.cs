using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Application.Interfaces;

/// <summary>JSON persistence for in-flight Import job_ids (no SQLite).</summary>
public interface IImportJobSessionStore
{
    Task SaveAsync(IReadOnlyList<ImportSessionJobDto> jobs, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ImportSessionJobDto>> LoadAsync(CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}
