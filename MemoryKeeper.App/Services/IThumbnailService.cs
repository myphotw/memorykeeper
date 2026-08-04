namespace MemoryKeeper.App.Services;

public interface IThumbnailService
{
    string CacheRootPath { get; }

    /// <summary>
    /// Returns a local cache path for the media thumbnail.
    /// Creates and caches a resized JPEG on first request without modifying the source file.
    /// </summary>
    Task<string?> GetOrCreateThumbnailAsync(
        Guid mediaId,
        string sourceAbsolutePath,
        CancellationToken cancellationToken = default);

    void DeleteThumbnail(Guid mediaId);
}
