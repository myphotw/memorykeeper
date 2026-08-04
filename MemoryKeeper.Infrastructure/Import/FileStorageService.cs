using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Domain.Interfaces;
using System.Text;

namespace MemoryKeeper.Infrastructure.Import;

public sealed class FileStorageService : IFileStorageService
{
    /// <summary>
    /// Pending / unclassified photos live under a single folder at the storage root.
    /// </summary>
    public const string IncompleteMemoriesFolder = "미완성 추억";

    private const int MaxFolderNameLength = 80;

    private readonly IStorageProvider _storageProvider;
    private readonly IFileAccessService _fileAccessService;

    public FileStorageService(
        IStorageProvider storageProvider,
        IFileAccessService fileAccessService)
    {
        _storageProvider = storageProvider;
        _fileAccessService = fileAccessService;
    }

    public string BuildLibraryRelativePath(DateTimeOffset? capturedAt, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        _ = capturedAt;
        var safeFileName = Path.GetFileName(fileName);
        return _fileAccessService.ToRelativePath($"{IncompleteMemoriesFolder}/{safeFileName}");
    }

    public string BuildClassifiedRelativePath(DateTimeOffset? capturedAt, string placeDisplayName, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(placeDisplayName);

        var year = (capturedAt ?? DateTimeOffset.UtcNow).Year;
        var placeFolder = SanitizeFolderName(placeDisplayName);
        var safeFileName = Path.GetFileName(fileName);
        return _fileAccessService.ToRelativePath($"{year}/{placeFolder}/{safeFileName}");
    }

    public async Task<string> CopyToLibraryAsync(
        string sourcePath,
        string storageRootPath,
        string libraryRelativePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageRootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryRelativePath);

        var normalizedRelativePath = _fileAccessService.ToRelativePath(libraryRelativePath, storageRootPath);
        var destinationPath = _fileAccessService.ResolveAbsolutePath(storageRootPath, normalizedRelativePath);

        // First-time registration only: uniquify if another file already occupies the slot.
        normalizedRelativePath = await EnsureUniqueRelativePathAsync(
            storageRootPath,
            normalizedRelativePath,
            destinationPath,
            cancellationToken);

        destinationPath = _fileAccessService.ResolveAbsolutePath(storageRootPath, normalizedRelativePath);
        await _storageProvider.CopyAsync(sourcePath, destinationPath, cancellationToken);
        return normalizedRelativePath;
    }

    public async Task<string> MoveWithinLibraryAsync(
        string storageRootPath,
        string sourceRelativePath,
        string destinationRelativePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageRootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRelativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRelativePath);

        var sourceRelative = _fileAccessService.ToRelativePath(sourceRelativePath, storageRootPath);
        var destinationRelative = _fileAccessService.ToRelativePath(destinationRelativePath, storageRootPath);
        if (string.Equals(sourceRelative, destinationRelative, StringComparison.OrdinalIgnoreCase))
        {
            return sourceRelative;
        }

        var sourceAbsolute = _fileAccessService.ResolveAbsolutePath(storageRootPath, sourceRelative);
        var destinationAbsolute = _fileAccessService.ResolveAbsolutePath(storageRootPath, destinationRelative);

        // MK-042P: place change must Move to the canonical target path — never Create a second copy via uniquify.
        await _storageProvider.MoveAsync(sourceAbsolute, destinationAbsolute, cancellationToken);
        TryDeleteEmptyDirectoriesUpward(Path.GetDirectoryName(sourceAbsolute), storageRootPath);
        return destinationRelative;
    }

    public void DeleteEmptyDirectoriesUpward(string? directoryPath, string storageRootPath) =>
        TryDeleteEmptyDirectoriesUpward(directoryPath, storageRootPath);

    private async Task<string> EnsureUniqueRelativePathAsync(
        string storageRootPath,
        string relativePath,
        string absolutePath,
        CancellationToken cancellationToken)
    {
        if (!await _storageProvider.ExistsAsync(absolutePath, cancellationToken))
        {
            return relativePath;
        }

        var fileName = Path.GetFileNameWithoutExtension(absolutePath);
        var extension = Path.GetExtension(absolutePath);
        var directory = Path.GetDirectoryName(absolutePath) ?? storageRootPath;
        var uniqueFileName = $"{fileName}_{DateTime.UtcNow:yyyyMMddHHmmssfff}{extension}";
        var uniqueAbsolute = Path.Combine(directory, uniqueFileName);
        return _fileAccessService.ToRelativePath(
            Path.GetRelativePath(storageRootPath, uniqueAbsolute),
            storageRootPath);
    }

    internal static string SanitizeFolderName(string displayName)
    {
        var builder = new StringBuilder(displayName.Trim().Length);
        foreach (var ch in displayName.Trim())
        {
            if (ch is '/' or '\\' || Array.IndexOf(Path.GetInvalidFileNameChars(), ch) >= 0)
            {
                builder.Append('_');
            }
            else
            {
                builder.Append(ch);
            }
        }

        var sanitized = builder.ToString().Trim().TrimEnd('.', ' ');
        while (sanitized.Contains("  ", StringComparison.Ordinal))
        {
            sanitized = sanitized.Replace("  ", " ", StringComparison.Ordinal);
        }

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return "Unknown";
        }

        if (sanitized.Length > MaxFolderNameLength)
        {
            sanitized = sanitized[..MaxFolderNameLength].TrimEnd('.', ' ');
        }

        return string.IsNullOrWhiteSpace(sanitized) ? "Unknown" : sanitized;
    }

    private static void TryDeleteEmptyDirectoriesUpward(string? directoryPath, string storageRootPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || string.IsNullOrWhiteSpace(storageRootPath))
        {
            return;
        }

        string rootFull;
        try
        {
            rootFull = Path.GetFullPath(storageRootPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return;
        }

        var current = directoryPath;
        while (!string.IsNullOrWhiteSpace(current))
        {
            string currentFull;
            try
            {
                currentFull = Path.GetFullPath(current)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                break;
            }

            if (string.Equals(currentFull, rootFull, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (!currentFull.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !currentFull.StartsWith(rootFull + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            try
            {
                if (!Directory.Exists(currentFull))
                {
                    break;
                }

                if (Directory.EnumerateFileSystemEntries(currentFull).Any())
                {
                    break;
                }

                Directory.Delete(currentFull);
            }
            catch
            {
                break;
            }

            current = Path.GetDirectoryName(currentFull);
        }
    }
}
