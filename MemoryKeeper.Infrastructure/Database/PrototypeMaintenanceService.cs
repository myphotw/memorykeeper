using System.IO.Compression;
using System.Text.Json;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Infrastructure.Database;

public sealed class PrototypeMaintenanceService : IPrototypeMaintenanceService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _databaseDirectory;
    private readonly string _thumbnailCachePath;
    private readonly ILogger<PrototypeMaintenanceService> _logger;

    public PrototypeMaintenanceService(
        IServiceScopeFactory scopeFactory,
        string databaseDirectory,
        string thumbnailCachePath,
        ILogger<PrototypeMaintenanceService> logger)
    {
        _scopeFactory = scopeFactory;
        _databaseDirectory = databaseDirectory;
        _thumbnailCachePath = thumbnailCachePath;
        _logger = logger;
    }

    public string DatabasePath => Path.Combine(_databaseDirectory, SqliteConnectionFactory.DatabaseFileName);

    public string ThumbnailCachePath => _thumbnailCachePath;

    public async Task<MaintenanceResultDto> ResetDatabaseAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("Resetting database at {DatabasePath}", DatabasePath);

        await DisposeOpenContextsAsync(cancellationToken);
        SqliteConnection.ClearAllPools();

        DeleteIfExists(DatabasePath);
        DeleteIfExists(DatabasePath + "-wal");
        DeleteIfExists(DatabasePath + "-shm");

        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            await DatabaseInitializer.InitializeAsync(
                scope.ServiceProvider,
                _databaseDirectory,
                cancellationToken);
        }

        return new MaintenanceResultDto
        {
            Succeeded = true,
            Message = "전체 초기화가 완료되었습니다. (사진 원본은 유지, 설정/API Key 포함 DB 초기화)",
            OutputPath = DatabasePath
        };
    }

    public Task<MaintenanceResultDto> ClearThumbnailCacheAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(_thumbnailCachePath))
        {
            return Task.FromResult(new MaintenanceResultDto
            {
                Succeeded = true,
                Message = "Thumbnail cache가 없습니다.",
                OutputPath = _thumbnailCachePath
            });
        }

        var deleted = 0;
        foreach (var file in Directory.EnumerateFiles(_thumbnailCachePath, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                File.Delete(file);
                deleted++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete thumbnail cache file {File}", file);
            }
        }

        return Task.FromResult(new MaintenanceResultDto
        {
            Succeeded = true,
            Message = $"Thumbnail cache {deleted}개 파일을 삭제했습니다. (API Key 유지)",
            OutputPath = _thumbnailCachePath
        });
    }

    public async Task<MaintenanceResultDto> ClearImportDataAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MemoryKeeperDbContext>();

        // FK order: media_tag -> media -> place; tags independent after media_tag
        await db.Database.ExecuteSqlRawAsync("DELETE FROM TB_MEDIA_TAG;", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM TB_MEDIA;", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM TB_PLACE;", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM TB_TAG;", cancellationToken);

        _logger.LogWarning("Import data cleared (MEDIA/PLACE/MEDIA_TAG/TAG). Photo originals kept.");

        return new MaintenanceResultDto
        {
            Succeeded = true,
            Message = "사진등록 데이터(Media/Place/Tag)를 초기화했습니다. 사진 원본과 Storage/설정(API Key 포함)은 유지됩니다."
        };
    }

    public async Task<MaintenanceResultDto> RegeneratePlacesAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MemoryKeeperDbContext>();
        var placeAssignment = scope.ServiceProvider.GetRequiredService<Application.Services.PlaceAssignmentService>();
        var mediaRepository = scope.ServiceProvider.GetRequiredService<IMediaRepository>();

        // Clear assignments then places; re-assign from GPS using existing PlaceAssignmentService.
        await db.Database.ExecuteSqlRawAsync("UPDATE TB_MEDIA SET PlaceId = NULL;", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM TB_PLACE;", cancellationToken);

        var withGps = await mediaRepository.GetWithGpsAsync(cancellationToken);
        var assigned = 0;

        foreach (var media in withGps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (media.Latitude is not double lat || media.Longitude is not double lon)
            {
                continue;
            }

            var place = await placeAssignment.AssignAsync(lat, lon, cancellationToken);
            media.PlaceId = place.Id;
            media.UpdatedAt = DateTime.UtcNow;
            if (media.Status == Domain.Enums.MediaStatus.Pending)
            {
                media.Status = Domain.Enums.MediaStatus.Imported;
            }

            await mediaRepository.UpdateAsync(media, cancellationToken);
            assigned++;
        }

        _logger.LogWarning(
            "Places regenerated. Assigned={Assigned}, Candidates={Candidates}",
            assigned,
            withGps.Count);

        return new MaintenanceResultDto
        {
            Succeeded = true,
            Message = $"장소/여행기록을 재생성했습니다. 배정 {assigned}건. (API Key 유지)"
        };
    }

    public async Task<MaintenanceResultDto> BackupAsync(
        string zipFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipFilePath);

        if (!File.Exists(DatabasePath))
        {
            return new MaintenanceResultDto
            {
                Succeeded = false,
                Message = $"Database 파일이 없습니다: {DatabasePath}"
            };
        }

        await DisposeOpenContextsAsync(cancellationToken);
        SqliteConnection.ClearAllPools();

        var directory = Path.GetDirectoryName(zipFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(zipFilePath))
        {
            File.Delete(zipFilePath);
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "MemoryKeeperBackup_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var dbCopy = Path.Combine(tempDir, SqliteConnectionFactory.DatabaseFileName);
            File.Copy(DatabasePath, dbCopy, overwrite: true);

            var manifest = new
            {
                App = "MemoryKeeper",
                CreatedAtUtc = DateTime.UtcNow,
                Includes = new[] { "MemoryKeeper.db", "Settings", "Tags", "Places" },
                Note = "Photo originals are managed separately and are not included in this backup."
            };
            await File.WriteAllTextAsync(
                Path.Combine(tempDir, "manifest.json"),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken);

            ZipFile.CreateFromDirectory(tempDir, zipFilePath, CompressionLevel.Optimal, includeBaseDirectory: false);

            _logger.LogInformation("Backup created at {ZipPath}", zipFilePath);
            return new MaintenanceResultDto
            {
                Succeeded = true,
                Message = "Backup이 생성되었습니다. (DB + 설정/태그/Place)",
                OutputPath = zipFilePath
            };
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                // ignore cleanup failures
            }
        }
    }

    public async Task<MaintenanceResultDto> RestoreAsync(
        string zipFilePath,
        bool backupExistingDatabase,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipFilePath);

        if (!File.Exists(zipFilePath))
        {
            return new MaintenanceResultDto
            {
                Succeeded = false,
                Message = $"Backup 파일이 없습니다: {zipFilePath}"
            };
        }

        await DisposeOpenContextsAsync(cancellationToken);
        SqliteConnection.ClearAllPools();

        string? existingBackupPath = null;
        if (backupExistingDatabase && File.Exists(DatabasePath))
        {
            existingBackupPath = DatabasePath + ".pre-restore-" + DateTime.Now.ToString("yyyyMMddHHmmss");
            File.Copy(DatabasePath, existingBackupPath, overwrite: true);
        }

        var extractDir = Path.Combine(Path.GetTempPath(), "MemoryKeeperRestore_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractDir);

        try
        {
            ZipFile.ExtractToDirectory(zipFilePath, extractDir, overwriteFiles: true);
            var restoredDb = Path.Combine(extractDir, SqliteConnectionFactory.DatabaseFileName);
            if (!File.Exists(restoredDb))
            {
                return new MaintenanceResultDto
                {
                    Succeeded = false,
                    Message = "Backup zip에 MemoryKeeper.db가 없습니다."
                };
            }

            Directory.CreateDirectory(_databaseDirectory);
            File.Copy(restoredDb, DatabasePath, overwrite: true);
            DeleteIfExists(DatabasePath + "-wal");
            DeleteIfExists(DatabasePath + "-shm");

            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                await DatabaseInitializer.InitializeAsync(
                    scope.ServiceProvider,
                    _databaseDirectory,
                    cancellationToken);
            }

            var message = existingBackupPath is null
                ? "Backup에서 복원했습니다."
                : $"Backup에서 복원했습니다. 기존 DB: {existingBackupPath}";

            _logger.LogInformation("Database restored from {ZipPath}", zipFilePath);
            return new MaintenanceResultDto
            {
                Succeeded = true,
                Message = message,
                OutputPath = DatabasePath
            };
        }
        finally
        {
            try
            {
                Directory.Delete(extractDir, recursive: true);
            }
            catch
            {
                // ignore
            }
        }
    }

    private async Task DisposeOpenContextsAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MemoryKeeperDbContext>();
        await db.Database.CloseConnectionAsync();
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
