using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Application.Interfaces;

public interface IFastTravelApiRepository
{
    Task<FastTravelAggregatesDto> GetAggregatesAsync(CancellationToken cancellationToken = default);
    Task<FastTravelMemoriesDto> GetMemoriesAsync(DateOnly referenceDate, int limit, CancellationToken cancellationToken = default);
}
