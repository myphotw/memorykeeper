namespace MemoryKeeper.Application.Interfaces;

public interface IFileStorageService
{
    /// <summary>
    /// Pending / unclassified layout: 미완성 추억/{fileName}.
    /// </summary>
    string BuildLibraryRelativePath(DateTimeOffset? capturedAt, string fileName);

    /// <summary>
    /// Classified layout: {year}/{placeDisplayName}/{fileName}.
    /// </summary>
    string BuildClassifiedRelativePath(DateTimeOffset? capturedAt, string placeDisplayName, string fileName);

    /// <summary>
    /// Copies the original file into the library. Returns the final relative library path.
    /// </summary>
    Task<string> CopyToLibraryAsync(
        string sourcePath,
        string storageRootPath,
        string libraryRelativePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a library file to a new relative path. Overwrites an orphan at the destination.
    /// Never creates a second copy for the same media (MK-042P).
    /// </summary>
    Task<string> MoveWithinLibraryAsync(
        string storageRootPath,
        string sourceRelativePath,
        string destinationRelativePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes empty directories walking upward toward (but not including) the storage root.
    /// </summary>
    void DeleteEmptyDirectoriesUpward(string? directoryPath, string storageRootPath);
}
