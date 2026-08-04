using MemoryKeeper.Domain.Enums;

namespace MemoryKeeper.Application.Interfaces;

public interface IFileScanner
{
    Task<IReadOnlyList<string>> ScanAsync(string folderPath, CancellationToken cancellationToken = default);

    MediaType? ResolveMediaType(string filePath);
}
