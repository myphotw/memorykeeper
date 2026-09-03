using MemoryKeeper.Domain.Enums;

namespace MemoryKeeper.Application;

/// <summary>Shared API media classification; missing metadata retains the legacy photo default.</summary>
public static class MediaTypeResolver
{
    public static MediaType Resolve(string? mimeType, string? extension, string? filename)
    {
        var mime = mimeType?.Trim();
        if (mime?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true) return MediaType.Video;
        if (mime?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true) return MediaType.Photo;

        var suffix = extension?.Trim();
        if (string.IsNullOrEmpty(suffix)) suffix = Path.GetExtension(filename);
        return suffix?.TrimStart('.').ToLowerInvariant() is
            "mp4" or "mov" or "m4v" or "avi" or "mkv" or "webm" or "wmv" or "mpg" or "mpeg" or "3gp"
            ? MediaType.Video
            : MediaType.Photo;
    }
}
