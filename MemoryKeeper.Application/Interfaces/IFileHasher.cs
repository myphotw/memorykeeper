namespace MemoryKeeper.Application.Interfaces;

public interface IFileHasher
{
    Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken = default);
}
