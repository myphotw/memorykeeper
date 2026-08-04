using MemoryKeeper.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Infrastructure.Storage;

/// <summary>
/// Local / NAS path-based file access. Replace with ServerFileAccessService later via DI.
/// </summary>
public sealed class LocalFileAccessService : IFileAccessService
{
    private readonly ILogger<LocalFileAccessService> _logger;

    public LocalFileAccessService(ILogger<LocalFileAccessService> logger)
    {
        _logger = logger;
    }

    public string ResolveAbsolutePath(string photoRoot, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(photoRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var root = photoRoot.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var relative = ToRelativePath(relativePath)
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

        return Path.GetFullPath(Path.Combine(root, relative));
    }

    public bool PhotoRootExists(string photoRoot)
    {
        if (string.IsNullOrWhiteSpace(photoRoot))
        {
            return false;
        }

        try
        {
            return Directory.Exists(photoRoot.Trim());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PhotoRoot existence check failed. PhotoRoot={PhotoRoot}", photoRoot);
            return false;
        }
    }

    public bool FileExists(string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath))
        {
            return false;
        }

        try
        {
            return File.Exists(absolutePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "File existence check failed. Path={Path}", absolutePath);
            return false;
        }
    }

    public Task<Stream> OpenReadAsync(string absolutePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        cancellationToken.ThrowIfCancellationRequested();
        Stream stream = new FileStream(
            absolutePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    public string ToRelativePath(string path, string? photoRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var trimmed = path.Trim().Replace('\\', '/');
        if (!string.IsNullOrWhiteSpace(photoRoot))
        {
            var root = photoRoot.Trim().Replace('\\', '/').TrimEnd('/');
            if (trimmed.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed[root.Length..].TrimStart('/');
            }
        }

        // Absolute Windows path without matching root — keep filename segment after drive if possible.
        if (Path.IsPathRooted(path) && string.IsNullOrWhiteSpace(photoRoot))
        {
            // Best-effort: drop drive root, keep remaining folders.
            var withoutRoot = trimmed;
            if (withoutRoot.Length >= 2 && withoutRoot[1] == ':')
            {
                withoutRoot = withoutRoot[2..].TrimStart('/');
            }

            trimmed = withoutRoot;
        }

        return trimmed.TrimStart('/');
    }
}
