using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Application.Interfaces;

public interface IMetadataExtractor
{
    Task<MediaMetadataDto> ExtractAsync(string filePath, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, string>> DumpTagsAsync(
        string filePath,
        CancellationToken cancellationToken = default);
}
