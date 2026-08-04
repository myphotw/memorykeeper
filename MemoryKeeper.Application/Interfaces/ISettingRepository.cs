using MemoryKeeper.Domain.Entities;

namespace MemoryKeeper.Application.Interfaces;

public interface ISettingRepository
{
    Task<Setting?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Setting?> GetByKeyAsync(string key, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Setting>> GetAllAsync(CancellationToken cancellationToken = default);

    Task AddAsync(Setting setting, CancellationToken cancellationToken = default);

    Task UpdateAsync(Setting setting, CancellationToken cancellationToken = default);

    Task DeleteAsync(Setting setting, CancellationToken cancellationToken = default);
}
