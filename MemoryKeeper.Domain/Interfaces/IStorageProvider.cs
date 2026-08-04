namespace MemoryKeeper.Domain.Interfaces;

public interface IStorageProvider
{
    Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default);

    Task CopyAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a file within storage. Creates the destination directory if needed.
    /// </summary>
    Task MoveAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default);
}
