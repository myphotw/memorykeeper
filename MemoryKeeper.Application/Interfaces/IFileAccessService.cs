namespace MemoryKeeper.Application.Interfaces;

/// <summary>
/// Abstracts photo file access so Local / NAS / future Server implementations can be swapped.
/// UI must not open library files directly.
/// </summary>
public interface IFileAccessService
{
    /// <summary>
    /// Builds an absolute path from PhotoRoot + RelativePath.
    /// RelativePath uses forward-slash storage form (e.g. 2026/Osaka/IMG0001.jpg).
    /// </summary>
    string ResolveAbsolutePath(string photoRoot, string relativePath);

    bool PhotoRootExists(string photoRoot);

    bool FileExists(string absolutePath);

    Task<Stream> OpenReadAsync(string absolutePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Normalizes a library-relative path to forward-slash form without a leading slash.
    /// If <paramref name="path"/> is absolute under <paramref name="photoRoot"/>, strips the root.
    /// </summary>
    string ToRelativePath(string path, string? photoRoot = null);
}
