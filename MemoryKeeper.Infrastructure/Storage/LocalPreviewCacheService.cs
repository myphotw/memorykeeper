using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Infrastructure.Storage;

public sealed class LocalPreviewCacheService : ILocalPreviewCacheService
{
    private readonly ILogger<LocalPreviewCacheService> _logger;

    public LocalPreviewCacheService(string cacheRootPath, ILogger<LocalPreviewCacheService> logger)
    {
        if (string.IsNullOrWhiteSpace(cacheRootPath))
        {
            throw new ArgumentException("Cache root is required.", nameof(cacheRootPath));
        }

        CacheRootPath = Path.GetFullPath(cacheRootPath);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string CacheRootPath { get; }

    public Task<MaintenanceResultDto> ClearAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(CacheRootPath))
        {
            return Task.FromResult(new MaintenanceResultDto
            {
                Succeeded = true,
                Message = "PC에 저장된 임시 미리보기 데이터가 없습니다.",
                OutputPath = CacheRootPath,
            });
        }

        var deleted = 0;
        var failed = 0;
        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = false,
        };
        foreach (var file in Directory.EnumerateFiles(CacheRootPath, "*", enumeration))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                File.Delete(file);
                deleted++;
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogWarning(ex, "Failed to delete local preview cache file {CacheFile}", file);
            }
        }

        return Task.FromResult(new MaintenanceResultDto
        {
            Succeeded = failed == 0,
            Message = failed == 0
                ? $"PC에 저장된 임시 미리보기 데이터 {deleted:N0}개를 정리했습니다."
                : $"임시 미리보기 {deleted:N0}개를 정리했고 {failed:N0}개는 정리하지 못했습니다.",
            OutputPath = CacheRootPath,
        });
    }
}
