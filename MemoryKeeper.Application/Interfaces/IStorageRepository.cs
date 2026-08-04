using MemoryKeeper.Domain.Entities;

namespace MemoryKeeper.Application.Interfaces;

public interface IStorageRepository
{
    Task<Storage?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Storage>> GetAllAsync(CancellationToken cancellationToken = default);

    Task AddAsync(Storage storage, CancellationToken cancellationToken = default);

    Task UpdateAsync(Storage storage, CancellationToken cancellationToken = default);

    Task DeleteAsync(Storage storage, CancellationToken cancellationToken = default);
}
