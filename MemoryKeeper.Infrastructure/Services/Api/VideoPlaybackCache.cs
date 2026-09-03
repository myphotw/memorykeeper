using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Infrastructure.Services.Api;

/// <summary>
/// Streams originals to a bounded disk cache. Only completed files are leased to the player;
/// active leases are excluded from cleanup. No video bytes enter the image cache.
/// </summary>
public sealed class VideoPlaybackCache
{
    private readonly BaseApiClient _apiClient;
    private readonly ILogger<VideoPlaybackCache> _logger;
    private readonly string _root;
    private readonly long _maxBytes;
    private readonly int _maxEntries;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, int> _pins = new(StringComparer.OrdinalIgnoreCase);

    public VideoPlaybackCache(BaseApiClient apiClient, ILogger<VideoPlaybackCache> logger)
        : this(apiClient, logger, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MemoryKeeper", "VideoPlaybackCache"), 512L * 1024 * 1024, 3) { }

    public VideoPlaybackCache(BaseApiClient apiClient, ILogger<VideoPlaybackCache> logger,
        string cacheDirectory, long maxBytes, int maxEntries)
    {
        _apiClient = apiClient;
        _logger = logger;
        _root = Path.GetFullPath(cacheDirectory);
        _maxBytes = Math.Max(0, maxBytes);
        _maxEntries = Math.Max(0, maxEntries);
    }

    public Task<Lease> AcquireAsync(Guid mediaId, string originalUrl, string? extension,
        long? expectedLength, CancellationToken cancellationToken = default) =>
        // Directory scans and file operations must not run on the viewer's UI thread.
        Task.Run(() => AcquireCoreAsync(mediaId, originalUrl, extension, expectedLength, cancellationToken), cancellationToken);

    private async Task<Lease> AcquireCoreAsync(Guid mediaId, string originalUrl, string? extension,
        long? expectedLength, CancellationToken token)
    {
        var uri = TcBackendRequestPolicy.ResolveUri(originalUrl, _apiClient.ApiBaseUrl);
        if (uri.Scheme is not ("http" or "https")) throw new ArgumentException("An HTTP original URL is required.");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{mediaId:N}|{uri.AbsoluteUri}|{expectedLength}"))).ToLowerInvariant();
        var suffix = extension?.Trim().TrimStart('.').ToLowerInvariant();
        suffix = suffix is "mp4" or "mov" or "m4v" or "avi" or "mkv" or "webm" or "wmv" or "mpg" or "mpeg" or "3gp"
            ? "." + suffix : ".media";
        var completedPath = Path.Combine(_root, hash + suffix);
        var partialPath = Path.Combine(_root, hash + "." + Guid.NewGuid().ToString("N") + ".part");

        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(_root);
            TrimCore();
            var info = new FileInfo(completedPath);
            if (!info.Exists || info.Length == 0 || (expectedLength > 0 && info.Length != expectedLength))
            {
                try
                {
                    await using (var destination = new FileStream(partialPath, FileMode.CreateNew,
                                     FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
                    {
                        await _apiClient.DownloadToAsync(uri.AbsoluteUri, destination, token).ConfigureAwait(false);
                        await destination.FlushAsync(token).ConfigureAwait(false);
                        if (destination.Length == 0 || (expectedLength > 0 && destination.Length != expectedLength))
                            throw new IOException("Incomplete video download.");
                    }
                    token.ThrowIfCancellationRequested();
                    File.Move(partialPath, completedPath, overwrite: true);
                }
                finally
                {
                    TryDelete(partialPath);
                }
            }

            token.ThrowIfCancellationRequested();
            File.SetLastWriteTimeUtc(completedPath, DateTime.UtcNow);
            lock (_pins)
                _pins[completedPath] = _pins.GetValueOrDefault(completedPath) + 1;
            TrimCore();
            return new Lease(this, completedPath);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task TrimAsync() => Task.Run(async () =>
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { TrimCore(); }
        finally { _gate.Release(); }
    });

    private void TrimCore()
    {
        try
        {
            if (!Directory.Exists(_root)) return;
            var cutoff = DateTime.UtcNow.AddDays(-1);
            var files = new DirectoryInfo(_root).GetFiles()
                .Where(file => IsOwnedFile(file.Name)).OrderBy(file => file.LastWriteTimeUtc).ToList();
            foreach (var partial in files.Where(file => file.Extension == ".part" && file.LastWriteTimeUtc < cutoff))
                TryDelete(partial.FullName);

            var completed = files.Where(file => file.Extension != ".part").ToList();
            var bytes = completed.Sum(file => file.Length);
            var count = completed.Count;
            foreach (var file in completed)
            {
                lock (_pins)
                    if (_pins.ContainsKey(file.FullName)) continue;
                if (file.LastWriteTimeUtc >= cutoff && bytes <= _maxBytes && count <= _maxEntries) continue;
                if (TryDelete(file.FullName))
                {
                    bytes -= file.Length;
                    count--;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Video cache cleanup unavailable. ErrorType={ErrorType}", ex.GetType().Name);
        }
    }

    private static bool IsOwnedFile(string name)
    {
        var key = name.Split('.')[0];
        return key.Length == 64 && key.All(Uri.IsHexDigit);
    }

    private bool TryDelete(string path)
    {
        try { File.Delete(path); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug("Video cache file retained. ErrorType={ErrorType}", ex.GetType().Name);
            return false;
        }
    }

    private void Release(string path)
    {
        lock (_pins)
        {
            if (_pins[path] == 1) _pins.Remove(path);
            else _pins[path]--;
        }
        _ = TrimAsync();
    }

    public sealed class Lease : IDisposable
    {
        private VideoPlaybackCache? _owner;
        internal Lease(VideoPlaybackCache owner, string path) { _owner = owner; Path = path; }
        public string Path { get; }
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release(Path);
    }
}
