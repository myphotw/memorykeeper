using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Domain.Enums;

namespace MemoryKeeper.Infrastructure.Import;

public sealed class FileScanner : IFileScanner
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".heic"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4",
        ".mov"
    };

    public Task<IReadOnlyList<string>> ScanAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            throw new ArgumentException("Folder path is required.", nameof(folderPath));
        }

        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException($"Folder not found: {folderPath}");
        }

        return Task.Run(() =>
        {
            var files = Directory
                .EnumerateFiles(folderPath, "*", SearchOption.AllDirectories)
                .Where(path =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return ResolveMediaType(path) is not null;
                })
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            IReadOnlyList<string> result = files;
            return result;
        }, cancellationToken);
    }

    public MediaType? ResolveMediaType(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        if (ImageExtensions.Contains(extension))
        {
            return MediaType.Photo;
        }

        if (VideoExtensions.Contains(extension))
        {
            return MediaType.Video;
        }

        return null;
    }
}
