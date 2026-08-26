using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Application.Interfaces;

/// <summary>JSON persistence for in-flight Import job_ids (no SQLite).</summary>
public interface IImportJobSessionStore
{
    Task SaveAsync(IReadOnlyList<ImportSessionJobDto> jobs, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically replaces only the caller-owned job ids, preserving jobs monitored by other import sessions.
    /// </summary>
    Task UpdateAsync(
        IReadOnlyList<ImportSessionJobDto> openJobs,
        IReadOnlyCollection<string> managedJobIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ImportSessionJobDto>> LoadAsync(CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}
