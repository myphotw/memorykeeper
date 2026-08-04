using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Services;

namespace MemoryKeeper.Application.UseCases;

public sealed class GetLibraryUseCase
{
    private readonly MediaService _mediaService;

    public GetLibraryUseCase(MediaService mediaService)
    {
        _mediaService = mediaService;
    }

    public Task<IReadOnlyList<MediaDto>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        return _mediaService.GetLibraryAsync(cancellationToken);
    }
}
