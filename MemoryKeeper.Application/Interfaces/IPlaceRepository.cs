using MemoryKeeper.Domain.Entities;

namespace MemoryKeeper.Application.Interfaces;

public interface IPlaceRepository
{
    Task<Place?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Place>> GetByIdsAsync(
        IReadOnlyCollection<Guid> placeIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Place>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Place>> GetActiveAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Place>> SearchAsync(string keyword, CancellationToken cancellationToken = default);

    Task AddAsync(Place place, CancellationToken cancellationToken = default);

    Task UpdateAsync(Place place, CancellationToken cancellationToken = default);

    Task DeleteAsync(Place place, CancellationToken cancellationToken = default);
}
