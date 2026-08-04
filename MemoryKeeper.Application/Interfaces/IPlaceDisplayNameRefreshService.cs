using MemoryKeeper.Domain.Entities;

namespace MemoryKeeper.Application.Interfaces;

public interface IPlaceDisplayNameRefreshService
{
    Task<int> RefreshKoreanNamesAsync(
        IEnumerable<Place> places,
        CancellationToken cancellationToken = default);
}
