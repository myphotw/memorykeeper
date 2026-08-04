using System.Collections.Concurrent;
using MemoryKeeper.Application.Interfaces;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace MemoryKeeper.App.Services;

public sealed class ThumbnailService : IThumbnailService
{
    public const int DefaultMaxEdgePixels = 320;

    private readonly IFileAccessService _fileAccessService;
    private readonly ILogger<ThumbnailService> _logger;
    private readonly ConcurrentDictionary<Guid, Task<string?>> _inFlight = new();
    private readonly SemaphoreSlim _generateLock = new(4, 4);
    private readonly int _maxEdgePixels;

    public ThumbnailService(
        IFileAccessService fileAccessService,
        ILogger<ThumbnailService> logger,
        int maxEdgePixels = DefaultMaxEdgePixels)
    {
        _fileAccessService = fileAccessService;
        _logger = logger;
        _maxEdgePixels = maxEdgePixels <= 0 ? DefaultMaxEdgePixels : maxEdgePixels;
        CacheRootPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MemoryKeeper",
            "ThumbnailCache");
    }

    public string CacheRootPath { get; }

    public void DeleteThumbnail(Guid mediaId)
    {
        var cachePath = GetCachePath(mediaId);
        if (!File.Exists(cachePath))
        {
            return;
        }

        try
        {
            File.Delete(cachePath);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to delete thumbnail cache. MediaId={MediaId}", mediaId);
        }
    }

    public Task<string?> GetOrCreateThumbnailAsync(
        Guid mediaId,
        string sourceAbsolutePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceAbsolutePath))
        {
            _logger.LogWarning(
                "Thumbnail skipped: empty source path. MediaId={MediaId}",
                mediaId);
            return Task.FromResult<string?>(null);
        }

        var cachePath = GetCachePath(mediaId);
        if (File.Exists(cachePath))
        {
            return Task.FromResult<string?>(cachePath);
        }

        return _inFlight.GetOrAdd(
            mediaId,
            id => GenerateAndTrackAsync(id, sourceAbsolutePath, cachePath, cancellationToken));
    }

    private async Task<string?> GenerateAndTrackAsync(
        Guid mediaId,
        string sourceAbsolutePath,
        string cachePath,
        CancellationToken cancellationToken)
    {
        try
        {
            return await GenerateAsync(mediaId, sourceAbsolutePath, cachePath, cancellationToken);
        }
        finally
        {
            _inFlight.TryRemove(mediaId, out _);
        }
    }

    private async Task<string?> GenerateAsync(
        Guid mediaId,
        string sourceAbsolutePath,
        string cachePath,
        CancellationToken cancellationToken)
    {
        if (File.Exists(cachePath))
        {
            return cachePath;
        }

        if (!_fileAccessService.FileExists(sourceAbsolutePath))
        {
            _logger.LogWarning(
                "Thumbnail source file not found. MediaId={MediaId}, Path={Path}",
                mediaId,
                sourceAbsolutePath);
            return null;
        }

        await _generateLock.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(cachePath))
            {
                return cachePath;
            }

            Directory.CreateDirectory(CacheRootPath);

            var tempPath = cachePath + ".tmp";
            try
            {
                await using (var sourceStream = await _fileAccessService.OpenReadAsync(sourceAbsolutePath, cancellationToken))
                using (var image = await Image.LoadAsync(sourceStream, cancellationToken))
                {
                    image.Mutate(context => context.Resize(new ResizeOptions
                    {
                        Mode = ResizeMode.Max,
                        Size = new Size(_maxEdgePixels, _maxEdgePixels)
                    }));

                    await image.SaveAsJpegAsync(
                        tempPath,
                        new JpegEncoder { Quality = 82 },
                        cancellationToken);
                }

                File.Move(tempPath, cachePath, overwrite: true);
                return cachePath;
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch (IOException)
                    {
                        // Best-effort cleanup of temp file.
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Failed to create thumbnail. MediaId={MediaId}, Path={Path}",
                mediaId,
                sourceAbsolutePath);
            return null;
        }
        finally
        {
            _generateLock.Release();
        }
    }

    private string GetCachePath(Guid mediaId)
    {
        return Path.Combine(CacheRootPath, $"{mediaId:N}_{_maxEdgePixels}.jpg");
    }
}
