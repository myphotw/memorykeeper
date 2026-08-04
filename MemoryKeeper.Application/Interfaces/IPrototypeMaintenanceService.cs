using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Application.Interfaces;

public interface IPrototypeMaintenanceService
{
    string DatabasePath { get; }

    string ThumbnailCachePath { get; }

    /// <summary>
    /// Full reset: deletes SQLite database and remigrates. Clears Settings including API Key.
    /// Photo originals are kept.
    /// </summary>
    Task<MaintenanceResultDto> ResetDatabaseAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes thumbnail cache files under LocalAppData. Photo originals are kept.
    /// </summary>
    Task<MaintenanceResultDto> ClearThumbnailCacheAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears TB_MEDIA, TB_PLACE, TB_MEDIA_TAG, TB_TAG. Storages/Settings and photo files remain.
    /// Google API Key is preserved.
    /// </summary>
    Task<MaintenanceResultDto> ClearImportDataAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears Place assignments and recreates Places from media GPS using existing assignment logic.
    /// Visit/Travel views are derived from Media/Place. Google API Key is preserved.
    /// </summary>
    Task<MaintenanceResultDto> RegeneratePlacesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports MemoryKeeper.db (+ manifest) into a zip. Photo originals are not included.
    /// </summary>
    Task<MaintenanceResultDto> BackupAsync(string zipFilePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores from zip. Optionally backs up the current DB first.
    /// </summary>
    Task<MaintenanceResultDto> RestoreAsync(
        string zipFilePath,
        bool backupExistingDatabase,
        CancellationToken cancellationToken = default);
}
